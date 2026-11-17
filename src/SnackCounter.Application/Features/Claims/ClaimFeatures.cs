using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Features.Claims;

/// <summary>
/// Outbox handler: sends a pending claim to HR. Safe to run more than once, because HR deduplicates on the
/// claim's idempotency key and an already-submitted claim is skipped.
/// </summary>
public sealed class SubmitClaimToHrHandler(
    IClaimRepository claims,
    IOrganisationRepository organisation,
    IReceiptStore receipts,
    IHrGateway hr,
    TimeProvider time) : IOutboxMessageHandler<SubmitClaimToHr>
{
    public async Task Handle(SubmitClaimToHr payload, CancellationToken ct)
    {
        var claim = await claims.Get(payload.ClaimId, ct) ?? throw new InvalidOperationException($"Claim {payload.ClaimId} not found.");
        if (claim.Status != ClaimStatus.Pending) return;

        var employee = await organisation.GetEmployee(claim.EmployeeId, ct)
            ?? throw new InvalidOperationException($"Employee {claim.EmployeeId} not found.");
        var receipt = await receipts.Get(claim.ReceiptKey, ct);

        var request = new HrClaimRequest(
            employee.HrEmployeeId, claim.Amount.Amount, claim.Amount.Currency.Code, claim.Description,
            receipt.Content, receipt.ContentType);

        var hrClaimId = await hr.CreateClaim(request, claim.IdempotencyKey, ct);
        claim.MarkSubmitted(hrClaimId, time.GetUtcNow());
    }
}

/// <summary>Run hourly. Pulls claim status from HR and flags claims that have waited more than 10 days.</summary>
public sealed record SyncClaimStatuses : ICommand<SyncClaimsResult>;

public sealed record SyncClaimsResult(int Checked, int Changed, int FlaggedOverdue, int Failed);

public sealed class SyncClaimStatusesHandler(
    IClaimRepository claims,
    IHrGateway hr,
    IUnitOfWork unitOfWork,
    IOptions<ReimbursementOptions> options,
    TimeProvider time,
    ILogger<SyncClaimStatusesHandler> logger) : IRequestHandler<SyncClaimStatuses, SyncClaimsResult>
{
    public async Task<Result<SyncClaimsResult>> Handle(SyncClaimStatuses command, CancellationToken ct)
    {
        int checkedCount = 0, changed = 0, overdue = 0, failed = 0;

        foreach (var claim in await claims.ListAwaitingHr(ct))
        {
            checkedCount++;
            var before = claim.Status;
            try
            {
                var report = await hr.GetClaimStatus(claim.HrClaimId!, ct);
                var applied = claim.ApplyHrStatus(report.Status, time.GetUtcNow(), report.Reason);
                if (applied.IsFailure)
                    logger.LogWarning("HR reported {Reported} for claim {ClaimId} in state {State}: {Error}", report.Status, claim.Id, claim.Status, applied.Error!.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(ex, "Could not fetch HR status for claim {ClaimId}", claim.Id);
            }

            if (claim.Status != before) changed++;
            if (claim.FlagIfOverdue(time.GetUtcNow(), options.Value.OverdueAfter)) overdue++;
        }

        await unitOfWork.SaveChanges(ct);
        return new SyncClaimsResult(checkedCount, changed, overdue, failed);
    }
}

/// <summary>After HR rejects a claim (for example an unreadable receipt), the buyer uploads a new receipt.</summary>
public sealed record ResubmitClaim(ClaimId ClaimId, EmployeeId EmployeeId, byte[] Receipt, string ReceiptContentType) : ICommand<Unit>;

public sealed class ResubmitClaimHandler(
    IClaimRepository claims,
    IReceiptStore receipts,
    IUnitOfWork unitOfWork,
    TimeProvider time) : IRequestHandler<ResubmitClaim, Unit>
{
    public async Task<Result<Unit>> Handle(ResubmitClaim command, CancellationToken ct)
    {
        if (command.Receipt is not { Length: > 0 }) return AppErrors.ReceiptRequired;
        var claim = await claims.Get(command.ClaimId, ct);
        if (claim is null) return ClaimErrors.NotFound(command.ClaimId);
        if (claim.EmployeeId != command.EmployeeId) return Error.Forbidden("claim.not_owner", "You can only resubmit your own claims.");
        if (claim.Status != ClaimStatus.Rejected) return ClaimErrors.InvalidTransition(claim.Status, ClaimStatus.Pending);

        var key = await receipts.Save(command.Receipt, command.ReceiptContentType, ct);
        var resubmitted = claim.Resubmit(key, time.GetUtcNow());
        if (resubmitted.IsFailure) return resubmitted.Error!;

        await unitOfWork.SaveChanges(ct);
        return Unit.Value;
    }
}
