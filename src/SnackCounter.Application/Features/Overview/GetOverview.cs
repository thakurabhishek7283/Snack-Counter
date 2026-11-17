using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Budget;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Overview;

/// <summary>Dashboard: every office, its budget, and the state of each snack box.</summary>
public sealed record GetOverview : IQuery<IReadOnlyList<OfficeOverview>>;

public sealed record OfficeOverview(
    OfficeId Id, string Name, Currency Currency, string TimeZoneId,
    Money TypicalMinPurchase, Money TypicalMaxPurchase, BudgetStatus Budget, IReadOnlyList<CounterOverview> Counters);

public sealed record CounterOverview(CounterId Id, string Name, RefillSummary? ActiveRefill);

public sealed record RefillSummary(
    RefillId Id, OfficeId OfficeId, CounterId CounterId, RefillStatus Status,
    EmployeeId? AssigneeId, string? AssigneeName, DateTimeOffset? DeadlineUtc,
    DateTimeOffset ReportedAt, DateTimeOffset? ClosedAt, Money? Amount);

public sealed class GetOverviewHandler(
    IOrganisationRepository organisation,
    IRefillRepository refills,
    IDispatcher dispatcher) : IRequestHandler<GetOverview, IReadOnlyList<OfficeOverview>>
{
    public async Task<Result<IReadOnlyList<OfficeOverview>>> Handle(GetOverview query, CancellationToken ct)
    {
        var names = (await organisation.ListEmployees(ct)).ToDictionary(e => e.Id, e => e.DisplayName);
        var result = new List<OfficeOverview>();

        foreach (var office in await organisation.ListOffices(ct))
        {
            var budget = await dispatcher.Send(new GetBudgetStatus(office.Id), ct);
            if (budget.IsFailure) return budget.Error!;

            var counters = new List<CounterOverview>();
            foreach (var counter in await organisation.ListCounters(office.Id, ct))
            {
                var active = await refills.GetActiveForCounter(counter.Id, ct);
                counters.Add(new CounterOverview(counter.Id, counter.Name, active is null ? null : RefillSummaries.From(active, names)));
            }

            result.Add(new OfficeOverview(office.Id, office.Name, office.Currency, office.TimeZoneId,
                office.TypicalMinPurchase, office.TypicalMaxPurchase, budget.Value, counters));
        }

        return result;
    }
}

internal static class RefillSummaries
{
    public static RefillSummary From(Refill r, IReadOnlyDictionary<EmployeeId, string> names) =>
        new(r.Id, r.OfficeId, r.CounterId, r.Status, r.AssigneeId,
            r.AssigneeId is { } a ? names.GetValueOrDefault(a) : null,
            r.DeadlineUtc, r.ReportedAt, r.ClosedAt, r.Purchase?.Amount);
}
