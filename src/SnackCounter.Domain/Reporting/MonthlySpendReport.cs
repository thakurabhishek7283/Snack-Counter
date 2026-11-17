using System.Globalization;
using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Reporting;

/// <summary>Raw figures for one office and one month, all in that office's currency.</summary>
public sealed record OfficeMonthFigures(
    OfficeId OfficeId,
    string OfficeName,
    Money Limit,
    Money Spent,
    int RefillCount,
    int OverBudgetCount,
    TimeSpan? MedianTimeToRefill);

public sealed record OfficeReportLine(
    OfficeId OfficeId,
    string OfficeName,
    Money Limit,
    Money Spent,
    Money Remaining,
    decimal PercentUsed,
    int RefillCount,
    Money? AveragePurchase,
    int OverBudgetCount,
    TimeSpan? MedianTimeToRefill,
    decimal FxRateToReporting,
    Money SpentInReportingCurrency);

public sealed record MonthlySpendReport(
    BudgetMonth Month,
    Currency ReportingCurrency,
    IReadOnlyList<OfficeReportLine> Offices,
    Money Total,
    IReadOnlyDictionary<string, decimal> RatesUsed);

public static class MonthlyReportCalculator
{
    /// <summary>
    /// Sums each office in its own currency first, then converts each total once with the month's saved rate.
    /// Converting once (not per purchase) keeps rounding drift to a single step per office.
    /// </summary>
    public static Result<MonthlySpendReport> Build(
        BudgetMonth month,
        Currency reportingCurrency,
        IReadOnlyList<OfficeMonthFigures> offices,
        IReadOnlyDictionary<Currency, decimal> ratesToReporting)
    {
        var lines = new List<OfficeReportLine>();
        var ratesUsed = new Dictionary<string, decimal>();

        foreach (var office in offices)
        {
            var currency = office.Spent.Currency;
            decimal rate;
            if (currency == reportingCurrency)
            {
                rate = 1m;
            }
            else if (!ratesToReporting.TryGetValue(currency, out rate))
            {
                return Error.Rule("report.missing_fx_rate", $"No {month} rate from {currency} to {reportingCurrency}.");
            }
            else
            {
                ratesUsed[string.Create(CultureInfo.InvariantCulture, $"{currency}->{reportingCurrency}")] = rate;
            }

            var percent = office.Limit.Amount == 0 ? 0 : decimal.Round(office.Spent.Amount / office.Limit.Amount * 100, 1, MidpointRounding.AwayFromZero);
            var average = office.RefillCount == 0
                ? (Money?)null
                : new Money(decimal.Round(office.Spent.Amount / office.RefillCount, 2, MidpointRounding.AwayFromZero), currency);

            lines.Add(new OfficeReportLine(
                office.OfficeId, office.OfficeName, office.Limit, office.Spent, office.Limit - office.Spent, percent,
                office.RefillCount, average, office.OverBudgetCount, office.MedianTimeToRefill,
                rate, office.Spent.ConvertTo(reportingCurrency, rate)));
        }

        var total = Money.Sum(lines.Select(l => l.SpentInReportingCurrency), reportingCurrency);
        return new MonthlySpendReport(month, reportingCurrency, lines, total, ratesUsed);
    }
}
