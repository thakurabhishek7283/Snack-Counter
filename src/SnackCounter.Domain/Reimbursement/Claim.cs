using System.Globalization;
using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Reimbursement;

public enum ClaimStatus { Pending, Submitted, Approved, Rejected, Paid }

/// <summary>
/// A reimbursement claim in the external HR system. Separate from Refill, so the box can be refilled
/// again while HR takes 1 to 10 days to approve the last one.
/// The amount is in the receipt currency. HR converts to the employee's payroll currency.
/// </summary>
public sealed class Claim : AggregateRoot<ClaimId>
{
    private Claim() { }

    public RefillId RefillId { get; private set; }
    public EmployeeId EmployeeId { get; private set; }
    public OfficeId OfficeId { get; private set; }
    public Money Amount { get; private set; }
    public string Description { get; private set; } = "";
    public string ReceiptKey { get; private set; } = "";
    public ClaimStatus Status { get; private set; }
    public string? HrClaimId { get; private set; }
    public int ResubmissionCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset? OverdueFlaggedAt { get; private set; }

    /// <summary>Sent to HR. The same key always produces the same HR claim, so retries never duplicate.</summary>
    public string IdempotencyKey => string.Create(CultureInfo.InvariantCulture, $"{Id.Value:N}-{ResubmissionCount}");

    public bool IsUnpaid => Status != ClaimStatus.Paid;
    public bool IsAwaitingHr => Status is ClaimStatus.Submitted or ClaimStatus.Approved;

    public static Claim Create(RefillId refill, EmployeeId employee, OfficeId office, Money amount, string description, string receiptKey, DateTimeOffset now)
    {
        if (!amount.IsPositive) throw new ArgumentOutOfRangeException(nameof(amount), "Claim amount must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptKey);

        var claim = new Claim
        {
            Id = ClaimId.New(),
            RefillId = refill,
            EmployeeId = employee,
            OfficeId = office,
            Amount = amount,
            Description = description,
            ReceiptKey = receiptKey,
            Status = ClaimStatus.Pending,
            CreatedAt = now,
        };
        claim.Raise(new ClaimReadyForSubmission(claim.Id, now));
        return claim;
    }

    public Result MarkSubmitted(string hrClaimId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hrClaimId);
        if (Status == ClaimStatus.Submitted && HrClaimId == hrClaimId) return Result.Ok();
        if (Status != ClaimStatus.Pending) return ClaimErrors.InvalidTransition(Status, ClaimStatus.Submitted);

        HrClaimId = hrClaimId;
        SubmittedAt = now;
        ChangeStatus(ClaimStatus.Submitted, now);
        return Result.Ok();
    }

    /// <summary>Applies what HR reports. Idempotent, because polling repeats the same answer.</summary>
    public Result ApplyHrStatus(ClaimStatus reported, DateTimeOffset now, string? reason = null)
    {
        if (reported == Status) return Result.Ok();

        var allowed = (Status, reported) switch
        {
            (ClaimStatus.Submitted, ClaimStatus.Approved) => true,
            (ClaimStatus.Submitted, ClaimStatus.Rejected) => true,
            (ClaimStatus.Submitted, ClaimStatus.Paid) => true, // approved and paid between two polls
            (ClaimStatus.Approved, ClaimStatus.Paid) => true,
            _ => false,
        };
        if (!allowed) return ClaimErrors.InvalidTransition(Status, reported);

        if (reported is ClaimStatus.Approved or ClaimStatus.Rejected || DecidedAt is null) DecidedAt = now;
        if (reported == ClaimStatus.Paid) PaidAt = now;
        if (reported == ClaimStatus.Rejected) RejectionReason = reason;
        ChangeStatus(reported, now);
        return Result.Ok();
    }

    /// <summary>After a rejection, such as an unreadable receipt, the buyer uploads a new receipt and we submit again.</summary>
    public Result Resubmit(string newReceiptKey, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newReceiptKey);
        if (Status != ClaimStatus.Rejected) return ClaimErrors.InvalidTransition(Status, ClaimStatus.Pending);

        ResubmissionCount++;
        ReceiptKey = newReceiptKey;
        HrClaimId = null;
        SubmittedAt = null;
        DecidedAt = null;
        RejectionReason = null;
        OverdueFlaggedAt = null;
        ChangeStatus(ClaimStatus.Pending, now);
        Raise(new ClaimReadyForSubmission(Id, now));
        return Result.Ok();
    }

    public bool IsOverdue(DateTimeOffset now, TimeSpan threshold) =>
        Status == ClaimStatus.Submitted && SubmittedAt is { } submitted && now - submitted > threshold;

    /// <summary>Raises <see cref="ClaimOverdue"/> once per submission.</summary>
    public bool FlagIfOverdue(DateTimeOffset now, TimeSpan threshold)
    {
        if (!IsOverdue(now, threshold) || OverdueFlaggedAt is not null) return false;
        OverdueFlaggedAt = now;
        Raise(new ClaimOverdue(Id, EmployeeId, OfficeId, Amount, SubmittedAt!.Value, now));
        return true;
    }

    private void ChangeStatus(ClaimStatus to, DateTimeOffset now)
    {
        var from = Status;
        Status = to;
        Raise(new ClaimStatusChanged(Id, EmployeeId, OfficeId, Amount, from, to, RejectionReason, now));
    }
}

public static class ClaimErrors
{
    public static Error NotFound(ClaimId id) => Error.NotFound("claim.not_found", $"Claim {id} does not exist.");

    public static Error InvalidTransition(ClaimStatus from, ClaimStatus to) =>
        Error.Conflict("claim.invalid_transition", $"A claim that is {from} cannot become {to}.");
}

public sealed record ClaimReadyForSubmission(ClaimId ClaimId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ClaimStatusChanged(ClaimId ClaimId, EmployeeId EmployeeId, OfficeId OfficeId, Money Amount, ClaimStatus From, ClaimStatus To, string? Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ClaimOverdue(ClaimId ClaimId, EmployeeId EmployeeId, OfficeId OfficeId, Money Amount, DateTimeOffset SubmittedAt, DateTimeOffset OccurredAt) : IDomainEvent;
