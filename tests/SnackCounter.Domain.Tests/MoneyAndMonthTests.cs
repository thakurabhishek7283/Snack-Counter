using Shouldly;
using SnackCounter.Domain.Common;
using static SnackCounter.Domain.Tests.TestData;

namespace SnackCounter.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Adding_same_currency_sums_amounts() =>
        (NOK(300) + NOK(850.50m)).ShouldBe(NOK(1150.50m));

    [Fact]
    public void Adding_different_currencies_throws() =>
        Should.Throw<CurrencyMismatchException>(() => NOK(100) + INR(100));

    [Fact]
    public void Comparing_different_currencies_throws() =>
        Should.Throw<CurrencyMismatchException>(() => NOK(100) < INR(100));

    [Fact]
    public void Conversion_rounds_to_two_decimals_midpoint_away_from_zero() =>
        INR(1001m).ConvertTo(Nok, 0.1255m).ShouldBe(NOK(125.63m));

    [Fact]
    public void Sum_of_nothing_is_zero_in_the_given_currency() =>
        Money.Sum([], Nok).ShouldBe(Money.Zero(Nok));

    [Theory]
    [InlineData("nok", true)]
    [InlineData("NOK", true)]
    [InlineData("NO", false)]
    [InlineData("N0K", false)]
    [InlineData("", false)]
    public void Currency_codes_are_validated(string code, bool valid) =>
        Currency.TryParse(code, out _).ShouldBe(valid);
}

public class BudgetMonthTests
{
    [Fact]
    public void Month_boundary_uses_the_office_time_zone()
    {
        // 22:30 UTC on 31 Oct = 23:30 in Oslo (still October) but 04:00 on 1 Nov in Bengaluru.
        var instant = new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero);

        BudgetMonth.From(instant, Oslo.TimeZone).ShouldBe(new BudgetMonth(2026, 10));
        BudgetMonth.From(instant, Bengaluru.TimeZone).ShouldBe(new BudgetMonth(2026, 11));
    }

    [Fact]
    public void Utc_range_starts_and_ends_at_local_midnight()
    {
        var (start, end) = new BudgetMonth(2026, 11).ToUtcRange(Oslo.TimeZone);

        start.ShouldBe(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero));
        end.ShouldBe(new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parses_and_formats_iso_year_month()
    {
        var month = BudgetMonth.Parse("2026-12");
        month.ToString().ShouldBe("2026-12");
        month.Next().ShouldBe(new BudgetMonth(2027, 1));
        BudgetMonth.TryParse("2026-13", out _).ShouldBeFalse();
    }
}
