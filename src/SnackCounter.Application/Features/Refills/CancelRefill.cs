using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Refills;

/// <summary>Office admin cancels a refill (false alarm, office closed). Any reservation is released.</summary>
public sealed record CancelRefill(RefillId RefillId, EmployeeId AdminId, string Reason) : ICommand<Unit>;

public sealed class CancelRefillHandler(
    IRefillRepository refills,
    IOrganisationRepository organisation,
    IBudgetRepository budgets,
    IUnitOfWork unitOfWork,
    TimeProvider time) : IRequestHandler<CancelRefill, Unit>
{
    public async Task<Result<Unit>> Handle(CancelRefill command, CancellationToken ct)
    {
        var refill = await refills.Get(command.RefillId, ct);
        if (refill is null) return RefillErrors.NotFound(command.RefillId);
        var admin = await organisation.GetEmployee(command.AdminId, ct);
        if (admin is null || !admin.IsAdminOf(refill.OfficeId)) return AppErrors.NotOfficeAdmin;

        var now = time.GetUtcNow();
        var cancelled = refill.Cancel(command.Reason, now);
        if (cancelled.IsFailure) return cancelled.Error!;

        (await budgets.FindWithOpenReservation(refill.Id, ct))?.Release(refill.Id, now);
        await unitOfWork.SaveChanges(ct);
        return Unit.Value;
    }
}

/// <summary>Run at the start of each month: refills on hold for lack of budget get a fresh start.</summary>
public sealed record ResumeOnHoldRefills : ICommand<int>;

public sealed class ResumeOnHoldRefillsHandler(
    IOrganisationRepository organisation,
    RefillAssigner assigner,
    IUnitOfWork unitOfWork) : IRequestHandler<ResumeOnHoldRefills, int>
{
    public async Task<Result<int>> Handle(ResumeOnHoldRefills command, CancellationToken ct)
    {
        var resumed = 0;
        foreach (var office in await organisation.ListOffices(ct))
            resumed += await assigner.ResumeOnHold(office, ct);

        await unitOfWork.SaveChanges(ct);
        return resumed;
    }
}
