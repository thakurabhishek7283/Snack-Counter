using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Budgeting;

public enum SpendDecision { Accept, AcceptOverBudget, Reject }

/// <summary>What to do with a purchase given the remaining budget. Swap the policy, not the code (strategy).</summary>
public interface IBudgetPolicy
{
    SpendDecision Evaluate(Money actual, Money remaining);
}

/// <summary>
/// Default. The volunteer has already paid with their own money, so we always record (and reimburse)
/// the spend. Going over budget is flagged to the office admin.
/// </summary>
public sealed class SoftCapBudgetPolicy : IBudgetPolicy
{
    public SpendDecision Evaluate(Money actual, Money remaining) =>
        actual <= remaining ? SpendDecision.Accept : SpendDecision.AcceptOverBudget;
}

/// <summary>Alternative: refuse anything that does not fit. Shown to demonstrate the extension point.</summary>
public sealed class HardCapBudgetPolicy : IBudgetPolicy
{
    public SpendDecision Evaluate(Money actual, Money remaining) =>
        actual <= remaining ? SpendDecision.Accept : SpendDecision.Reject;
}

public static class BudgetErrors
{
    public static Error Exhausted(Money remaining, Money minimum) =>
        Error.Rule("budget.exhausted", $"Only {remaining} left this month, less than the minimum purchase of {minimum}.");

    public static Error CurrencyMismatch(Currency expected, Currency actual) =>
        Error.Validation("budget.currency_mismatch", $"This budget is in {expected}, got {actual}.");

    public static Error SpendRejected(Money actual, Money remaining) =>
        Error.Rule("budget.spend_rejected", $"A purchase of {actual} exceeds the remaining budget of {remaining}.");

    public static readonly Error InvalidLimit =
        Error.Validation("budget.invalid_limit", "The monthly limit cannot be negative.");

    public static readonly Error InvalidAmount =
        Error.Validation("budget.invalid_amount", "The amount must be greater than zero.");
}

public sealed record OverBudgetSpendRecorded(OfficeId OfficeId, BudgetMonth Month, RefillId RefillId, Money Amount, Money RemainingAfter, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BudgetLimitChanged(OfficeId OfficeId, BudgetMonth Month, Money OldLimit, Money NewLimit, DateTimeOffset OccurredAt) : IDomainEvent;
