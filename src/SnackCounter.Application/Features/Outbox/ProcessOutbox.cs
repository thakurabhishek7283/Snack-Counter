using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;

namespace SnackCounter.Application.Features.Outbox;

/// <summary>Run every few seconds by a background job. Delivers chat messages and HR submissions.</summary>
public sealed record ProcessOutbox(int MaxMessages = 50) : ICommand<int>;

public sealed class ProcessOutboxHandler(OutboxProcessor processor) : IRequestHandler<ProcessOutbox, int>
{
    public async Task<Result<int>> Handle(ProcessOutbox command, CancellationToken ct) =>
        await processor.ProcessDue(command.MaxMessages, ct);
}

/// <summary>Outbox handler: hands a chat message to the chat tool.</summary>
public sealed class SendChatMessageHandler(IChatGateway chat) : IOutboxMessageHandler<SendChatMessage>
{
    public Task Handle(SendChatMessage payload, CancellationToken ct) => chat.Send(payload.Audience, payload.Message, ct);
}
