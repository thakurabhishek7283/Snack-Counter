using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SnackCounter.Domain.Common;

namespace SnackCounter.Application.Abstractions;

/// <summary>
/// Reacts to something that happened inside an aggregate. Runs during SaveChanges, in the same transaction.
/// Convention: use-case handlers orchestrate aggregates; event handlers only produce side effects (outbox).
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}

public sealed class DomainEventDispatcher(IServiceProvider services)
{
    private static readonly ConcurrentDictionary<Type, EventInvoker> Invokers = new();

    public Task Dispatch(IDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var invoker = Invokers.GetOrAdd(
            domainEvent.GetType(),
            type => (EventInvoker)Activator.CreateInstance(typeof(EventInvoker<>).MakeGenericType(type))!);
        return invoker.Invoke(domainEvent, services, ct);
    }

    private abstract class EventInvoker
    {
        public abstract Task Invoke(IDomainEvent domainEvent, IServiceProvider services, CancellationToken ct);
    }

    private sealed class EventInvoker<TEvent> : EventInvoker where TEvent : IDomainEvent
    {
        public override async Task Invoke(IDomainEvent domainEvent, IServiceProvider services, CancellationToken ct)
        {
            foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
                await handler.Handle((TEvent)domainEvent, ct);
        }
    }
}
