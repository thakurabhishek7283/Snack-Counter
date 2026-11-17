using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Organisation;

/// <summary>An employee as synced from the company directory / HR.</summary>
public sealed record Employee(
    EmployeeId Id,
    string DisplayName,
    string HrEmployeeId,
    string ChatUserId,
    OfficeId HomeOfficeId,
    Currency PayrollCurrency,
    IReadOnlySet<DayOfWeek> DefaultOfficeDays,
    bool IsVolunteer = true,
    bool IsOfficeAdmin = false)
{
    public bool IsAdminOf(OfficeId office) => IsOfficeAdmin && HomeOfficeId == office;
}

public enum AvailabilityKind { Holiday, Remote, AtOffice }

/// <summary>A self-declared exception to the default office-days pattern: holiday, remote, or visiting an office.</summary>
public sealed record Availability
{
    private Availability(Guid id, EmployeeId employeeId, DateOnly from, DateOnly to, AvailabilityKind kind, OfficeId? officeId)
    {
        Id = id;
        EmployeeId = employeeId;
        From = from;
        To = to;
        Kind = kind;
        OfficeId = officeId;
    }

    public Guid Id { get; }
    public EmployeeId EmployeeId { get; }
    public DateOnly From { get; }
    public DateOnly To { get; }
    public AvailabilityKind Kind { get; }

    /// <summary>The office the employee is at. Only set when <see cref="Kind"/> is AtOffice.</summary>
    public OfficeId? OfficeId { get; }

    public static Result<Availability> Create(EmployeeId employee, DateOnly from, DateOnly to, AvailabilityKind kind, OfficeId? officeId = null)
    {
        if (to < from)
            return Error.Validation("availability.invalid_range", "End date must be on or after the start date.");
        if (kind == AvailabilityKind.AtOffice && officeId is null)
            return Error.Validation("availability.office_required", "Say which office you will be at.");
        if (kind != AvailabilityKind.AtOffice && officeId is not null)
            return Error.Validation("availability.office_not_allowed", "Only office presence can name an office.");

        return new Availability(Guid.CreateVersion7(), employee, from, to, kind, officeId);
    }

    public bool Covers(DateOnly date) => date >= From && date <= To;
    public bool Overlaps(Availability other) => From <= other.To && other.From <= To;
}
