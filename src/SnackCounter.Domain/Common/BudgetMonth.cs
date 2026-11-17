using System.Globalization;

namespace SnackCounter.Domain.Common;

/// <summary>
/// A calendar month as seen in an office's local time zone. 23:30 on 31 October in Oslo belongs to
/// October, even when it is already 1 November in Bengaluru.
/// </summary>
public readonly record struct BudgetMonth
{
    public BudgetMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        Year = year;
        Month = month;
    }

    public int Year { get; }
    public int Month { get; }

    public static BudgetMonth From(DateTimeOffset instant, TimeZoneInfo officeTimeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, officeTimeZone);
        return new BudgetMonth(local.Year, local.Month);
    }

    public static BudgetMonth Parse(string value) =>
        TryParse(value, out var month) ? month : throw new FormatException($"Expected YYYY-MM, got '{value}'.");

    public static bool TryParse(string? value, out BudgetMonth month)
    {
        month = default;
        if (value is not { Length: 7 } || value[4] != '-') return false;
        if (!int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y < 2000) return false;
        if (!int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 1 or > 12) return false;
        month = new BudgetMonth(y, m);
        return true;
    }

    public BudgetMonth Next() => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);
    public BudgetMonth Previous() => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);

    /// <summary>[start, end) of this month in UTC, using the office's local midnight.</summary>
    public (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtcRange(TimeZoneInfo officeTimeZone) =>
        (LocalMidnightUtc(this, officeTimeZone), LocalMidnightUtc(Next(), officeTimeZone));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");

    private static DateTimeOffset LocalMidnightUtc(BudgetMonth m, TimeZoneInfo tz)
    {
        var local = new DateTime(m.Year, m.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }
}
