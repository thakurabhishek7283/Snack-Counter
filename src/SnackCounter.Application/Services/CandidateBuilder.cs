using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Assignment;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Application.Services;

/// <summary>
/// Builds the candidate snapshot the assignment engine ranks. All the I/O lives here, so the engine stays pure.
/// Every volunteer from every office is considered, so visitors are found automatically. The rules filter.
/// </summary>
public sealed class CandidateBuilder(
    IOrganisationRepository organisation,
    IAvailabilityRepository availability,
    IRefillRepository refills,
    IClaimRepository claims,
    IOptions<AssignmentOptions> options)
{
    public async Task<IReadOnlyList<Candidate>> Build(Office counterOffice, DateTimeOffset now, CancellationToken ct)
    {
        var today = counterOffice.LocalDate(now);
        var nextWorkingDay = WorkCalendar.NextWorkingDay(today);

        var volunteers = await organisation.ListVolunteers(ct);
        var declared = await availability.ListOverlapping(today, nextWorkingDay, ct);
        var stats = await refills.GetBuyerStats(now - options.Value.RecentWindow, ct);
        var unpaid = await claims.GetUnpaidCounts(ct);

        return volunteers
            .Select(employee =>
            {
                var mine = declared.Where(a => a.EmployeeId == employee.Id).ToList();
                var s = stats.GetValueOrDefault(employee.Id) ?? new BuyerStats(0, 0, null);
                return new Candidate(
                    employee.Id,
                    employee.HomeOfficeId,
                    employee.IsVolunteer,
                    PresenceResolver.Resolve(employee, mine, today),
                    PresenceResolver.Resolve(employee, mine, nextWorkingDay),
                    s.RecentCount,
                    s.LastRefillAt,
                    s.LifetimeCount,
                    unpaid.GetValueOrDefault(employee.Id));
            })
            .ToList();
    }
}
