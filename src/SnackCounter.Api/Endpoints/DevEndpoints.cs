using SnackCounter.Api.Http;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Claims;
using SnackCounter.Application.Features.Outbox;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Infrastructure.Simulated;

namespace SnackCounter.Api.Endpoints;

public sealed record AdvanceClockRequest(int Minutes);
public sealed record RejectClaimRequest(string Reason);
public sealed record FailNextRequest(int Count);

/// <summary>
/// Demo controls, only mapped when DevTools are enabled: see what the simulated chat tool and HR system
/// received, act as HR, move the clock forward, and run background jobs on demand.
/// </summary>
public static class DevEndpoints
{
    public static RouteGroupBuilder MapDevEndpoints(this RouteGroupBuilder api)
    {
        var dev = api.MapGroup("/dev").WithTags("Dev tools");

        dev.MapGet("/chat/messages", (SimulatedChatGateway chat, int? take) =>
            TypedResults.Ok(chat.Sent.TakeLast(take ?? 100).Reverse().Select(m => new
            {
                m.SentAt,
                Audience = m.Audience switch
                {
                    ChatAudience.Direct d => new { Kind = "direct", Id = d.EmployeeId.Value },
                    ChatAudience.OfficeChannel c => new { Kind = "channel", Id = c.OfficeId.Value },
                    ChatAudience.OfficeAdmins a => new { Kind = "admins", Id = a.OfficeId.Value },
                    _ => new { Kind = "unknown", Id = Guid.Empty },
                },
                m.Message.Text,
                m.Message.Actions,
            })));

        dev.MapGet("/hr/claims", (SimulatedHrGateway hr) =>
            TypedResults.Ok(hr.Claims.OrderByDescending(c => c.ReceivedAt).Select(c => new
            {
                c.HrClaimId,
                c.IdempotencyKey,
                c.Request.HrEmployeeId,
                c.Request.Amount,
                c.Request.Currency,
                c.Request.Description,
                ReceiptBytes = c.Request.ReceiptImage.Length,
                Status = c.Status.ToString(),
                c.Comment,
                c.ReceivedAt,
            })));

        dev.MapPost("/hr/claims/{hrClaimId}/approve", (string hrClaimId, SimulatedHrGateway hr) => { hr.Approve(hrClaimId); return TypedResults.NoContent(); });
        dev.MapPost("/hr/claims/{hrClaimId}/pay", (string hrClaimId, SimulatedHrGateway hr) => { hr.PayOut(hrClaimId); return TypedResults.NoContent(); });
        dev.MapPost("/hr/claims/{hrClaimId}/reject", (string hrClaimId, RejectClaimRequest body, SimulatedHrGateway hr) => { hr.Reject(hrClaimId, body.Reason); return TypedResults.NoContent(); });
        dev.MapPost("/hr/fail-next", (FailNextRequest body, SimulatedHrGateway hr) => { hr.FailNextCalls(body.Count); return TypedResults.NoContent(); });

        dev.MapGet("/clock", (TimeProvider time, AdjustableTimeProvider clock) =>
            TypedResults.Ok(new { Now = time.GetUtcNow(), clock.Offset }));

        dev.MapPost("/clock/advance", (AdvanceClockRequest body, AdjustableTimeProvider clock, TimeProvider time) =>
        {
            clock.Advance(TimeSpan.FromMinutes(body.Minutes));
            return TypedResults.Ok(new { Now = time.GetUtcNow(), clock.Offset });
        });

        dev.MapPost("/jobs/{job}", async (string job, IDispatcher dispatcher, CancellationToken ct) => job switch
        {
            "sweep" => (await dispatcher.Send(new SweepDeadlines(), ct)).ToHttp(),
            "outbox" => (await dispatcher.Send(new ProcessOutbox(), ct)).ToHttp(),
            "claims" => (await dispatcher.Send(new SyncClaimStatuses(), ct)).ToHttp(),
            "resume" => (await dispatcher.Send(new ResumeOnHoldRefills(), ct)).ToHttp(),
            _ => TypedResults.NotFound(),
        })
        .WithSummary("Run a background job now: sweep | outbox | claims | resume.");

        return api;
    }
}
