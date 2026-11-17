using SnackCounter.Api.Http;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Availability;
using SnackCounter.Application.Features.Budget;
using SnackCounter.Application.Features.Chat;
using SnackCounter.Application.Features.Claims;
using SnackCounter.Application.Features.Overview;
using SnackCounter.Application.Features.People;
using SnackCounter.Application.Features.Reports;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Api.Endpoints;

public sealed record SetBudgetRequest(decimal Amount);
public sealed record SetAvailabilityRequest(DateOnly From, DateOnly To, AvailabilityKind Kind, Guid? VisitingOfficeId);
public sealed record ChatEventRequest(string ChatUserId, string Text);

public static class GeneralEndpoints
{
    public static RouteGroupBuilder MapOfficeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.WithTags("Offices and budgets");

        group.MapGet("/overview", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetOverview(), ct)).ToHttp())
            .WithSummary("Every office, its budget for this month, and the state of each snack box.");

        group.MapGet("/offices/{id:guid}/budget", async (Guid id, string? month, IDispatcher dispatcher, CancellationToken ct) =>
        {
            BudgetMonth? parsed = null;
            if (month is not null)
            {
                if (!BudgetMonth.TryParse(month, out var m)) return Error.Validation("month.invalid", "Use YYYY-MM.").ToProblem();
                parsed = m;
            }
            return (await dispatcher.Send(new GetBudgetStatus(new OfficeId(id), parsed), ct)).ToHttp();
        });

        group.MapPut("/offices/{id:guid}/budget/{month}", async (Guid id, string month, Me me, SetBudgetRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            BudgetMonth.TryParse(month, out var m)
                ? (await dispatcher.Send(new SetMonthlyBudget(new OfficeId(id), m, body.Amount, me.Id), ct)).ToHttp()
                : Error.Validation("month.invalid", "Use YYYY-MM.").ToProblem())
            .WithSummary("Office admin sets a month's limit. Refills on hold resume if there is room.");

        group.MapGet("/reports/monthly", async (string? month, string? currency, IDispatcher dispatcher, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            var m = new BudgetMonth(now.Year, now.Month);
            if (month is not null && !BudgetMonth.TryParse(month, out m)) return Error.Validation("month.invalid", "Use YYYY-MM.").ToProblem();
            return (await dispatcher.Send(new GetMonthlyReport(m, currency), ct)).ToHttp();
        })
        .WithTags("Reports")
        .WithSummary("Management's monthly report: each office in its own currency plus one consolidated total.");

        return api;
    }

    public static RouteGroupBuilder MapPeopleEndpoints(this RouteGroupBuilder api)
    {
        var group = api.WithTags("People");

        group.MapGet("/people", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ListPeople(), ct)).ToHttp());

        group.MapGet("/me", async (Me me, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetPerson(me.Id), ct)).ToHttp());

        group.MapGet("/me/availability", async (Me me, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetMyAvailability(me.Id), ct)).ToHttp());

        group.MapPost("/me/availability", async (Me me, SetAvailabilityRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SetAvailability(me.Id, body.From, body.To, body.Kind,
                body.VisitingOfficeId is { } o ? new OfficeId(o) : null), ct))
                .ToHttp(id => TypedResults.Created($"/api/me/availability/{id}", new { id })))
            .WithSummary("Declare a holiday, remote days, or a visit to another office.");

        group.MapGet("/me/claims", async (Me me, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetMyClaims(me.Id), ct)).ToHttp());

        group.MapPost("/claims/{id:guid}/resubmit", async (Guid id, Me me, IFormFile receipt, IDispatcher dispatcher, CancellationToken ct) =>
            {
                await using var stream = receipt.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                return (await dispatcher.Send(new ResubmitClaim(new ClaimId(id), me.Id, buffer.ToArray(), receipt.ContentType), ct))
                    .ToHttp(_ => TypedResults.NoContent());
            })
            .DisableAntiforgery();

        return api;
    }

    public static RouteGroupBuilder MapIntegrationEndpoints(this RouteGroupBuilder api)
    {
        // Production: verify the chat tool's request signature here before dispatching.
        api.MapPost("/integrations/chat/events", async (ChatEventRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new HandleChatEvent(body.ChatUserId, body.Text), ct)).ToHttp())
            .WithTags("Integrations")
            .WithSummary("Chat webhook: slash commands and button clicks. Always replies with a message.");

        return api;
    }
}
