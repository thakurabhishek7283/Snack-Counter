using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SnackCounter.Domain.Common;

namespace SnackCounter.Application.Abstractions;

/// <summary>
/// Transactional outbox: side effects (chat, HR) are written in the same transaction as the state change,
/// then delivered by a background job with retries. Nothing is sent if the transaction rolls back, and
/// nothing is lost if the external system is down.
/// </summary>
public interface IOutbox
{
    void Enqueue(object payload);
}

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> ListDue(DateTimeOffset now, int max, CancellationToken ct);
}

public sealed class OutboxMessage
{
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    public OutboxMessage(object payload, DateTimeOffset occurredAt)
    {
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    private OutboxMessage() => Payload = null!; // EF Core

    public Guid Id { get; } = Guid.CreateVersion7();
    public object Payload { get; }
    public DateTimeOffset OccurredAt { get; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Exponential backoff: 5s, 10s, 20s ... capped at 1 hour.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error;
        var delay = TimeSpan.FromSeconds(5 * Math.Pow(2, Math.Min(Attempts - 1, 20)));
        NextAttemptAt = now + (delay > MaxBackoff ? MaxBackoff : delay);
    }
}

// Payloads. Small, serialisable records.
public sealed record SendChatMessage(ChatAudience Audience, ChatMessage Message);
public sealed record SubmitClaimToHr(ClaimId ClaimId);

public interface IOutboxMessageHandler<in TPayload>
{
    Task Handle(TPayload payload, CancellationToken ct);
}

public sealed class OutboxProcessor(
    IOutboxStore store,
    IUnitOfWork unitOfWork,
    IServiceProvider services,
    TimeProvider time,
    ILogger<OutboxProcessor> logger)
{
    private static readonly ConcurrentDictionary<Type, PayloadInvoker> Invokers = new();

    /// <summary>Delivers due messages. Returns how many were delivered successfully.</summary>
    public async Task<int> ProcessDue(int max, CancellationToken ct)
    {
        var delivered = 0;
        foreach (var message in await store.ListDue(time.GetUtcNow(), max, ct))
        {
            try
            {
                var invoker = Invokers.GetOrAdd(
                    message.Payload.GetType(),
                    type => (PayloadInvoker)Activator.CreateInstance(typeof(PayloadInvoker<>).MakeGenericType(type))!);
                await invoker.Invoke(message.Payload, services, ct);
                message.MarkProcessed(time.GetUtcNow());
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed, attempt {Attempt}", message.Id, message.Payload.GetType().Name, message.Attempts + 1);
                message.MarkFailed(ex.Message, time.GetUtcNow());
            }

            await unitOfWork.SaveChanges(ct);
        }
        return delivered;
    }

    private abstract class PayloadInvoker
    {
        public abstract Task Invoke(object payload, IServiceProvider services, CancellationToken ct);
    }

    private sealed class PayloadInvoker<TPayload> : PayloadInvoker
    {
        public override Task Invoke(object payload, IServiceProvider services, CancellationToken ct) =>
            services.GetRequiredService<IOutboxMessageHandler<TPayload>>().Handle((TPayload)payload, ct);
    }
}
