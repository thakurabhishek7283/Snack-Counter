using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Overview;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Features.Refills;

/// <summary>Full history of one refill: who was asked, what happened, what was bought, where the money is.</summary>
public sealed record GetRefill(RefillId RefillId) : IQuery<RefillDetails>;

public sealed record ListRefills(OfficeId? OfficeId = null, int Take = 30) : IQuery<IReadOnlyList<RefillSummary>>;

public sealed record RefillDetails(
    RefillSummary Summary,
    string OfficeName,
    string CounterName,
    EmployeeId ReportedBy,
    string ReportedByName,
    string? CancellationReason,
    PurchaseDetails? Purchase,
    IReadOnlyList<AttemptDetails> Attempts,
    ClaimSummary? Claim);

public sealed record PurchaseDetails(EmployeeId BuyerId, string BuyerName, Money Amount, DateTimeOffset PurchasedAt, bool IsLate, IReadOnlyList<PurchaseItem> Items);

public sealed record AttemptDetails(EmployeeId EmployeeId, string Name, AttemptKind Kind, AttemptOutcome Outcome, DateTimeOffset OfferedAt, DateTimeOffset? ResolvedAt, string? Explanation);

public sealed record ClaimSummary(
    ClaimId Id, RefillId RefillId, EmployeeId EmployeeId, string EmployeeName, Money Amount, ClaimStatus Status,
    string? HrClaimId, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? PaidAt, string? RejectionReason, bool IsOverdue)
{
    public static ClaimSummary From(Claim c, string employeeName, DateTimeOffset now, TimeSpan overdueAfter) =>
        new(c.Id, c.RefillId, c.EmployeeId, employeeName, c.Amount, c.Status, c.HrClaimId, c.CreatedAt,
            c.SubmittedAt, c.PaidAt, c.RejectionReason, c.IsOverdue(now, overdueAfter));
}

public sealed class GetRefillHandler(
    IRefillRepository refills,
    IOrganisationRepository organisation,
    IClaimRepository claims,
    Microsoft.Extensions.Options.IOptions<ReimbursementOptions> options,
    TimeProvider time) : IRequestHandler<GetRefill, RefillDetails>
{
    public async Task<Result<RefillDetails>> Handle(GetRefill query, CancellationToken ct)
    {
        var refill = await refills.Get(query.RefillId, ct);
        if (refill is null) return RefillErrors.NotFound(query.RefillId);

        var names = (await organisation.ListEmployees(ct)).ToDictionary(e => e.Id, e => e.DisplayName);
        string Name(EmployeeId id) => names.GetValueOrDefault(id) ?? "Unknown";

        var office = await organisation.GetOffice(refill.OfficeId, ct);
        var counter = await organisation.GetCounter(refill.CounterId, ct);
        var claim = await claims.GetByRefill(refill.Id, ct);

        return new RefillDetails(
            RefillSummaries.From(refill, names),
            office?.Name ?? "",
            counter?.Name ?? "",
            refill.ReportedBy,
            Name(refill.ReportedBy),
            refill.CancellationReason,
            refill.Purchase is { } p ? new PurchaseDetails(p.BuyerId, Name(p.BuyerId), p.Amount, p.PurchasedAt, p.IsLate, p.Items) : null,
            refill.Attempts
                .OrderBy(a => a.OfferedAt)
                .Select(a => new AttemptDetails(a.EmployeeId, Name(a.EmployeeId), a.Kind, a.Outcome, a.OfferedAt, a.ResolvedAt, a.Explanation))
                .ToList(),
            claim is null ? null : ClaimSummary.From(claim, Name(claim.EmployeeId), time.GetUtcNow(), options.Value.OverdueAfter));
    }
}

public sealed class ListRefillsHandler(IRefillRepository refills, IOrganisationRepository organisation)
    : IRequestHandler<ListRefills, IReadOnlyList<RefillSummary>>
{
    public async Task<Result<IReadOnlyList<RefillSummary>>> Handle(ListRefills query, CancellationToken ct)
    {
        var names = (await organisation.ListEmployees(ct)).ToDictionary(e => e.Id, e => e.DisplayName);
        var recent = await refills.ListRecent(query.OfficeId, Math.Clamp(query.Take, 1, 200), ct);
        return recent.Select(r => RefillSummaries.From(r, names)).ToList();
    }
}
