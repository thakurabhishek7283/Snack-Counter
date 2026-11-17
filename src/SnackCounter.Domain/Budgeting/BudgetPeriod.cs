using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Budgeting;

public enum LedgerEntryType { Reserve, Release, Spend, Adjust }

public sealed record LedgerEntry(
    LedgerEntryType Type,
    RefillId? RefillId,
    Money Amount,
    DateTimeOffset At,
    bool OverBudget = false,
    string? Note = null)
{
    private LedgerEntry() : this(default, null, default, default) { } // EF Core: complex types can't be constructor-bound
}

public sealed record SpendOutcome(Money Amount, bool OverBudget, bool AlreadyRecorded);

/// <summary>
/// One office's budget for one month, always in that office's currency. Append-only ledger, so every
/// krone and rupee can be traced.
///   Remaining = Limit - Spent - open Reservations
/// </summary>
public sealed class BudgetPeriod : AggregateRoot<BudgetPeriodId>
{
    private readonly List<LedgerEntry> _entries = [];

    private BudgetPeriod() { }

    public OfficeId OfficeId { get; private set; }
    public BudgetMonth Month { get; private set; }
    public Money Limit { get; private set; }
    public Currency Currency => Limit.Currency;
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public Money Spent => SumOf(LedgerEntryType.Spend) + SumOf(LedgerEntryType.Adjust);
    public Money Reserved => SumOf(LedgerEntryType.Reserve) - SumOf(LedgerEntryType.Release);
    public Money Remaining => Limit - Spent - Reserved;
    public int SpendCount => _entries.Count(e => e.Type == LedgerEntryType.Spend);
    public int OverBudgetCount => _entries.Count(e => e.Type == LedgerEntryType.Spend && e.OverBudget);

    public static BudgetPeriod Open(OfficeId office, BudgetMonth month, Money limit)
    {
        if (limit.Amount < 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit cannot be negative.");
        return new BudgetPeriod { Id = BudgetPeriodId.New(), OfficeId = office, Month = month, Limit = limit };
    }

    public Money? OpenReservationFor(RefillId refill)
    {
        var amount = Money.Sum(_entries.Where(e => e.RefillId == refill && e.Type == LedgerEntryType.Reserve).Select(e => e.Amount), Currency)
                   - Money.Sum(_entries.Where(e => e.RefillId == refill && e.Type == LedgerEntryType.Release).Select(e => e.Amount), Currency);
        return amount.IsPositive ? amount : null;
    }

    /// <summary>
    /// Called when a volunteer commits. Fails if less than the minimum useful purchase is left.
    /// Otherwise reserves up to the typical maximum (or whatever is left). Idempotent per refill.
    /// </summary>
    public Result<Money> ReserveForRefill(RefillId refill, Money minimum, Money preferredMaximum, DateTimeOffset now)
    {
        if (minimum.Currency != Currency) return BudgetErrors.CurrencyMismatch(Currency, minimum.Currency);
        if (preferredMaximum.Currency != Currency) return BudgetErrors.CurrencyMismatch(Currency, preferredMaximum.Currency);
        if (OpenReservationFor(refill) is { } existing) return existing;

        var remaining = Remaining;
        if (remaining < minimum) return BudgetErrors.Exhausted(remaining, minimum);

        var amount = Money.Min(preferredMaximum, remaining);
        _entries.Add(new LedgerEntry(LedgerEntryType.Reserve, refill, amount, now));
        return amount;
    }

    /// <summary>Frees a reservation (deadline missed, cancelled, or replaced by the actual spend). Returns false if none was open.</summary>
    public bool Release(RefillId refill, DateTimeOffset now)
    {
        if (OpenReservationFor(refill) is not { } open) return false;
        _entries.Add(new LedgerEntry(LedgerEntryType.Release, refill, open, now));
        return true;
    }

    /// <summary>
    /// Records the actual purchase. Any reservation this refill holds in this period is released atomically,
    /// and the policy sees the remaining budget as it would be after that release. Idempotent per refill.
    /// </summary>
    public Result<SpendOutcome> RecordSpend(RefillId refill, Money actual, IBudgetPolicy policy, DateTimeOffset now)
    {
        if (actual.Currency != Currency) return BudgetErrors.CurrencyMismatch(Currency, actual.Currency);
        if (!actual.IsPositive) return BudgetErrors.InvalidAmount;

        var previous = _entries.FirstOrDefault(e => e.Type == LedgerEntryType.Spend && e.RefillId == refill);
        if (previous is not null) return new SpendOutcome(previous.Amount, previous.OverBudget, AlreadyRecorded: true);

        var remainingAfterRelease = Remaining + (OpenReservationFor(refill) ?? Money.Zero(Currency));
        var decision = policy.Evaluate(actual, remainingAfterRelease);
        if (decision == SpendDecision.Reject) return BudgetErrors.SpendRejected(actual, remainingAfterRelease);

        Release(refill, now);
        var overBudget = decision == SpendDecision.AcceptOverBudget;
        _entries.Add(new LedgerEntry(LedgerEntryType.Spend, refill, actual, now, overBudget));

        if (overBudget) Raise(new OverBudgetSpendRecorded(OfficeId, Month, refill, actual, Remaining, now));
        return new SpendOutcome(actual, overBudget, AlreadyRecorded: false);
    }

    public Result SetLimit(Money newLimit, DateTimeOffset now)
    {
        if (newLimit.Currency != Currency) return BudgetErrors.CurrencyMismatch(Currency, newLimit.Currency);
        if (newLimit.Amount < 0) return BudgetErrors.InvalidLimit;
        if (newLimit == Limit) return Result.Ok();

        var old = Limit;
        Limit = newLimit;
        Raise(new BudgetLimitChanged(OfficeId, Month, old, newLimit, now));
        return Result.Ok();
    }

    /// <summary>Manual correction, such as a refund (negative) or a voided purchase. Counts toward Spent.</summary>
    public Result Adjust(Money delta, string note, DateTimeOffset now)
    {
        if (delta.Currency != Currency) return BudgetErrors.CurrencyMismatch(Currency, delta.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        _entries.Add(new LedgerEntry(LedgerEntryType.Adjust, null, delta, now, Note: note));
        return Result.Ok();
    }

    private Money SumOf(LedgerEntryType type) =>
        Money.Sum(_entries.Where(e => e.Type == type).Select(e => e.Amount), Currency);
}
