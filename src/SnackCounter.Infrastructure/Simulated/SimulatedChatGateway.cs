using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;

namespace SnackCounter.Infrastructure.Simulated;

public sealed record SentChatMessage(ChatAudience Audience, ChatMessage Message, DateTimeOffset SentAt);

/// <summary>
/// Stand-in for the company chat tool. Records every message so tests (and the dev panel in the web app)
/// can see what people would have received, and click the buttons.
/// </summary>
public sealed class SimulatedChatGateway(TimeProvider time) : IChatGateway
{
    private readonly Lock _sync = new();
    private readonly List<SentChatMessage> _sent = [];

    public IReadOnlyList<SentChatMessage> Sent
    {
        get { lock (_sync) return _sent.ToList(); }
    }

    public Task Send(ChatAudience audience, ChatMessage message, CancellationToken ct)
    {
        lock (_sync) _sent.Add(new SentChatMessage(audience, message, time.GetUtcNow()));
        return Task.CompletedTask;
    }

    public IReadOnlyList<ChatMessage> DirectMessagesTo(EmployeeId employee) =>
        Sent.Where(m => m.Audience is ChatAudience.Direct d && d.EmployeeId == employee).Select(m => m.Message).ToList();

    public IReadOnlyList<ChatMessage> ChannelMessages(OfficeId office) =>
        Sent.Where(m => m.Audience is ChatAudience.OfficeChannel c && c.OfficeId == office).Select(m => m.Message).ToList();

    public IReadOnlyList<ChatMessage> AdminMessages(OfficeId office) =>
        Sent.Where(m => m.Audience is ChatAudience.OfficeAdmins a && a.OfficeId == office).Select(m => m.Message).ToList();
}
