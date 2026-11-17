using Microsoft.EntityFrameworkCore;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Infrastructure.Persistence;

public sealed class EfOrganisationRepository(SnackDbContext db) : IOrganisationRepository
{
    public Task<Office?> GetOffice(OfficeId id, CancellationToken ct) => db.Offices.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Office>> ListOffices(CancellationToken ct) =>
        await db.Offices.OrderBy(o => o.Name).ToListAsync(ct);

    public Task<Counter?> GetCounter(CounterId id, CancellationToken ct) => db.Counters.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Counter>> ListCounters(OfficeId office, CancellationToken ct) =>
        await db.Counters.Where(c => c.OfficeId == office).OrderBy(c => c.Name).ToListAsync(ct);

    public Task<Employee?> GetEmployee(EmployeeId id, CancellationToken ct) => db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Employee?> GetEmployeeByChatId(string chatUserId, CancellationToken ct) =>
        db.Employees.FirstOrDefaultAsync(e => e.ChatUserId == chatUserId, ct);

    public async Task<IReadOnlyList<Employee>> ListVolunteers(CancellationToken ct) =>
        await db.Employees.Where(e => e.IsVolunteer).ToListAsync(ct);

    public async Task<IReadOnlyList<Employee>> ListEmployees(CancellationToken ct) =>
        await db.Employees.OrderBy(e => e.DisplayName).ToListAsync(ct);
}

public sealed class EfAvailabilityRepository(SnackDbContext db) : IAvailabilityRepository
{
    public async Task<IReadOnlyList<Availability>> ListOverlapping(DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.Availability.Where(a => a.From <= to && from <= a.To).ToListAsync(ct);

    public async Task<IReadOnlyList<Availability>> ListForEmployee(EmployeeId employee, CancellationToken ct) =>
        await db.Availability.Where(a => a.EmployeeId == employee).OrderBy(a => a.From).ToListAsync(ct);

    public void Add(Availability availability) => db.Availability.Add(availability);
}

public sealed class EfRefillRepository(SnackDbContext db) : IRefillRepository
{
    private static readonly RefillStatus[] Active = [.. RefillTransitions.ActiveStatuses];

    public Task<Refill?> Get(RefillId id, CancellationToken ct) => db.Refills.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Refill?> GetActiveForCounter(CounterId counter, CancellationToken ct) =>
        db.Refills.FirstOrDefaultAsync(r => r.CounterId == counter && Active.Contains(r.Status), ct);

    public async Task<IReadOnlyList<Refill>> ListDueForSweep(DateTimeOffset now, CancellationToken ct) =>
        await db.Refills
            .Where(r => (r.Status == RefillStatus.Offered || r.Status == RefillStatus.Committed) &&
                        (r.DeadlineUtc <= now || (r.Status == RefillStatus.Committed && !r.ReminderSent)))
            .OrderBy(r => r.DeadlineUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Refill>> ListByStatus(OfficeId? office, IReadOnlyCollection<RefillStatus> statuses, CancellationToken ct)
    {
        var wanted = statuses.ToArray();
        return await db.Refills
            .Where(r => wanted.Contains(r.Status) && (office == null || r.OfficeId == office))
            .OrderBy(r => r.ReportedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Refill>> ListCompletedBetween(OfficeId office, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        await db.Refills
            .Where(r => r.OfficeId == office && r.Status == RefillStatus.Completed && r.ClosedAt >= fromUtc && r.ClosedAt < toUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<EmployeeId, BuyerStats>> GetBuyerStats(DateTimeOffset recentSince, CancellationToken ct)
    {
        var rows = await db.Refills
            .Where(r => r.Status == RefillStatus.Completed && r.AssigneeId != null) // the buyer, once completed
            .GroupBy(r => r.AssigneeId)
            .Select(g => new
            {
                Buyer = g.Key,
                Recent = g.Count(r => r.ClosedAt >= recentSince),
                Lifetime = g.Count(),
                Last = g.Max(r => r.ClosedAt),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Buyer!.Value, r => new BuyerStats(r.Recent, r.Lifetime, r.Last));
    }

    public async Task<IReadOnlyList<Refill>> ListRecent(OfficeId? office, int take, CancellationToken ct) =>
        await db.Refills
            .Where(r => office == null || r.OfficeId == office)
            .OrderByDescending(r => r.ReportedAt)
            .Take(take)
            .ToListAsync(ct);

    public void Add(Refill refill) => db.Refills.Add(refill);
}

public sealed class EfBudgetRepository(SnackDbContext db) : IBudgetRepository
{
    public async Task<BudgetPeriod?> Get(OfficeId office, BudgetMonth month, CancellationToken ct) =>
        // Check unsaved additions first, so opening a period twice in one unit of work can't create a duplicate.
        db.Budgets.Local.FirstOrDefault(b => b.OfficeId == office && b.Month == month)
        ?? await db.Budgets.FirstOrDefaultAsync(b => b.OfficeId == office && b.Month == month, ct);

    public async Task<BudgetPeriod?> FindWithOpenReservation(RefillId refill, CancellationToken ct)
    {
        var local = db.Budgets.Local.FirstOrDefault(b => b.OpenReservationFor(refill) is not null);
        if (local is not null) return local;

        // Narrow in SQL to periods that ever reserved for this refill, then apply the exact rule in the domain.
        var candidates = await db.Budgets
            .Where(b => b.Entries.Any(e => e.RefillId == refill && e.Type == LedgerEntryType.Reserve))
            .ToListAsync(ct);
        return candidates.FirstOrDefault(b => b.OpenReservationFor(refill) is not null);
    }

    public async Task<BudgetPeriod> GetOrOpen(OfficeId office, BudgetMonth month, Money defaultLimit, CancellationToken ct)
    {
        var existing = await Get(office, month, ct);
        if (existing is not null) return existing;

        // Idempotent insert, committed on its own: an empty period is harmless, and this way two requests
        // racing to open the month both succeed instead of one failing on the unique index.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO budget_periods (id, office_id, month, limit_amount, limit_currency)
            VALUES ({Guid.CreateVersion7()}, {office.Value}, {month.ToString()}, {defaultLimit.Amount}, {defaultLimit.Currency.Code})
            ON CONFLICT (office_id, month) DO NOTHING
            """, ct);

        return await db.Budgets.FirstAsync(b => b.OfficeId == office && b.Month == month, ct);
    }
}

public sealed class EfClaimRepository(SnackDbContext db) : IClaimRepository
{
    public Task<Claim?> Get(ClaimId id, CancellationToken ct) => db.Claims.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Claim?> GetByRefill(RefillId refill, CancellationToken ct) => db.Claims.FirstOrDefaultAsync(c => c.RefillId == refill, ct);

    public async Task<IReadOnlyList<Claim>> ListAwaitingHr(CancellationToken ct) =>
        await db.Claims
            .Where(c => c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.Approved)
            .OrderBy(c => c.SubmittedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Claim>> ListForEmployee(EmployeeId employee, CancellationToken ct) =>
        await db.Claims.Where(c => c.EmployeeId == employee).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<EmployeeId, int>> GetUnpaidCounts(CancellationToken ct)
    {
        var rows = await db.Claims
            .Where(c => c.Status != ClaimStatus.Paid)
            .GroupBy(c => c.EmployeeId)
            .Select(g => new { Employee = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Employee, r => r.Count);
    }

    public void Add(Claim claim) => db.Claims.Add(claim);
}

/// <summary>
/// Outbox rows are added to the same DbContext as the business change, so they commit or roll back together.
/// Multiple API instances may pick up the same message; that is safe because HR submission is idempotent
/// (idempotency key) and a duplicate chat message is a tolerable worst case. See the design trade-offs.
/// </summary>
public sealed class EfOutbox(SnackDbContext db, TimeProvider time) : IOutbox, IOutboxStore
{
    public void Enqueue(object payload) => db.Outbox.Add(new OutboxMessage(payload, time.GetUtcNow()));

    public async Task<IReadOnlyList<OutboxMessage>> ListDue(DateTimeOffset now, int max, CancellationToken ct) =>
        await db.Outbox
            .Where(m => m.ProcessedAt == null && m.NextAttemptAt <= now)
            .OrderBy(m => m.OccurredAt)
            .Take(max)
            .ToListAsync(ct);
}
