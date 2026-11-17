using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Refills;

public static class RefillErrors
{
    public static Error NotFound(RefillId id) =>
        Error.NotFound("refill.not_found", $"Refill {id} does not exist.");

    public static Error InvalidTransition(RefillStatus from, RefillTrigger trigger) =>
        Error.Conflict("refill.invalid_transition", $"Cannot {trigger.ToString().ToLowerInvariant()} a refill that is {from}.");

    public static Error AlreadyTaken(EmployeeId assignee) =>
        Error.Conflict("refill.already_taken", $"This refill has already been taken by employee {assignee}.");

    public static Error NotOfferedTo(EmployeeId employee) =>
        Error.Conflict("refill.not_offered_to_you", $"This refill is currently offered to someone else, not {employee}.");

    public static Error AlreadyAttempted(EmployeeId employee) =>
        Error.Conflict("refill.already_attempted", $"Employee {employee} has already had a turn at this refill.");

    public static Error NotAssignee(EmployeeId employee) =>
        Error.Forbidden("refill.not_assignee", $"Employee {employee} is not responsible for this refill.");

    public static Error WrongCurrency(Currency expected, Currency actual) =>
        Error.Validation("refill.wrong_currency", $"Purchases at this counter must be in {expected}, got {actual}.");

    public static readonly Error DeadlineNotDue =
        Error.Rule("refill.deadline_not_due", "The deadline for this refill has not passed yet.");

    public static readonly Error AmountMustBePositive =
        Error.Validation("refill.amount_not_positive", "The purchase amount must be greater than zero.");

    public static readonly Error ReceiptRequired =
        Error.Validation("refill.receipt_required", "A receipt is required.");

    public static readonly Error ReasonRequired =
        Error.Validation("refill.reason_required", "Give a reason for cancelling.");

    public static readonly Error ConcurrentUpdate =
        Error.Conflict("refill.concurrent_update", "Someone else changed this refill at the same moment. Refresh and try again.");
}
