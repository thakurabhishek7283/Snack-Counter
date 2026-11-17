using System.Collections;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Infrastructure.Persistence;

/// <summary>
/// One SaveChanges = one database transaction, containing the state change, everything domain event
/// handlers did in response, and the outbox rows. Translates database-level guards into the
/// application's exceptions so handlers don't depend on EF or Npgsql.
/// </summary>
public sealed class EfUnitOfWork(SnackDbContext db, DomainEventDispatcher events) : IUnitOfWork
{
    private const int MaxEventRounds = 10;

    public async Task SaveChanges(CancellationToken ct)
    {
        await DispatchDomainEvents(ct);
        TouchAggregatesWithChangedChildren();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The data was changed by someone else since it was read.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_refills_one_active_per_counter",
        })
        {
            var counter = ex.Entries.Select(e => e.Entity).OfType<Refill>().FirstOrDefault()?.CounterId ?? default;
            throw new DuplicateActiveRefillException(counter);
        }
    }

    private async Task DispatchDomainEvents(CancellationToken ct)
    {
        for (var round = 0; round < MaxEventRounds; round++)
        {
            var pending = db.ChangeTracker.Entries<IHasDomainEvents>()
                .SelectMany(entry => entry.Entity.DequeueDomainEvents())
                .ToList();
            if (pending.Count == 0) return;

            foreach (var domainEvent in pending) await events.Dispatch(domainEvent, ct);
        }
        throw new InvalidOperationException($"Domain events still cascading after {MaxEventRounds} rounds.");
    }

    /// <summary>
    /// The row version lives on the aggregate root's row. If only a child changed (a new ledger entry or
    /// assignment attempt), the root row would not be updated and two concurrent writers could both win.
    /// Marking the root Modified makes EF issue UPDATE ... WHERE xmin = @original, so the aggregate is
    /// the unit of concurrency, as DDD intends.
    /// </summary>
    private void TouchAggregatesWithChangedChildren()
    {
        db.ChangeTracker.DetectChanges();
        var roots = db.ChangeTracker.Entries()
            .Where(e => e.Entity is IHasDomainEvents && e.State == EntityState.Unchanged)
            .ToList();

        foreach (var root in roots)
        {
            var childChanged = root.Collections.Any(collection =>
                collection.CurrentValue is IEnumerable children &&
                children.Cast<object>().Any(child => db.Entry(child).State is EntityState.Added or EntityState.Modified or EntityState.Deleted));

            if (childChanged) root.State = EntityState.Modified;
        }
    }
}
