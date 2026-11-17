using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Organisation;

public enum PresenceKind { AtOffice, Remote, Holiday, NonWorkingDay }

/// <summary>Where an employee is expected to be on a given day.</summary>
public readonly record struct Presence(PresenceKind Kind, OfficeId? OfficeId)
{
    public static Presence At(OfficeId office) => new(PresenceKind.AtOffice, office);
    public static Presence WorkingRemotely => new(PresenceKind.Remote, null);
    public static Presence OnHoliday => new(PresenceKind.Holiday, null);
    public static Presence NotWorking => new(PresenceKind.NonWorkingDay, null);

    public bool IsAt(OfficeId office) => Kind == PresenceKind.AtOffice && OfficeId == office;

    public string Describe() => Kind switch
    {
        PresenceKind.AtOffice => "in the office",
        PresenceKind.Remote => "working remotely",
        PresenceKind.Holiday => "on holiday",
        _ => "not working",
    };
}

/// <summary>Working-day rules. Extension point: swap in an office holiday calendar.</summary>
public static class WorkCalendar
{
    public static bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static DateOnly NextWorkingDay(DateOnly date)
    {
        var next = date.AddDays(1);
        while (!IsWorkingDay(next)) next = next.AddDays(1);
        return next;
    }
}

public static class PresenceResolver
{
    /// <summary>
    /// Precedence: non-working day, then an explicit availability entry, then the default office-days pattern.
    /// A visit to another office counts as being at that office on every working day it covers.
    /// </summary>
    public static Presence Resolve(Employee employee, IEnumerable<Availability> availability, DateOnly date)
    {
        if (!WorkCalendar.IsWorkingDay(date)) return Presence.NotWorking;

        var entry = availability
            .Where(a => a.EmployeeId == employee.Id && a.Covers(date))
            .OrderByDescending(a => a.From)
            .FirstOrDefault();

        if (entry is not null)
        {
            return entry.Kind switch
            {
                AvailabilityKind.Holiday => Presence.OnHoliday,
                AvailabilityKind.Remote => Presence.WorkingRemotely,
                AvailabilityKind.AtOffice => Presence.At(entry.OfficeId!.Value),
                _ => throw new InvalidOperationException($"Unknown availability kind {entry.Kind}."),
            };
        }

        return employee.DefaultOfficeDays.Contains(date.DayOfWeek)
            ? Presence.At(employee.HomeOfficeId)
            : Presence.WorkingRemotely;
    }
}
