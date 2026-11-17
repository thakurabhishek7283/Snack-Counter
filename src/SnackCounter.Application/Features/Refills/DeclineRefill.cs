using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Refills;

/// <summary>"Can't this time." The refill moves straight on to the next candidate.</summary>
public sealed record DeclineRefill(RefillId RefillId, EmployeeId EmployeeId) : ICommand<Unit>;

public sealed class DeclineRefillHandler(
    IRefillRepository refills,
    IOrganisationRepository organisation,
    RefillAssigner assigner,
    IUnitOfWork unitOfWork,
    TimeProvider time) : IRequestHandler<DeclineRefill, Unit>
{
    public async Task<Result<Unit>> Handle(DeclineRefill command, CancellationToken ct)
    {
        var refill = await refills.Get(command.RefillId, ct);
        if (refill is null) return RefillErrors.NotFound(command.RefillId);
        var office = await organisation.GetOffice(refill.OfficeId, ct);
        if (office is null) return AppErrors.OfficeNotFound(refill.OfficeId);

        var declined = refill.Decline(command.EmployeeId, time.GetUtcNow());
        if (declined.IsFailure) return declined.Error!;

        await assigner.AssignNext(refill, office, ct);
        await unitOfWork.SaveChanges(ct);
        return Unit.Value;
    }
}
