using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Refills;

/// <summary>
/// Run every minute by a background job. Expired offers and missed purchase deadlines are handed to the
/// next person. Committed buyers get one reminder halfway through their window.
/// </summary>
public sealed record SweepDeadlines : ICommand<SweepResult>;

public sealed record SweepResult(int Expired, int Reminded);

public sealed class SweepDeadlinesHandler(
    IRefillRepository refills,
    IOrganisationRepository organisation,
    IBudgetRepository budgets,
    RefillAssigner assigner,
    IUnitOfWork unitOfWork,
    IOptions<AssignmentOptions> options,
    TimeProvider time) : IRequestHandler<SweepDeadlines, SweepResult>
{
    public async Task<Result<SweepResult>> Handle(SweepDeadlines command, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        int expired = 0, reminded = 0;

        foreach (var refill in await refills.ListDueForSweep(now, ct))
        {
            if (refill.DeadlineUtc <= now)
            {
                var office = await organisation.GetOffice(refill.OfficeId, ct);
                if (office is null) continue;

                if (refill.Status == RefillStatus.Committed)
                    (await budgets.FindWithOpenReservation(refill.Id, ct))?.Release(refill.Id, now);

                if (refill.ExpireDeadline(now).IsFailure) continue;
                await assigner.AssignNext(refill, office, ct);
                expired++;
            }
            else if (refill.TryRemind(now, options.Value.ReminderAtFraction))
            {
                reminded++;
            }

            // One transaction per refill.
            try
            {
                await unitOfWork.SaveChanges(ct);
            }
            catch (ConcurrencyConflictException)
            {
                // Someone (a user, or another instance's sweep) changed this refill at the same moment.
                // Stop here: the next tick starts with a fresh unit of work and re-reads current state.
                break;
            }
        }

        return new SweepResult(expired, reminded);
    }
}
