using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Application.Features.People;

public sealed record PersonSummary(
    EmployeeId Id, string DisplayName, OfficeId HomeOfficeId, string HomeOfficeName, Currency PayrollCurrency,
    bool IsVolunteer, bool IsOfficeAdmin, string ChatUserId, IReadOnlyList<DayOfWeek> DefaultOfficeDays);

public sealed record AvailabilityEntry(Guid Id, DateOnly From, DateOnly To, AvailabilityKind Kind, OfficeId? OfficeId);

/// <summary>Everyone, for the people list and the dev "act as" picker.</summary>
public sealed record ListPeople : IQuery<IReadOnlyList<PersonSummary>>;

public sealed record GetPerson(EmployeeId EmployeeId) : IQuery<PersonSummary>;

public sealed record GetMyAvailability(EmployeeId EmployeeId) : IQuery<IReadOnlyList<AvailabilityEntry>>;

public sealed record GetMyClaims(EmployeeId EmployeeId) : IQuery<IReadOnlyList<ClaimSummary>>;

public sealed class PeopleQueryHandlers(
    IOrganisationRepository organisation,
    IAvailabilityRepository availability,
    IClaimRepository claims,
    IOptions<ReimbursementOptions> options,
    TimeProvider time) :
    IRequestHandler<ListPeople, IReadOnlyList<PersonSummary>>,
    IRequestHandler<GetPerson, PersonSummary>,
    IRequestHandler<GetMyAvailability, IReadOnlyList<AvailabilityEntry>>,
    IRequestHandler<GetMyClaims, IReadOnlyList<ClaimSummary>>
{
    public async Task<Result<IReadOnlyList<PersonSummary>>> Handle(ListPeople query, CancellationToken ct)
    {
        var offices = (await organisation.ListOffices(ct)).ToDictionary(o => o.Id, o => o.Name);
        return (await organisation.ListEmployees(ct)).Select(e => ToSummary(e, offices)).ToList();
    }

    public async Task<Result<PersonSummary>> Handle(GetPerson query, CancellationToken ct)
    {
        var employee = await organisation.GetEmployee(query.EmployeeId, ct);
        if (employee is null) return AppErrors.EmployeeNotFound(query.EmployeeId);
        var offices = (await organisation.ListOffices(ct)).ToDictionary(o => o.Id, o => o.Name);
        return ToSummary(employee, offices);
    }

    public async Task<Result<IReadOnlyList<AvailabilityEntry>>> Handle(GetMyAvailability query, CancellationToken ct) =>
        (await availability.ListForEmployee(query.EmployeeId, ct))
            .OrderBy(a => a.From)
            .Select(a => new AvailabilityEntry(a.Id, a.From, a.To, a.Kind, a.OfficeId))
            .ToList();

    public async Task<Result<IReadOnlyList<ClaimSummary>>> Handle(GetMyClaims query, CancellationToken ct)
    {
        var employee = await organisation.GetEmployee(query.EmployeeId, ct);
        if (employee is null) return AppErrors.EmployeeNotFound(query.EmployeeId);
        var now = time.GetUtcNow();
        return (await claims.ListForEmployee(query.EmployeeId, ct))
            .Select(c => ClaimSummary.From(c, employee.DisplayName, now, options.Value.OverdueAfter))
            .ToList();
    }

    private static PersonSummary ToSummary(Employee e, IReadOnlyDictionary<OfficeId, string> offices) =>
        new(e.Id, e.DisplayName, e.HomeOfficeId, offices.GetValueOrDefault(e.HomeOfficeId) ?? "", e.PayrollCurrency,
            e.IsVolunteer, e.IsOfficeAdmin, e.ChatUserId, e.DefaultOfficeDays.Order().ToList());
}
