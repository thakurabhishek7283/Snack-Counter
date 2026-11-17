using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Assignment;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Services;

public enum AssignmentOutcome { NotApplicable, Offered, Escalated, OnHold }

/// <summary>
/// Moves an Open refill forward: on hold if the budget is gone, otherwise offer to the best candidate,
/// otherwise escalate to a broadcast. Used by report, decline, sweep and resume.
/// </summary>
public sealed class RefillAssigner(
    AssignmentEngine engine,
    CandidateBuilder candidates,
    BudgetService budgets,
    IRefillRepository refills,
    IOptions<AssignmentOptions> options,
    TimeProvider time)
{
    private AssignmentOptions Options => options.Value;

    public async Task<AssignmentOutcome> AssignNext(Refill refill, Office office, CancellationToken ct)
    {
        if (refill.Status != RefillStatus.Open) return AssignmentOutcome.NotApplicable;
        var now = time.GetUtcNow();

        var budget = await budgets.GetOrOpen(office, office.BudgetMonthOf(now), ct);
        if (budget.Remaining < office.TypicalMinPurchase)
        {
            refill.PutOnHold(now);
            return AssignmentOutcome.OnHold;
        }

        if (refill.DirectedOfferCount >= Options.MaxDirectedOffers)
        {
            refill.Escalate(now);
            return AssignmentOutcome.Escalated;
        }

        var context = new AssignmentContext(
            office.Id, office.AllowVisitorsToVolunteer, refill.AttemptedEmployees,
            Options.OutstandingClaimsCap, Options.Weights, now);
        var decision = engine.Rank(await candidates.Build(office, now, ct), context);

        if (decision.Top is not { } top)
        {
            refill.Escalate(now);
            return AssignmentOutcome.Escalated;
        }

        refill.Offer(top.Candidate.Id, now, Options.OfferTtl, top.Explanation);
        return AssignmentOutcome.Offered;
    }

    /// <summary>Called when the budget is raised or a new month starts. Returns how many refills resumed.</summary>
    public async Task<int> ResumeOnHold(Office office, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var budget = await budgets.GetOrOpen(office, office.BudgetMonthOf(now), ct);
        if (budget.Remaining < office.TypicalMinPurchase) return 0;

        var resumed = 0;
        foreach (var refill in await refills.ListByStatus(office.Id, [RefillStatus.OnHold], ct))
        {
            if (refill.Resume(now).IsFailure) continue;
            await AssignNext(refill, office, ct);
            resumed++;
        }
        return resumed;
    }
}
