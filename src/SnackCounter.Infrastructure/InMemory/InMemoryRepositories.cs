using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Infrastructure.InMemory;

public sealed class InMemoryOrganisationRepository(InMemoryDatabase db) : IOrganisationRepository
{
    public Task<Office?> GetOffice(OfficeId id, CancellationToken ct) => Read(() => db.Offices.FirstOrDefault(o => o.Id == id));
    public Task<IReadOnlyList<Office>> ListOffices(CancellationToken ct) => Read(() => (IReadOnlyList<Office>)db.Offices.OrderBy(o => o.Name).ToList());
    public Task<Counter?> GetCounter(CounterId id, CancellationToken ct) => Read(() => db.Counters.FirstOrDefault(c => c.Id == id));
    public Task<IReadOnlyList<Counter>> ListCounters(OfficeId office, CancellationToken ct) => Read(() => (IReadOnlyList<Counter>)db.Counters.Where(c => c.OfficeId == office).ToList());
    public Task<Employee?> GetEmployee(EmployeeId id, CancellationToken ct) => Read(() => db.Employees.FirstOrDefault(e => e.Id == id));
    public Task<Employee?> GetEmployeeByChatId(string chatUserId, CancellationToken ct) => Read(() => db.Employees.FirstOrDefault(e => e.ChatUserId == chatUserId));
    public Task<IReadOnlyList<Employee>> ListVolunteers(CancellationToken ct) => Read(() => (IReadOnlyList<Employee>)db.Employees.Where(e => e.IsVolunteer).ToList());
    public Task<IReadOnlyList<Employee>> ListEmployees(CancellationToken ct) => Read(() => (IReadOnlyList<Employee>)db.Employees.OrderBy(e => e.DisplayName).ToList());

    private Task<T> Read<T>(Func<T> query)
    {
        lock (db.Sync) return Task.FromResult(query());
    }
}

public sealed class InMemoryAvailabilityRepository(InMemoryDatabase db) : IAvailabilityRepository
{
    public Task<IReadOnlyList<Availability>> ListOverlapping(DateOnly from, DateOnly to, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult<IReadOnlyList<Availability>>(db.Availability.Where(a => a.From <= to && from <= a.To).ToList());
    }

    public Task<IReadOnlyList<Availability>> ListForEmployee(EmployeeId employee, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult<IReadOnlyList<Availability>>(db.Availability.Where(a => a.EmployeeId == employee).ToList());
    }

    public void Add(Availability availability)
    {
        lock (db.Sync) db.Availability.Add(availability);
    }
}

public sealed class InMemoryRefillRepository(InMemoryDatabase db, AggregateTracker tracker) : IRefillRepository
{
    public Task<Refill?> Get(RefillId id, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.Track(db.Refills.FirstOrDefault(r => r.Id == id)));
    }

    public Task<Refill?> GetActiveForCounter(CounterId counter, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.Track(db.Refills.FirstOrDefault(r => r.CounterId == counter && r.IsActive)));
    }

    public Task<IReadOnlyList<Refill>> ListDueForSweep(DateTimeOffset now, CancellationToken ct)
    {
        lock (db.Sync)
        {
            return Task.FromResult(tracker.TrackAll(db.Refills.Where(r =>
                (r.Status is RefillStatus.Offered or RefillStatus.Committed && r.DeadlineUtc <= now) ||
                (r.Status == RefillStatus.Committed && !r.ReminderSent))));
        }
    }

    public Task<IReadOnlyList<Refill>> ListByStatus(OfficeId? office, IReadOnlyCollection<RefillStatus> statuses, CancellationToken ct)
    {
        lock (db.Sync)
        {
            return Task.FromResult(tracker.TrackAll(db.Refills.Where(r =>
                statuses.Contains(r.Status) && (office is null || r.OfficeId == office))));
        }
    }

    public Task<IReadOnlyList<Refill>> ListCompletedBetween(OfficeId office, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        lock (db.Sync)
        {
            return Task.FromResult<IReadOnlyList<Refill>>(db.Refills.Where(r =>
                r.OfficeId == office && r.Status == RefillStatus.Completed && r.ClosedAt >= fromUtc && r.ClosedAt < toUtc).ToList());
        }
    }

    public Task<IReadOnlyDictionary<EmployeeId, BuyerStats>> GetBuyerStats(DateTimeOffset recentSince, CancellationToken ct)
    {
        lock (db.Sync)
        {
            IReadOnlyDictionary<EmployeeId, BuyerStats> stats = db.Refills
                .Where(r => r.Status == RefillStatus.Completed && r.AssigneeId != null) // the buyer, once completed
                .GroupBy(r => r.AssigneeId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => new BuyerStats(
                        g.Count(r => r.ClosedAt >= recentSince),
                        g.Count(),
                        g.Max(r => r.ClosedAt)));
            return Task.FromResult(stats);
        }
    }

    public Task<IReadOnlyList<Refill>> ListRecent(OfficeId? office, int take, CancellationToken ct)
    {
        lock (db.Sync)
        {
            return Task.FromResult(tracker.TrackAll(db.Refills
                .Where(r => office == null || r.OfficeId == office)
                .OrderByDescending(r => r.ReportedAt)
                .Take(take)));
        }
    }

    public void Add(Refill refill)
    {
        lock (db.Sync)
        {
            // Mirrors: CREATE UNIQUE INDEX ... ON refills (counter_id) WHERE status IN (active statuses)
            if (refill.IsActive && db.Refills.Any(r => r.CounterId == refill.CounterId && r.IsActive))
                throw new DuplicateActiveRefillException(refill.CounterId);
            db.Refills.Add(refill);
            tracker.Track(refill);
        }
    }
}

public sealed class InMemoryBudgetRepository(InMemoryDatabase db, AggregateTracker tracker) : IBudgetRepository
{
    public Task<BudgetPeriod?> Get(OfficeId office, BudgetMonth month, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.Track(db.Budgets.FirstOrDefault(b => b.OfficeId == office && b.Month == month)));
    }

    public Task<BudgetPeriod?> FindWithOpenReservation(RefillId refill, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.Track(db.Budgets.FirstOrDefault(b => b.OpenReservationFor(refill) is not null)));
    }

    public Task<BudgetPeriod> GetOrOpen(OfficeId office, BudgetMonth month, Money defaultLimit, CancellationToken ct)
    {
        lock (db.Sync)
        {
            var period = db.Budgets.FirstOrDefault(b => b.OfficeId == office && b.Month == month);
            if (period is null)
            {
                period = BudgetPeriod.Open(office, month, defaultLimit);
                db.Budgets.Add(period);
            }
            return Task.FromResult(tracker.Track(period)!);
        }
    }
}

public sealed class InMemoryClaimRepository(InMemoryDatabase db, AggregateTracker tracker) : IClaimRepository
{
    public Task<Claim?> Get(ClaimId id, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.Track(db.Claims.FirstOrDefault(c => c.Id == id)));
    }

    public Task<Claim?> GetByRefill(RefillId refill, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.Track(db.Claims.FirstOrDefault(c => c.RefillId == refill)));
    }

    public Task<IReadOnlyList<Claim>> ListAwaitingHr(CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.TrackAll(db.Claims.Where(c => c.IsAwaitingHr)));
    }

    public Task<IReadOnlyList<Claim>> ListForEmployee(EmployeeId employee, CancellationToken ct)
    {
        lock (db.Sync) return Task.FromResult(tracker.TrackAll(db.Claims.Where(c => c.EmployeeId == employee)));
    }

    public Task<IReadOnlyDictionary<EmployeeId, int>> GetUnpaidCounts(CancellationToken ct)
    {
        lock (db.Sync)
        {
            IReadOnlyDictionary<EmployeeId, int> counts = db.Claims.Where(c => c.IsUnpaid)
                .GroupBy(c => c.EmployeeId)
                .ToDictionary(g => g.Key, g => g.Count());
            return Task.FromResult(counts);
        }
    }

    public void Add(Claim claim)
    {
        lock (db.Sync)
        {
            db.Claims.Add(claim);
            tracker.Track(claim);
        }
    }
}

public sealed class InMemoryOutbox(InMemoryDatabase db, TimeProvider time) : IOutbox, IOutboxStore
{
    public void Enqueue(object payload)
    {
        lock (db.Sync) db.Outbox.Add(new OutboxMessage(payload, time.GetUtcNow()));
    }

    public Task<IReadOnlyList<OutboxMessage>> ListDue(DateTimeOffset now, int max, CancellationToken ct)
    {
        lock (db.Sync)
        {
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(db.Outbox
                .Where(m => m.ProcessedAt is null && m.NextAttemptAt <= now)
                .OrderBy(m => m.OccurredAt)
                .Take(max)
                .ToList());
        }
    }
}
