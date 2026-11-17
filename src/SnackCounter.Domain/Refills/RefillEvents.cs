using SnackCounter.Domain.Common;

namespace SnackCounter.Domain.Refills;

public sealed record RefillReported(RefillId RefillId, CounterId CounterId, OfficeId OfficeId, EmployeeId ReportedBy, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillOffered(RefillId RefillId, OfficeId OfficeId, EmployeeId Candidate, DateTimeOffset Deadline, string? Explanation, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OfferDeclined(RefillId RefillId, OfficeId OfficeId, EmployeeId EmployeeId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OfferExpired(RefillId RefillId, OfficeId OfficeId, EmployeeId EmployeeId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillCommitted(RefillId RefillId, OfficeId OfficeId, EmployeeId Assignee, DateTimeOffset PurchaseDeadline, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record PurchaseReminderDue(RefillId RefillId, OfficeId OfficeId, EmployeeId Assignee, DateTimeOffset PurchaseDeadline, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record PurchaseDeadlineMissed(RefillId RefillId, OfficeId OfficeId, EmployeeId PreviousAssignee, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillEscalated(RefillId RefillId, OfficeId OfficeId, int DirectedOffers, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillPutOnHold(RefillId RefillId, OfficeId OfficeId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillResumed(RefillId RefillId, OfficeId OfficeId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillCompleted(
    RefillId RefillId, OfficeId OfficeId, CounterId CounterId, EmployeeId Buyer, Money Amount,
    string ReceiptKey, bool IsLate, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RefillCancelled(RefillId RefillId, OfficeId OfficeId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
