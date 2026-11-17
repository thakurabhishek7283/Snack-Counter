using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Abstractions;

// Ports. Implemented in Infrastructure: in memory for tests and the demo, EF Core + PostgreSQL in production.

public interface IOrganisationRepository
{
    Task<Office?> GetOffice(OfficeId id, CancellationToken ct);
    Task<IReadOnlyList<Office>> ListOffices(CancellationToken ct);
    Task<Counter?> GetCounter(CounterId id, CancellationToken ct);
    Task<IReadOnlyList<Counter>> ListCounters(OfficeId office, CancellationToken ct);
    Task<Employee?> GetEmployee(EmployeeId id, CancellationToken ct);
    Task<Employee?> GetEmployeeByChatId(string chatUserId, CancellationToken ct);
    Task<IReadOnlyList<Employee>> ListVolunteers(CancellationToken ct);
    Task<IReadOnlyList<Employee>> ListEmployees(CancellationToken ct);
}

public interface IAvailabilityRepository
{
    Task<IReadOnlyList<Availability>> ListOverlapping(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<Availability>> ListForEmployee(EmployeeId employee, CancellationToken ct);
    void Add(Availability availability);
}

public sealed record BuyerStats(int RecentCount, int LifetimeCount, DateTimeOffset? LastRefillAt);

public interface IRefillRepository
{
    Task<Refill?> Get(RefillId id, CancellationToken ct);
    Task<Refill?> GetActiveForCounter(CounterId counter, CancellationToken ct);

    /// <summary>Offered or Committed refills whose deadline has passed or whose reminder may be due.</summary>
    Task<IReadOnlyList<Refill>> ListDueForSweep(DateTimeOffset now, CancellationToken ct);

    Task<IReadOnlyList<Refill>> ListByStatus(OfficeId? office, IReadOnlyCollection<RefillStatus> statuses, CancellationToken ct);
    Task<IReadOnlyList<Refill>> ListCompletedBetween(OfficeId office, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<IReadOnlyList<Refill>> ListRecent(OfficeId? office, int take, CancellationToken ct);
    Task<IReadOnlyDictionary<EmployeeId, BuyerStats>> GetBuyerStats(DateTimeOffset recentSince, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateActiveRefillException"/> if the counter already has an active refill.</summary>
    void Add(Refill refill);
}

public interface IBudgetRepository
{
    Task<BudgetPeriod?> Get(OfficeId office, BudgetMonth month, CancellationToken ct);
    Task<BudgetPeriod?> FindWithOpenReservation(RefillId refill, CancellationToken ct);

    /// <summary>
    /// Gets the month's period, creating it with <paramref name="defaultLimit"/> if missing. Idempotent and
    /// race-safe: concurrent first requests of a month all get the same single period.
    /// </summary>
    Task<BudgetPeriod> GetOrOpen(OfficeId office, BudgetMonth month, Money defaultLimit, CancellationToken ct);
}

public interface IClaimRepository
{
    Task<Claim?> Get(ClaimId id, CancellationToken ct);
    Task<Claim?> GetByRefill(RefillId refill, CancellationToken ct);
    Task<IReadOnlyList<Claim>> ListAwaitingHr(CancellationToken ct);
    Task<IReadOnlyList<Claim>> ListForEmployee(EmployeeId employee, CancellationToken ct);
    Task<IReadOnlyDictionary<EmployeeId, int>> GetUnpaidCounts(CancellationToken ct);
    void Add(Claim claim);
}

/// <summary>Persists all tracked changes and dispatches domain events, atomically.</summary>
public interface IUnitOfWork
{
    Task SaveChanges(CancellationToken ct);
}

/// <summary>Thrown when the database rejects a second active refill for the same counter (unique index).</summary>
public sealed class DuplicateActiveRefillException(CounterId counter)
    : Exception($"Counter {counter} already has an active refill.")
{
    public CounterId CounterId { get; } = counter;
}

/// <summary>Thrown when an optimistic concurrency check fails (someone else saved the same aggregate first).</summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null) : Exception(message, inner);
