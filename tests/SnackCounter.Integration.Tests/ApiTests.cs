using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using SnackCounter.Domain.Organisation;
using SnackCounter.Infrastructure.Seed;
using SnackCounter.Integration.Tests.Infrastructure;

namespace SnackCounter.Integration.Tests;

/// <summary>The real API (Program.cs, endpoints, JSON, ProblemDetails) on a real PostgreSQL database.</summary>
[Collection(PostgresCollection.Name)]
public class ApiTests(PostgresFixture postgres)
{
    private static readonly Employee Lars = People.Named("Lars"); // Oslo volunteer, in the office on Tuesdays
    private static readonly Employee Ingrid = People.Named("Ingrid"); // Oslo office admin

    [SkippableFact]
    public async Task Full_lifecycle_from_empty_to_reimbursed_over_http()
    {
        await using var api = new ApiHost(postgres.NewDatabase());
        using var reporter = api.ClientAs(Lars);

        // Report: the system picks a volunteer.
        var report = await reporter.PostAsync($"/api/counters/{DemoData.OsloKitchen.Value}/refills", null);
        report.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reported = await Json(report);
        var refillId = (string)reported["refillId"]!;
        reported["status"]!.GetValue<string>().ShouldBe("Offered");
        var buyer = People.OsloVolunteers.Single(v => v.Id.Value == Guid.Parse((string)reported["assigneeId"]!));

        // Commit: told how much to spend.
        using var buyerClient = api.ClientAs(buyer);
        var commit = await Json(await buyerClient.PostAsync($"/api/refills/{refillId}/commit", null), HttpStatusCode.OK);
        commit["spendUpTo"]!["amount"]!.GetValue<decimal>().ShouldBe(1200m);
        commit["spendUpTo"]!["currency"]!.GetValue<string>().ShouldBe("NOK");

        // Complete: receipt upload as multipart form.
        var completed = await Json(await buyerClient.PostAsync($"/api/refills/{refillId}/complete", Receipt(850, "NOK", "Chips x2, Chocolate")), HttpStatusCode.OK);
        completed["overBudget"]!.GetValue<bool>().ShouldBeFalse();

        // Outbox delivers the claim to (simulated) HR, which approves and pays.
        (await reporter.PostAsync("/api/dev/jobs/outbox", null)).EnsureSuccessStatusCode();
        var hrClaims = (await Json(await reporter.GetAsync("/api/dev/hr/claims"))).AsArray();
        var hrClaim = hrClaims.ShouldHaveSingleItem()!;
        hrClaim["amount"]!.GetValue<decimal>().ShouldBe(850m);
        hrClaim["hrEmployeeId"]!.GetValue<string>().ShouldBe(buyer.HrEmployeeId);
        var hrClaimId = (string)hrClaim["hrClaimId"]!;
        (await reporter.PostAsync($"/api/dev/hr/claims/{hrClaimId}/pay", null)).EnsureSuccessStatusCode();
        (await reporter.PostAsync("/api/dev/jobs/claims", null)).EnsureSuccessStatusCode();

        // The refill's history shows the whole story.
        var details = await Json(await reporter.GetAsync($"/api/refills/{refillId}"), HttpStatusCode.OK);
        details["summary"]!["status"]!.GetValue<string>().ShouldBe("Completed");
        details["purchase"]!["items"]!.AsArray().Count.ShouldBe(2);
        details["claim"]!["status"]!.GetValue<string>().ShouldBe("Paid");

        // Budget and the management report reflect it.
        var overview = (await Json(await reporter.GetAsync("/api/overview"))).AsArray();
        var oslo = overview.Single(o => (string)o!["name"]! == "Oslo")!;
        oslo["budget"]!["spent"]!["amount"]!.GetValue<decimal>().ShouldBe(850m);
        oslo["counters"]![0]!["activeRefill"].ShouldBeNull();

        var monthly = await Json(await reporter.GetAsync("/api/reports/monthly?month=2026-11"), HttpStatusCode.OK);
        monthly["total"]!["amount"]!.GetValue<decimal>().ShouldBe(850m);
        monthly["total"]!["currency"]!.GetValue<string>().ShouldBe("NOK");
    }

    // Rule: two people cannot both commit, under real concurrent HTTP load.
    [SkippableFact]
    public async Task Ten_volunteers_racing_to_accept_exactly_one_wins()
    {
        await using var api = new ApiHost(postgres.NewDatabase(), new Dictionary<string, string> { ["Assignment:MaxDirectedOffers"] = "0" });
        using var reporter = api.ClientAs(Lars);
        var reported = await Json(await reporter.PostAsync($"/api/counters/{DemoData.OsloKitchen.Value}/refills", null));
        reported["status"]!.GetValue<string>().ShouldBe("Escalated"); // open to everyone: first to accept gets it
        var refillId = (string)reported["refillId"]!;

        var racers = People.OsloVolunteers.Take(10).Select(api.ClientAs).ToList();
        var responses = await Task.WhenAll(racers.Select(c => c.PostAsync($"/api/refills/{refillId}/commit", null)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);

        var budget = await Json(await reporter.GetAsync($"/api/offices/{DemoData.OsloId.Value}/budget"));
        budget["reserved"]!["amount"]!.GetValue<decimal>().ShouldBe(1200m); // reserved once
        racers.ForEach(c => c.Dispose());
    }

    // Rule: no duplicate refills, under real concurrent HTTP load.
    [SkippableFact]
    public async Task Simultaneous_empty_reports_create_one_refill()
    {
        await using var api = new ApiHost(postgres.NewDatabase());
        var reporters = People.OsloVolunteers.Take(8).Select(api.ClientAs).ToList();

        var responses = await Task.WhenAll(reporters.Select(c => c.PostAsync($"/api/counters/{DemoData.OsloKitchen.Value}/refills", null)));

        responses.ShouldAllBe(r => r.IsSuccessStatusCode);
        var ids = await Task.WhenAll(responses.Select(async r => (string)(await Json(r))["refillId"]!));
        ids.Distinct().ShouldHaveSingleItem();
        reporters.ForEach(c => c.Dispose());
    }

    // Rule: a refill nobody completes is handed to someone else, with the sweep running against PostgreSQL.
    [SkippableFact]
    public async Task Unanswered_offer_moves_to_the_next_volunteer_after_two_hours()
    {
        await using var api = new ApiHost(postgres.NewDatabase());
        using var client = api.ClientAs(Lars);
        var reported = await Json(await client.PostAsync($"/api/counters/{DemoData.OsloKitchen.Value}/refills", null));
        var refillId = (string)reported["refillId"]!;
        var first = (string)reported["assigneeId"]!;

        api.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        var sweep = await Json(await client.PostAsync("/api/dev/jobs/sweep", null), HttpStatusCode.OK);

        sweep["expired"]!.GetValue<int>().ShouldBe(1);
        var details = await Json(await client.GetAsync($"/api/refills/{refillId}"));
        ((string)details["summary"]!["assigneeId"]!).ShouldNotBe(first);
        details["attempts"]!.AsArray().First()!["outcome"]!.GetValue<string>().ShouldBe("OfferExpired");
    }

    [SkippableFact]
    public async Task Errors_come_back_as_problem_details_with_stable_codes()
    {
        await using var api = new ApiHost(postgres.NewDatabase());
        using var anonymous = api.CreateClient();
        using var lars = api.ClientAs(Lars);

        (await anonymous.PostAsync($"/api/counters/{DemoData.OsloKitchen.Value}/refills", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var notFound = await lars.PostAsync($"/api/refills/{Guid.NewGuid()}/commit", null);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        notFound.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await Json(notFound))["errorCode"]!.GetValue<string>().ShouldBe("refill.not_found");

        var forbidden = await lars.PutAsJsonAsync($"/api/offices/{DemoData.OsloId.Value}/budget/2026-11", new { amount = 9000 });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(forbidden))["errorCode"]!.GetValue<string>().ShouldBe("employee.not_office_admin");

        using var ingrid = api.ClientAs(Ingrid);
        var raised = await Json(await ingrid.PutAsJsonAsync($"/api/offices/{DemoData.OsloId.Value}/budget/2026-11", new { amount = 9000 }), HttpStatusCode.OK);
        raised["limit"]!["amount"]!.GetValue<decimal>().ShouldBe(9000m);
    }

    [SkippableFact]
    public async Task Chat_webhook_reports_and_accepts_through_the_same_use_cases()
    {
        await using var api = new ApiHost(postgres.NewDatabase());
        using var client = api.CreateClient();

        var reply = await Json(await client.PostAsJsonAsync("/api/integrations/chat/events", new { chatUserId = Lars.ChatUserId, text = "/snacks empty" }), HttpStatusCode.OK);
        ((string)reply["text"]!).ShouldStartWith("Thanks for reporting it!");

        var status = await Json(await client.PostAsJsonAsync("/api/integrations/chat/events", new { chatUserId = Lars.ChatUserId, text = "/snacks status" }));
        ((string)status["text"]!).ShouldContain("Oslo / Kitchen");
    }

    private static MultipartFormDataContent Receipt(decimal amount, string currency, string? items = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "amount" },
            { new StringContent(currency), "currency" },
        };
        if (items is not null) form.Add(new StringContent(items), "items");
        var image = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "receipt", "receipt.jpg");
        return form;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage response, HttpStatusCode? expected = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (expected is not null) response.StatusCode.ShouldBe(expected.Value, body);
        return JsonNode.Parse(body) ?? throw new InvalidOperationException("Empty response body.");
    }
}
