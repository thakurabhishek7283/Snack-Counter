using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Notifications;

/// <summary>Claim and budget events become outbox messages: HR submissions and chat updates.</summary>
public sealed class ClaimNotifications(IOutbox outbox, IOrganisationRepository organisation) :
    IDomainEventHandler<ClaimReadyForSubmission>,
    IDomainEventHandler<ClaimStatusChanged>,
    IDomainEventHandler<ClaimOverdue>,
    IDomainEventHandler<OverBudgetSpendRecorded>
{
    public Task Handle(ClaimReadyForSubmission e, CancellationToken ct)
    {
        outbox.Enqueue(new SubmitClaimToHr(e.ClaimId));
        return Task.CompletedTask;
    }

    public Task Handle(ClaimStatusChanged e, CancellationToken ct)
    {
        var text = e.To switch
        {
            ClaimStatus.Submitted => $"Your reimbursement claim for {e.Amount} has been sent to HR. Approval usually takes 1 to 10 days.",
            ClaimStatus.Approved => $"HR approved your claim for {e.Amount}.",
            ClaimStatus.Paid => $"Your claim for {e.Amount} has been reimbursed. Thanks for refilling!",
            ClaimStatus.Rejected => $"HR rejected your claim for {e.Amount}: {e.Reason ?? "no reason given"}. Upload a new receipt to resubmit.",
            _ => null,
        };
        if (text is not null)
            outbox.Enqueue(new SendChatMessage(new ChatAudience.Direct(e.EmployeeId), ChatMessage.Plain(text)));
        return Task.CompletedTask;
    }

    public async Task Handle(ClaimOverdue e, CancellationToken ct)
    {
        var name = (await organisation.GetEmployee(e.EmployeeId, ct))?.DisplayName ?? "a volunteer";
        outbox.Enqueue(new SendChatMessage(new ChatAudience.OfficeAdmins(e.OfficeId),
            ChatMessage.Plain($"{name}'s snack claim for {e.Amount} has been waiting on HR for more than 10 days. Please chase it.")));
    }

    public async Task Handle(OverBudgetSpendRecorded e, CancellationToken ct)
    {
        var office = await organisation.GetOffice(e.OfficeId, ct);
        outbox.Enqueue(new SendChatMessage(new ChatAudience.OfficeAdmins(e.OfficeId),
            ChatMessage.Plain($"A {e.Amount} snack purchase put the {office?.Name} budget for {e.Month} over the limit ({e.RemainingAfter} left). It was recorded and will be reimbursed.")));
    }
}
