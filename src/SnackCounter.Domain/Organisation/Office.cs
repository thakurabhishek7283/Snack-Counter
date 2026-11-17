using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Organisation;

/// <summary>An office with its own currency, time zone and budget. Offices are data: adding London is an insert.</summary>
public sealed class Office
{
    public Office(
        OfficeId id,
        string name,
        Currency currency,
        string timeZoneId,
        Money monthlyBudget,
        Money typicalMinPurchase,
        Money typicalMaxPurchase,
        bool allowVisitorsToVolunteer = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var amount in new[] { monthlyBudget, typicalMinPurchase, typicalMaxPurchase })
        {
            if (amount.Currency != currency)
                throw new ArgumentException($"Office amounts must be in {currency}, got {amount}.");
            if (!amount.IsPositive)
                throw new ArgumentException($"Office amounts must be positive, got {amount}.");
        }
        if (typicalMinPurchase > typicalMaxPurchase)
            throw new ArgumentException("Typical minimum purchase cannot exceed typical maximum.");

        Id = id;
        Name = name;
        Currency = currency;
        TimeZoneId = timeZoneId;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); // fails fast on an unknown zone
        MonthlyBudget = monthlyBudget;
        TypicalMinPurchase = typicalMinPurchase;
        TypicalMaxPurchase = typicalMaxPurchase;
        AllowVisitorsToVolunteer = allowVisitorsToVolunteer;
    }

    private TimeZoneInfo? _timeZone;

    private Office() // EF Core
    {
        Name = "";
        TimeZoneId = "";
    }

    public OfficeId Id { get; }
    public string Name { get; }
    public Currency Currency { get; }
    public string TimeZoneId { get; }
    public TimeZoneInfo TimeZone => _timeZone ??= TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>Default limit used when a month's budget period is first opened.</summary>
    public Money MonthlyBudget { get; }
    public Money TypicalMinPurchase { get; }
    public Money TypicalMaxPurchase { get; }
    public bool AllowVisitorsToVolunteer { get; }

    public DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    public BudgetMonth BudgetMonthOf(DateTimeOffset instant) => BudgetMonth.From(instant, TimeZone);
}

/// <summary>A physical snack box. Modelled separately so an office can have several.</summary>
public sealed record Counter(CounterId Id, OfficeId OfficeId, string Name);
