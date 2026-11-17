using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Refills;

/// <summary>
/// One "the box is empty" episode for one counter, from report to refilled.
/// Invariant: at most one owner (assignee) at a time, and every move goes through <see cref="RefillTransitions"/>.
/// Money is deliberately not tracked here. Reimbursement is the separate Claim aggregate.
/// </summary>
public sealed class Refill : AggregateRoot<RefillId>
{
    private readonly List<AssignmentAttempt> _attempts = [];

    private Refill() { }

    public CounterId CounterId { get; private set; }
    public OfficeId OfficeId { get; private set; }

    /// <summary>Snapshot of the office currency at report time. Purchases must be in this currency.</summary>
    public Currency OfficeCurrency { get; private set; }

    public RefillStatus Status { get; private set; }
    public EmployeeId ReportedBy { get; private set; }
    public DateTimeOffset ReportedAt { get; private set; }

    /// <summary>The person currently offered or committed. Null while Open, Escalated or OnHold.</summary>
    public EmployeeId? AssigneeId { get; private set; }

    /// <summary>Offer deadline while Offered, purchase deadline while Committed.</summary>
    public DateTimeOffset? DeadlineUtc { get; private set; }

    public DateTimeOffset? CommittedAt { get; private set; }
    public bool ReminderSent { get; private set; }
    public Purchase? Purchase { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<AssignmentAttempt> Attempts => _attempts;

    public bool IsActive => RefillTransitions.IsActive(Status);
    public int DirectedOfferCount => _attempts.Count(a => a.Kind == AttemptKind.DirectedOffer);
    public IReadOnlySet<EmployeeId> AttemptedEmployees => _attempts.Select(a => a.EmployeeId).ToHashSet();

    public static Refill Report(CounterId counter, OfficeId office, Currency officeCurrency, EmployeeId reportedBy, DateTimeOffset now)
    {
        var refill = new Refill
        {
            Id = RefillId.New(),
            CounterId = counter,
            OfficeId = office,
            OfficeCurrency = officeCurrency,
            Status = RefillStatus.Open,
            ReportedBy = reportedBy,
            ReportedAt = now,
        };
        refill.Raise(new RefillReported(refill.Id, counter, office, reportedBy, now));
        return refill;
    }

    public Result Offer(EmployeeId candidate, DateTimeOffset now, TimeSpan ttl, string? explanation = null)
    {
        var allowed = Allowed(RefillTrigger.Offer);
        if (allowed.IsFailure) return allowed;
        if (_attempts.Any(a => a.EmployeeId == candidate)) return RefillErrors.AlreadyAttempted(candidate);

        _attempts.Add(new AssignmentAttempt(candidate, AttemptKind.DirectedOffer, now, explanation));
        AssigneeId = candidate;
        DeadlineUtc = now + ttl;
        Move(RefillTrigger.Offer);
        Raise(new RefillOffered(Id, OfficeId, candidate, DeadlineUtc.Value, explanation, now));
        return Result.Ok();
    }

    public Result Decline(EmployeeId by, DateTimeOffset now)
    {
        var allowed = Allowed(RefillTrigger.Decline);
        if (allowed.IsFailure) return allowed;
        if (by != AssigneeId) return RefillErrors.NotOfferedTo(by);

        CurrentAttempt()!.Resolve(AttemptOutcome.Declined, now);
        ClearAssignment();
        Move(RefillTrigger.Decline);
        Raise(new OfferDeclined(Id, OfficeId, by, now));
        return Result.Ok();
    }

    /// <summary>Side-effect free check, so a handler can verify before reserving budget.</summary>
    public Result CanCommit(EmployeeId by)
    {
        if (Status == RefillStatus.Committed)
            return by == AssigneeId ? Result.Ok() : RefillErrors.AlreadyTaken(AssigneeId!.Value);

        var allowed = Allowed(RefillTrigger.Commit);
        if (allowed.IsFailure) return allowed;

        // Offered: only the offered person. Escalated (broadcast): first committer wins.
        return Status == RefillStatus.Offered && by != AssigneeId ? RefillErrors.NotOfferedTo(by) : Result.Ok();
    }

    public Result Commit(EmployeeId by, DateTimeOffset now, TimeSpan purchaseWindow)
    {
        var allowed = CanCommit(by);
        if (allowed.IsFailure) return allowed;
        if (Status == RefillStatus.Committed) return Result.Ok(); // idempotent: same person pressed Accept twice

        var attempt = Status == RefillStatus.Escalated
            ? AddAttempt(new AssignmentAttempt(by, AttemptKind.Broadcast, now, "Accepted the open call"))
            : CurrentAttempt()!;
        attempt.Resolve(AttemptOutcome.Accepted, now);

        AssigneeId = by;
        CommittedAt = now;
        DeadlineUtc = now + purchaseWindow;
        ReminderSent = false;
        Move(RefillTrigger.Commit);
        Raise(new RefillCommitted(Id, OfficeId, by, DeadlineUtc.Value, now));
        return Result.Ok();
    }

    /// <summary>Sends one reminder once <paramref name="atFraction"/> of the purchase window has passed.</summary>
    public bool TryRemind(DateTimeOffset now, double atFraction)
    {
        if (Status != RefillStatus.Committed || ReminderSent || CommittedAt is null || DeadlineUtc is null) return false;
        if (now >= DeadlineUtc) return false;

        var remindAt = CommittedAt.Value + (DeadlineUtc.Value - CommittedAt.Value) * atFraction;
        if (now < remindAt) return false;

        ReminderSent = true;
        Raise(new PurchaseReminderDue(Id, OfficeId, AssigneeId!.Value, DeadlineUtc.Value, now));
        return true;
    }

    /// <summary>Offer TTL or purchase deadline passed: free the refill so the next person can be asked.</summary>
    public Result ExpireDeadline(DateTimeOffset now)
    {
        var allowed = Allowed(RefillTrigger.Expire);
        if (allowed.IsFailure) return allowed;
        if (DeadlineUtc is null || now < DeadlineUtc) return RefillErrors.DeadlineNotDue;

        var previous = AssigneeId!.Value;
        var wasCommitted = Status == RefillStatus.Committed;

        CurrentAttempt()!.Resolve(wasCommitted ? AttemptOutcome.PurchaseDeadlineMissed : AttemptOutcome.OfferExpired, now);
        ClearAssignment();
        Move(RefillTrigger.Expire);
        Raise(wasCommitted
            ? new PurchaseDeadlineMissed(Id, OfficeId, previous, now)
            : new OfferExpired(Id, OfficeId, previous, now));
        return Result.Ok();
    }

    public Result Escalate(DateTimeOffset now)
    {
        var allowed = Allowed(RefillTrigger.Escalate);
        if (allowed.IsFailure) return allowed;

        Move(RefillTrigger.Escalate);
        Raise(new RefillEscalated(Id, OfficeId, DirectedOfferCount, now));
        return Result.Ok();
    }

    public Result PutOnHold(DateTimeOffset now)
    {
        var allowed = Allowed(RefillTrigger.Hold);
        if (allowed.IsFailure) return allowed;

        SupersedePendingAttempt(now);
        ClearAssignment();
        Move(RefillTrigger.Hold);
        Raise(new RefillPutOnHold(Id, OfficeId, now));
        return Result.Ok();
    }

    public Result Resume(DateTimeOffset now)
    {
        var allowed = Allowed(RefillTrigger.Resume);
        if (allowed.IsFailure) return allowed;

        Move(RefillTrigger.Resume);
        Raise(new RefillResumed(Id, OfficeId, now));
        return Result.Ok();
    }

    /// <summary>Side-effect free check for <see cref="Complete"/>.</summary>
    public Result CanComplete(EmployeeId by, Money amount)
    {
        var allowed = Allowed(RefillTrigger.Complete);
        if (allowed.IsFailure) return allowed;

        var isCurrentAssignee = Status == RefillStatus.Committed && by == AssigneeId;
        if (!isCurrentAssignee && !IsLateCompleter(by)) return RefillErrors.NotAssignee(by);
        if (amount.Currency != OfficeCurrency) return RefillErrors.WrongCurrency(OfficeCurrency, amount.Currency);
        if (!amount.IsPositive) return RefillErrors.AmountMustBePositive;
        return Result.Ok();
    }

    public Result Complete(EmployeeId by, Money amount, string receiptKey, IReadOnlyList<PurchaseItem>? items, DateTimeOffset now)
    {
        var allowed = CanComplete(by, amount);
        if (allowed.IsFailure) return allowed;
        if (string.IsNullOrWhiteSpace(receiptKey)) return RefillErrors.ReceiptRequired;

        var isLate = !(Status == RefillStatus.Committed && by == AssigneeId);
        if (isLate) SupersedePendingAttempt(now);

        Purchase = new Purchase(by, amount, receiptKey, now, items ?? [], isLate);
        AssigneeId = by;
        DeadlineUtc = null;
        ClosedAt = now;
        Move(RefillTrigger.Complete);
        Raise(new RefillCompleted(Id, OfficeId, CounterId, by, amount, receiptKey, isLate, now));
        return Result.Ok();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        var allowed = Allowed(RefillTrigger.Cancel);
        if (allowed.IsFailure) return allowed;
        if (string.IsNullOrWhiteSpace(reason)) return RefillErrors.ReasonRequired;

        SupersedePendingAttempt(now);
        ClearAssignment();
        CancellationReason = reason;
        ClosedAt = now;
        Move(RefillTrigger.Cancel);
        Raise(new RefillCancelled(Id, OfficeId, reason, now));
        return Result.Ok();
    }

    /// <summary>A previous assignee who missed the purchase deadline but bought anyway. We never strand their money.</summary>
    private bool IsLateCompleter(EmployeeId by) =>
        Status != RefillStatus.Committed &&
        _attempts.Any(a => a.EmployeeId == by && a.Outcome == AttemptOutcome.PurchaseDeadlineMissed);

    private Result Allowed(RefillTrigger trigger) =>
        RefillTransitions.CanFire(Status, trigger) ? Result.Ok() : RefillErrors.InvalidTransition(Status, trigger);

    private void Move(RefillTrigger trigger) => Status = RefillTransitions.Next(Status, trigger);

    private AssignmentAttempt? CurrentAttempt() =>
        AssigneeId is { } assignee ? _attempts.LastOrDefault(a => a.EmployeeId == assignee) : null;

    private AssignmentAttempt AddAttempt(AssignmentAttempt attempt)
    {
        _attempts.Add(attempt);
        return attempt;
    }

    private void SupersedePendingAttempt(DateTimeOffset now)
    {
        foreach (var attempt in _attempts.Where(a => a.IsPending)) attempt.Resolve(AttemptOutcome.Superseded, now);
    }

    private void ClearAssignment()
    {
        AssigneeId = null;
        DeadlineUtc = null;
        CommittedAt = null;
        ReminderSent = false;
    }
}
