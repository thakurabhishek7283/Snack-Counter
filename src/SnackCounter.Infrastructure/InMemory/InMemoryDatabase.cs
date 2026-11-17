using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Infrastructure.InMemory;

/// <summary>
/// Process-wide store for tests and the quick demo. Enforces the same uniqueness rules the PostgreSQL
/// schema does (one active refill per counter, one budget per office per month), so handler code that
/// relies on them behaves the same on both.
/// </summary>
public sealed class InMemoryDatabase
{
    public Lock Sync { get; } = new();
    public List<Office> Offices { get; } = [];
    public List<Counter> Counters { get; } = [];
    public List<Employee> Employees { get; } = [];
    public List<Availability> Availability { get; } = [];
    public List<Refill> Refills { get; } = [];
    public List<BudgetPeriod> Budgets { get; } = [];
    public List<Claim> Claims { get; } = [];
    public List<OutboxMessage> Outbox { get; } = [];
}

/// <summary>Remembers which aggregates a unit of work touched, so their domain events can be dispatched on save.</summary>
public sealed class AggregateTracker
{
    private readonly HashSet<IHasDomainEvents> _tracked = new(ReferenceEqualityComparer.Instance);

    public T? Track<T>(T? aggregate) where T : class, IHasDomainEvents
    {
        if (aggregate is not null) _tracked.Add(aggregate);
        return aggregate;
    }

    public IReadOnlyList<T> TrackAll<T>(IEnumerable<T> aggregates) where T : class, IHasDomainEvents
    {
        var list = aggregates.ToList();
        foreach (var aggregate in list) _tracked.Add(aggregate);
        return list;
    }

    public IReadOnlyList<IDomainEvent> DequeueAll() => _tracked.SelectMany(a => a.DequeueDomainEvents()).ToList();
}

public sealed class InMemoryUnitOfWork(AggregateTracker tracker, DomainEventDispatcher events) : IUnitOfWork
{
    private const int MaxRounds = 10;

    /// <summary>Dispatches domain events until none are left (handlers may create aggregates that raise more).</summary>
    public async Task SaveChanges(CancellationToken ct)
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var pending = tracker.DequeueAll();
            if (pending.Count == 0) return;
            foreach (var domainEvent in pending) await events.Dispatch(domainEvent, ct);
        }
        throw new InvalidOperationException($"Domain events still cascading after {MaxRounds} rounds.");
    }
}
