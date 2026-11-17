using System.Globalization;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Infrastructure.Simulated;

/// <summary>The HR system's own vocabulary. Deliberately different from ours, to show the anti-corruption mapping.</summary>
public enum HrSimStatus { Received, Approved, Rejected, PaidOut }

public sealed class SimulatedHrClaim(string hrClaimId, string idempotencyKey, HrClaimRequest request, DateTimeOffset receivedAt)
{
    public string HrClaimId { get; } = hrClaimId;
    public string IdempotencyKey { get; } = idempotencyKey;
    public HrClaimRequest Request { get; } = request;
    public DateTimeOffset ReceivedAt { get; } = receivedAt;
    public HrSimStatus Status { get; internal set; } = HrSimStatus.Received;
    public string? Comment { get; internal set; }
}

/// <summary>
/// Stand-in for the external HR system. Honours idempotency keys like a well-behaved API, can be told
/// to fail (to prove retries never duplicate claims), and lets a test or the dev panel approve, reject or pay.
/// </summary>
public sealed class SimulatedHrGateway(TimeProvider time) : IHrGateway
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, SimulatedHrClaim> _byId = [];
    private readonly Dictionary<string, string> _idByKey = [];
    private int _sequence;
    private int _failuresToInject;

    public int CreateCalls { get; private set; }

    public IReadOnlyList<SimulatedHrClaim> Claims
    {
        get { lock (_sync) return _byId.Values.ToList(); }
    }

    /// <summary>The next <paramref name="count"/> calls throw, as if HR were down.</summary>
    public void FailNextCalls(int count)
    {
        lock (_sync) _failuresToInject = count;
    }

    public Task<string> CreateClaim(HrClaimRequest request, string idempotencyKey, CancellationToken ct)
    {
        lock (_sync)
        {
            CreateCalls++;
            ThrowIfFailureInjected();

            if (_idByKey.TryGetValue(idempotencyKey, out var existing)) return Task.FromResult(existing);

            var id = string.Create(CultureInfo.InvariantCulture, $"HR-{++_sequence:D5}");
            _byId[id] = new SimulatedHrClaim(id, idempotencyKey, request, time.GetUtcNow());
            _idByKey[idempotencyKey] = id;
            return Task.FromResult(id);
        }
    }

    public Task<HrClaimStatusReport> GetClaimStatus(string hrClaimId, CancellationToken ct)
    {
        lock (_sync)
        {
            ThrowIfFailureInjected();
            var claim = _byId.TryGetValue(hrClaimId, out var c) ? c : throw new KeyNotFoundException($"HR has no claim {hrClaimId}.");

            // Anti-corruption layer: translate HR's statuses into ours.
            var status = claim.Status switch
            {
                HrSimStatus.Received => ClaimStatus.Submitted,
                HrSimStatus.Approved => ClaimStatus.Approved,
                HrSimStatus.Rejected => ClaimStatus.Rejected,
                HrSimStatus.PaidOut => ClaimStatus.Paid,
                _ => throw new InvalidOperationException($"Unknown HR status {claim.Status}."),
            };
            return Task.FromResult(new HrClaimStatusReport(status, claim.Comment));
        }
    }

    public void Approve(string hrClaimId) => Set(hrClaimId, HrSimStatus.Approved, null);
    public void Reject(string hrClaimId, string reason) => Set(hrClaimId, HrSimStatus.Rejected, reason);
    public void PayOut(string hrClaimId) => Set(hrClaimId, HrSimStatus.PaidOut, null);

    private void Set(string hrClaimId, HrSimStatus status, string? comment)
    {
        lock (_sync)
        {
            var claim = _byId[hrClaimId];
            claim.Status = status;
            claim.Comment = comment;
        }
    }

    private void ThrowIfFailureInjected()
    {
        if (_failuresToInject <= 0) return;
        _failuresToInject--;
        throw new HttpRequestException("HR system unavailable (simulated).");
    }
}
