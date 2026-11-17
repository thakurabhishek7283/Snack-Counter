using System.Globalization;

namespace SnackCounter.Domain.Common;

/// <summary>ISO 4217 code. Deliberately data, not an enum: a new office needs no code change.</summary>
public readonly record struct Currency
{
    private Currency(string code) => Code = code;

    public string Code { get; }

    public static Currency Of(string code) =>
        TryParse(code, out var currency) ? currency : throw new ArgumentException($"Invalid ISO currency code '{code}'.", nameof(code));

    public static bool TryParse(string? code, out Currency currency)
    {
        var normalised = code?.Trim().ToUpperInvariant();
        if (normalised is { Length: 3 } && normalised.All(char.IsAsciiLetterUpper))
        {
            currency = new Currency(normalised);
            return true;
        }
        currency = default;
        return false;
    }

    public override string ToString() => Code;
}

public sealed class CurrencyMismatchException(Currency left, Currency right)
    : InvalidOperationException($"Cannot combine {left} and {right}. Convert explicitly first.");

/// <summary>
/// An amount in a specific currency. Arithmetic across currencies throws, because mixing NOK and INR
/// is a programming error. Conversion happens only in reporting, through <see cref="ConvertTo"/>.
/// </summary>
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money Zero(Currency currency) => new(0m, currency);
    public static Money Of(decimal amount, string currency) => new(amount, Currency.Of(currency));

    public Money Add(Money other) => this with { Amount = Amount + Same(other).Amount };
    public Money Subtract(Money other) => this with { Amount = Amount - Same(other).Amount };
    public int CompareTo(Money other) => Amount.CompareTo(Same(other).Amount);

    public static Money operator +(Money a, Money b) => a.Add(b);
    public static Money operator -(Money a, Money b) => a.Subtract(b);
    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    public static Money Min(Money a, Money b) => a <= b ? a : b;

    public static Money Sum(IEnumerable<Money> amounts, Currency currency) =>
        amounts.Aggregate(Zero(currency), (total, next) => total + next);

    public bool IsPositive => Amount > 0;

    /// <summary>Reporting only. Rounds to 2 decimals, midpoint away from zero.</summary>
    public Money ConvertTo(Currency target, decimal rate)
    {
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate), "FX rate must be positive.");
        return new Money(decimal.Round(Amount * rate, 2, MidpointRounding.AwayFromZero), target);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency.Code}");

    private Money Same(Money other) =>
        other.Currency == Currency ? other : throw new CurrencyMismatchException(Currency, other.Currency);
}
