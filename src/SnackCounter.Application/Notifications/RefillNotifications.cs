using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using static SnackCounter.Application.Notifications.ChatText;

namespace SnackCounter.Application.Notifications;

/// <summary>Turns refill events into chat messages. Only writes to the outbox; delivery happens after commit.</summary>
public sealed class RefillNotifications(IOutbox outbox, IOrganisationRepository organisation, IBudgetRepository budgets, TimeProvider time) :
    IDomainEventHandler<RefillOffered>,
    IDomainEventHandler<RefillCommitted>,
    IDomainEventHandler<OfferExpired>,
    IDomainEventHandler<PurchaseReminderDue>,
    IDomainEventHandler<PurchaseDeadlineMissed>,
    IDomainEventHandler<RefillEscalated>,
    IDomainEventHandler<RefillPutOnHold>,
    IDomainEventHandler<RefillCompleted>,
    IDomainEventHandler<RefillCancelled>
{
    public async Task Handle(RefillOffered e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        var period = await budgets.Get(office.Id, office.BudgetMonthOf(time.GetUtcNow()), ct);
        var left = period?.Remaining ?? office.MonthlyBudget;

        Direct(e.Candidate,
            $"The {office.Name} snack box is empty and it's your turn ({e.Explanation}). " +
            $"Budget left this month: {left}. A typical refill is up to {office.TypicalMaxPurchase}. " +
            $"Please answer by {LocalTime(e.Deadline, office)}.",
            Accept(e.RefillId), Decline(e.RefillId));
    }

    public async Task Handle(RefillCommitted e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        Channel(e.OfficeId, $"{await Name(e.Assignee, ct)} is refilling the {office.Name} snack box. No need for anyone else to buy.");
    }

    public Task Handle(OfferExpired e, CancellationToken ct)
    {
        Direct(e.EmployeeId, "No answer in time, so the snack refill has been passed on. No action needed.");
        return Task.CompletedTask;
    }

    public async Task Handle(PurchaseReminderDue e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        Direct(e.Assignee, $"Reminder: please refill the {office.Name} snack box and upload the receipt by {LocalTime(e.PurchaseDeadline, office)}.");
    }

    public Task Handle(PurchaseDeadlineMissed e, CancellationToken ct)
    {
        Direct(e.PreviousAssignee,
            "The purchase deadline passed, so the refill has been handed to someone else. " +
            "If you already bought snacks, upload the receipt anyway and you will still be reimbursed.");
        return Task.CompletedTask;
    }

    public async Task Handle(RefillEscalated e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        outbox.Enqueue(new SendChatMessage(new ChatAudience.OfficeChannel(e.OfficeId),
            new ChatMessage($"The {office.Name} snack box is empty and nobody has picked it up yet. First to accept gets it.", [Accept(e.RefillId)])));
        Admins(e.OfficeId, $"The {office.Name} refill was escalated to everyone after {e.DirectedOffers} unanswered offers.");
    }

    public async Task Handle(RefillPutOnHold e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        var month = office.BudgetMonthOf(e.OccurredAt);
        var period = await budgets.Get(office.Id, month, ct);
        Admins(e.OfficeId,
            $"The {office.Name} snack budget for {month} is nearly used up ({period?.Remaining ?? office.MonthlyBudget} left, " +
            $"minimum purchase {office.TypicalMinPurchase}). The refill is on hold until the budget is raised or the next month starts.");
    }

    public async Task Handle(RefillCompleted e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        Channel(e.OfficeId, $"The {office.Name} snack box has been refilled by {await Name(e.Buyer, ct)}. Thanks!");
    }

    public async Task Handle(RefillCancelled e, CancellationToken ct)
    {
        var office = await Office(e.OfficeId, ct);
        Channel(e.OfficeId, $"The {office.Name} snack refill was cancelled: {e.Reason}");
    }

    private void Direct(EmployeeId to, string text, params ChatAction[] actions) =>
        outbox.Enqueue(new SendChatMessage(new ChatAudience.Direct(to), new ChatMessage(text, actions)));

    private void Channel(OfficeId office, string text) =>
        outbox.Enqueue(new SendChatMessage(new ChatAudience.OfficeChannel(office), ChatMessage.Plain(text)));

    private void Admins(OfficeId office, string text) =>
        outbox.Enqueue(new SendChatMessage(new ChatAudience.OfficeAdmins(office), ChatMessage.Plain(text)));

    private async Task<Office> Office(OfficeId id, CancellationToken ct) =>
        await organisation.GetOffice(id, ct) ?? throw new InvalidOperationException($"Office {id} not found.");

    private async Task<string> Name(EmployeeId id, CancellationToken ct) =>
        (await organisation.GetEmployee(id, ct))?.DisplayName ?? "A volunteer";
}
