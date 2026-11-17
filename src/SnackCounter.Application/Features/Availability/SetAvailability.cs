using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Application.Features.Availability;

/// <summary>A volunteer declares a holiday, remote days, or a visit to another office.</summary>
public sealed record SetAvailability(
    EmployeeId EmployeeId,
    DateOnly From,
    DateOnly To,
    AvailabilityKind Kind,
    OfficeId? VisitingOfficeId = null) : ICommand<Guid>;

public sealed class SetAvailabilityHandler(
    IOrganisationRepository organisation,
    IAvailabilityRepository availability,
    IUnitOfWork unitOfWork) : IRequestHandler<SetAvailability, Guid>
{
    public async Task<Result<Guid>> Handle(SetAvailability command, CancellationToken ct)
    {
        if (await organisation.GetEmployee(command.EmployeeId, ct) is null) return AppErrors.EmployeeNotFound(command.EmployeeId);
        if (command.VisitingOfficeId is { } office && await organisation.GetOffice(office, ct) is null) return AppErrors.OfficeNotFound(office);

        var created = Domain.Organisation.Availability.Create(command.EmployeeId, command.From, command.To, command.Kind, command.VisitingOfficeId);
        if (created.IsFailure) return created.Error!;

        var existing = await availability.ListForEmployee(command.EmployeeId, ct);
        if (existing.Any(e => e.Overlaps(created.Value))) return AppErrors.AvailabilityOverlaps;

        availability.Add(created.Value);
        await unitOfWork.SaveChanges(ct);
        return created.Value.Id;
    }
}
