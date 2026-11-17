using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Refills;

/// <summary>"I'll buy it." First valid commit wins. Reserves budget so the buyer knows how much to spend.</summary>
public sealed record CommitToRefill(RefillId RefillId, EmployeeId EmployeeId) : ICommand<CommitResult>;

public sealed record CommitResult(RefillId RefillId, Money SpendUpTo, DateTimeOffset PurchaseDeadline);

public sealed class CommitToRefillHandler(
    IRefillRepository refills,
    IOrganisationRepository organisation,
    IBudgetRepository budgetRepository,
    BudgetService budgets,
    IUnitOfWork unitOfWork,
    IOptions<AssignmentOptions> options,
    TimeProvider time) : IRequestHandler<CommitToRefill, CommitResult>
{
    public async Task<Result<CommitResult>> Handle(CommitToRefill command, CancellationToken ct)
    {
        var refill = await refills.Get(command.RefillId, ct);
        if (refill is null) return RefillErrors.NotFound(command.RefillId);
        var employee = await organisation.GetEmployee(command.EmployeeId, ct);
        if (employee is null) return AppErrors.EmployeeNotFound(command.EmployeeId);
        if (!employee.IsVolunteer) return AppErrors.NotAVolunteer;
        var office = await organisation.GetOffice(refill.OfficeId, ct);
        if (office is null) return AppErrors.OfficeNotFound(refill.OfficeId);

        // Check first, so we never reserve budget for a commit that is going to be refused.
        var allowed = refill.CanCommit(employee.Id);
        if (allowed.IsFailure) return allowed.Error!;

        if (refill.Status == RefillStatus.Committed) // same person pressed Accept again
        {
            var held = (await budgetRepository.FindWithOpenReservation(refill.Id, ct))?.OpenReservationFor(refill.Id);
            return new CommitResult(refill.Id, held ?? Money.Zero(office.Currency), refill.DeadlineUtc!.Value);
        }

        var now = time.GetUtcNow();
        var budget = await budgets.GetOrOpen(office, office.BudgetMonthOf(now), ct);
        var reservation = budget.ReserveForRefill(refill.Id, office.TypicalMinPurchase, office.TypicalMaxPurchase, now);
        if (reservation.IsFailure)
        {
            refill.PutOnHold(now);
            await unitOfWork.SaveChanges(ct);
            return reservation.Error!;
        }

        refill.Commit(employee.Id, now, options.Value.PurchaseWindow);

        try
        {
            await unitOfWork.SaveChanges(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else committed in the same instant. Their row version won.
            return RefillErrors.ConcurrentUpdate;
        }

        return new CommitResult(refill.Id, reservation.Value, refill.DeadlineUtc!.Value);
    }
}
