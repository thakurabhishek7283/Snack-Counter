using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Reporting;

namespace SnackCounter.Application.Features.Reports;

/// <summary>Management's one monthly report: each office in its own currency, plus a consolidated total.</summary>
public sealed record GetMonthlyReport(BudgetMonth Month, string? ReportingCurrency = null) : IQuery<MonthlySpendReport>;

public sealed class GetMonthlyReportHandler(
    IOrganisationRepository organisation,
    IBudgetRepository budgets,
    IRefillRepository refills,
    IFxRateProvider fx,
    IOptions<ReportingOptions> options) : IRequestHandler<GetMonthlyReport, MonthlySpendReport>
{
    public async Task<Result<MonthlySpendReport>> Handle(GetMonthlyReport query, CancellationToken ct)
    {
        var code = query.ReportingCurrency ?? options.Value.Currency;
        if (!Currency.TryParse(code, out var reporting)) return AppErrors.InvalidCurrency(code);

        var figures = new List<OfficeMonthFigures>();
        var rates = new Dictionary<Currency, decimal>();

        foreach (var office in await organisation.ListOffices(ct))
        {
            var period = await budgets.Get(office.Id, query.Month, ct);
            var (from, to) = query.Month.ToUtcRange(office.TimeZone);
            var completed = await refills.ListCompletedBetween(office.Id, from, to, ct);

            var timesToRefill = completed
                .Where(r => r.ClosedAt is not null)
                .Select(r => r.ClosedAt!.Value - r.ReportedAt)
                .Order()
                .ToList();

            figures.Add(new OfficeMonthFigures(
                office.Id,
                office.Name,
                period?.Limit ?? office.MonthlyBudget,
                period?.Spent ?? Money.Zero(office.Currency),
                period?.SpendCount ?? 0,
                period?.OverBudgetCount ?? 0,
                timesToRefill.Count == 0 ? null : timesToRefill[timesToRefill.Count / 2]));

            if (office.Currency != reporting && !rates.ContainsKey(office.Currency) &&
                await fx.GetMonthlyRate(office.Currency, reporting, query.Month, ct) is { } rate)
            {
                rates[office.Currency] = rate;
            }
        }

        return MonthlyReportCalculator.Build(query.Month, reporting, figures, rates);
    }
}
