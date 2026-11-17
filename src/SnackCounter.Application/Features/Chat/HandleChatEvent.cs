using System.Globalization;
using System.Text;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Availability;
using SnackCounter.Application.Features.Budget;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Application.Notifications;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Chat;

/// <summary>
/// Inbound chat webhook: a slash command or a button click. Translated into the same commands the web app
/// uses, so there is one source of truth. Always replies with a message, even when the command fails.
/// </summary>
public sealed record HandleChatEvent(string ChatUserId, string Text) : ICommand<ChatMessage>;

public sealed class HandleChatEventHandler(
    IOrganisationRepository organisation,
    IAvailabilityRepository availability,
    IRefillRepository refills,
    IDispatcher dispatcher,
    TimeProvider time) : IRequestHandler<HandleChatEvent, ChatMessage>
{
    public const string Help =
        "Snack commands: /snacks empty [counter] | /snacks status | /snacks away YYYY-MM-DD YYYY-MM-DD | " +
        "/snacks remote YYYY-MM-DD YYYY-MM-DD | /snacks visiting <office> YYYY-MM-DD YYYY-MM-DD";

    public async Task<Result<ChatMessage>> Handle(HandleChatEvent command, CancellationToken ct)
    {
        var employee = await organisation.GetEmployeeByChatId(command.ChatUserId, ct);
        if (employee is null) return Reply("I don't know you yet. Ask an office admin to add you.");

        var text = command.Text.Trim();
        if (text.StartsWith(ChatText.AcceptPrefix, StringComparison.OrdinalIgnoreCase)) return await Accept(employee, text, ct);
        if (text.StartsWith(ChatText.DeclinePrefix, StringComparison.OrdinalIgnoreCase)) return await Decline(employee, text, ct);

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2 || !words[0].Equals("/snacks", StringComparison.OrdinalIgnoreCase)) return Reply(Help);

        return words[1].ToLowerInvariant() switch
        {
            "empty" => await ReportEmpty(employee, words.Skip(2).FirstOrDefault(), ct),
            "status" => await Status(ct),
            "away" => await Declare(employee, words, 2, AvailabilityKind.Holiday, null, ct),
            "remote" => await Declare(employee, words, 2, AvailabilityKind.Remote, null, ct),
            "visiting" => await Visiting(employee, words, ct),
            _ => Reply(Help),
        };
    }

    private async Task<Result<ChatMessage>> Accept(Employee employee, string text, CancellationToken ct)
    {
        if (!TryRefillId(text, ChatText.AcceptPrefix, out var refillId)) return Reply("That button has expired.");
        var result = await dispatcher.Send(new CommitToRefill(refillId, employee.Id), ct);
        if (result.IsFailure) return Reply(Friendly(result.Error!));

        var refill = await refills.Get(refillId, ct);
        var office = refill is null ? null : await organisation.GetOffice(refill.OfficeId, ct);
        var deadline = office is null ? "" : $" by {ChatText.LocalTime(result.Value.PurchaseDeadline, office)}";
        return Reply($"You're on it, thank you! Spend up to {result.Value.SpendUpTo} and upload the receipt{deadline}.");
    }

    private async Task<Result<ChatMessage>> Decline(Employee employee, string text, CancellationToken ct)
    {
        if (!TryRefillId(text, ChatText.DeclinePrefix, out var refillId)) return Reply("That button has expired.");
        var result = await dispatcher.Send(new DeclineRefill(refillId, employee.Id), ct);
        return Reply(result.IsSuccess ? "No problem, I've asked someone else." : Friendly(result.Error!));
    }

    private async Task<Result<ChatMessage>> ReportEmpty(Employee employee, string? counterName, CancellationToken ct)
    {
        // Report for the office the person is at today (they may be visiting), not just their home office.
        var home = await organisation.GetOffice(employee.HomeOfficeId, ct);
        var now = time.GetUtcNow();
        var today = home?.LocalDate(now) ?? DateOnly.FromDateTime(now.UtcDateTime);
        var declared = await availability.ListForEmployee(employee.Id, ct);
        var presence = PresenceResolver.Resolve(employee, declared, today);
        var officeId = presence.Kind == PresenceKind.AtOffice ? presence.OfficeId!.Value : employee.HomeOfficeId;

        var counters = await organisation.ListCounters(officeId, ct);
        var counter = counterName is null
            ? counters.FirstOrDefault()
            : counters.FirstOrDefault(c => c.Name.Equals(counterName, StringComparison.OrdinalIgnoreCase));
        if (counter is null) return Reply(counterName is null ? "Your office has no snack counter set up." : $"No counter called '{counterName}'.");

        var result = await dispatcher.Send(new Refills.ReportEmpty(counter.Id, employee.Id), ct);
        if (result.IsFailure) return Reply(Friendly(result.Error!));

        var prefix = result.Value.AlreadyReported ? "Thanks, this was already reported." : "Thanks for reporting it!";
        return Reply($"{prefix} {await Describe(result.Value.Status, result.Value.AssigneeId, ct)}");
    }

    private async Task<Result<ChatMessage>> Status(CancellationToken ct)
    {
        var text = new StringBuilder();
        foreach (var office in await organisation.ListOffices(ct))
        {
            var budget = await dispatcher.Send(new GetBudgetStatus(office.Id), ct);
            var remaining = budget.IsSuccess ? budget.Value.Remaining.ToString() : "unknown";
            foreach (var counter in await organisation.ListCounters(office.Id, ct))
            {
                var active = await refills.GetActiveForCounter(counter.Id, ct);
                var state = active is null ? "Stocked." : await Describe(active.Status, active.AssigneeId, ct);
                text.Append(CultureInfo.InvariantCulture, $"{office.Name} / {counter.Name}: {state} Budget left: {remaining}. ");
            }
        }
        return Reply(text.ToString().Trim());
    }

    private async Task<Result<ChatMessage>> Visiting(Employee employee, string[] words, CancellationToken ct)
    {
        if (words.Length < 5) return Reply(Help);
        var office = (await organisation.ListOffices(ct)).FirstOrDefault(o => o.Name.Equals(words[2], StringComparison.OrdinalIgnoreCase));
        if (office is null) return Reply($"No office called '{words[2]}'.");
        return await Declare(employee, words, 3, AvailabilityKind.AtOffice, office.Id, ct);
    }

    private async Task<Result<ChatMessage>> Declare(Employee employee, string[] words, int dateIndex, AvailabilityKind kind, OfficeId? office, CancellationToken ct)
    {
        if (words.Length < dateIndex + 2 ||
            !DateOnly.TryParseExact(words[dateIndex], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(words[dateIndex + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            return Reply(Help);
        }

        var result = await dispatcher.Send(new SetAvailability(employee.Id, from, to, kind, office), ct);
        return Reply(result.IsSuccess ? $"Got it: {kind} from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}." : Friendly(result.Error!));
    }

    private async Task<string> Describe(RefillStatus status, EmployeeId? assignee, CancellationToken ct)
    {
        var name = assignee is { } id ? (await organisation.GetEmployee(id, ct))?.DisplayName ?? "a volunteer" : null;
        return status switch
        {
            RefillStatus.Offered => $"I've asked {name} to refill it.",
            RefillStatus.Committed => $"{name} is on it.",
            RefillStatus.Escalated => "Waiting for any volunteer to pick it up.",
            RefillStatus.OnHold => "On hold: this month's budget is used up. The office admin has been told.",
            RefillStatus.Open => "Looking for a volunteer.",
            _ => $"Refill is {status}.",
        };
    }

    private static string Friendly(Error error) => error.Code switch
    {
        "refill.already_taken" => "Thanks, but someone else already took this refill.",
        "refill.not_offered_to_you" => "This refill is currently offered to someone else.",
        "refill.invalid_transition" => "This refill has moved on, so that button no longer applies.",
        "budget.exhausted" => "This month's snack budget is used up, so the refill is on hold. The office admin has been told.",
        _ => error.Message,
    };

    private static bool TryRefillId(string text, string prefix, out RefillId id)
    {
        var ok = Guid.TryParse(text.AsSpan(prefix.Length), out var guid);
        id = new RefillId(guid);
        return ok;
    }

    private static Result<ChatMessage> Reply(string text) => ChatMessage.Plain(text);
}
