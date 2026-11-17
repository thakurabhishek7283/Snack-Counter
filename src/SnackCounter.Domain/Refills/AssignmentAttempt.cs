using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Refills;

public enum AttemptKind { DirectedOffer, Broadcast }

public enum AttemptOutcome { Pending, Accepted, Declined, OfferExpired, PurchaseDeadlineMissed, Superseded }

/// <summary>One person's turn at a refill. The history makes fairness and "never re-offer the same person" provable.</summary>
public sealed class AssignmentAttempt
{
    internal AssignmentAttempt(EmployeeId employeeId, AttemptKind kind, DateTimeOffset offeredAt, string? explanation)
    {
        EmployeeId = employeeId;
        Kind = kind;
        OfferedAt = offeredAt;
        Explanation = explanation;
        Outcome = AttemptOutcome.Pending;
    }

    public Guid Id { get; } = Guid.CreateVersion7();
    public EmployeeId EmployeeId { get; }
    public AttemptKind Kind { get; }
    public DateTimeOffset OfferedAt { get; }

    /// <summary>Why this person was chosen, shown to them in the offer.</summary>
    public string? Explanation { get; }

    public AttemptOutcome Outcome { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsPending => Outcome == AttemptOutcome.Pending;

    internal void Resolve(AttemptOutcome outcome, DateTimeOffset at)
    {
        Outcome = outcome;
        ResolvedAt = at;
    }
}

public sealed record PurchaseItem(string Name, int Quantity = 1, string? Category = null);

/// <summary>What was actually bought. The amount is always in the counter's office currency.</summary>
public sealed record Purchase(
    EmployeeId BuyerId,
    Money Amount,
    string ReceiptKey,
    DateTimeOffset PurchasedAt,
    IReadOnlyList<PurchaseItem> Items,
    bool IsLate);
