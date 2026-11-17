using Microsoft.AspNetCore.Mvc;
using SnackCounter.Api.Http;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Api.Endpoints;

public sealed record CancelRefillRequest(string Reason);

public static class RefillEndpoints
{
    private const long MaxReceiptBytes = 10 * 1024 * 1024;

    public static RouteGroupBuilder MapRefillEndpoints(this RouteGroupBuilder api)
    {
        var group = api.WithTags("Refills");

        group.MapPost("/counters/{counterId:guid}/refills", async (Guid counterId, Me me, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ReportEmpty(new CounterId(counterId), me.Id), ct))
                .ToHttp(r => r.AlreadyReported ? TypedResults.Ok(r) : TypedResults.Created($"/api/refills/{r.RefillId}", r)))
            .WithSummary("Report an empty snack box. Returns the existing refill if one is already active.");

        group.MapGet("/refills", async (Guid? officeId, int? take, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ListRefills(officeId is { } o ? new OfficeId(o) : null, take ?? 30), ct)).ToHttp());

        group.MapGet("/refills/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetRefill(new RefillId(id)), ct)).ToHttp());

        group.MapPost("/refills/{id:guid}/commit", async (Guid id, Me me, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new CommitToRefill(new RefillId(id), me.Id), ct)).ToHttp())
            .WithSummary("\"I'll buy it.\" First valid commit wins; everyone else gets 409.");

        group.MapPost("/refills/{id:guid}/decline", async (Guid id, Me me, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new DeclineRefill(new RefillId(id), me.Id), ct)).ToHttp(_ => TypedResults.NoContent()));

        group.MapPost("/refills/{id:guid}/complete", async (
                Guid id, Me me, [FromForm] decimal amount, [FromForm] string currency, IFormFile receipt,
                [FromForm] string? items, IDispatcher dispatcher, CancellationToken ct) =>
            {
                if (receipt.Length is 0 or > MaxReceiptBytes)
                    return Error.Validation("receipt.size", "The receipt must be between 1 byte and 10 MB.").ToProblem();

                await using var stream = receipt.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);

                var command = new CompleteRefill(new RefillId(id), me.Id, amount, currency, buffer.ToArray(),
                    receipt.ContentType, ParseItems(items));
                return (await dispatcher.Send(command, ct)).ToHttp();
            })
            .DisableAntiforgery() // header-authenticated API, not cookie-based
            .WithSummary("Upload the receipt. Records the spend and sends the claim to HR automatically.");

        group.MapPost("/refills/{id:guid}/cancel", async (Guid id, Me me, CancelRefillRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new CancelRefill(new RefillId(id), me.Id, body.Reason), ct)).ToHttp(_ => TypedResults.NoContent()));

        return api;
    }

    /// <summary>"Chips x2, Chocolate" becomes two items. Optional, for the "what gets eaten" insight.</summary>
    private static List<PurchaseItem>? ParseItems(string? items)
    {
        if (string.IsNullOrWhiteSpace(items)) return null;
        return items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                var parts = item.Split(" x", 2, StringSplitOptions.TrimEntries);
                return parts.Length == 2 && int.TryParse(parts[1], out var qty) && qty > 0
                    ? new PurchaseItem(parts[0], qty)
                    : new PurchaseItem(item);
            })
            .ToList();
    }
}
