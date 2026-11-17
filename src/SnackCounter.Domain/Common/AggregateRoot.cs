namespace SnackCounter.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}

public abstract class AggregateRoot<TId> : IHasDomainEvents where TId : struct
{
    private readonly List<IDomainEvent> _events = [];

    public TId Id { get; protected set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    protected void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);
}
