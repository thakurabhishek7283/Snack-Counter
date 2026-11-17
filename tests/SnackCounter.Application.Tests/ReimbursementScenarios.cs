using Shouldly;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Claims;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Tests;

public class ReimbursementScenarios
{
    [Fact]
    public async Task Claim_reaches_HR_exactly_once_even_when_HR_is_down_at_first()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var (refill, _) = await app.RunRefillCycle(app.OsloCounter, asha, 850);

        app.Hr.FailNextCalls(1);
        await app.DeliverOutbox();
        app.ClaimFor(refill).Status.ShouldBe(ClaimStatus.Pending);
        app.Hr.Claims.ShouldBeEmpty();

        app.Clock.Advance(TimeSpan.FromSeconds(6)); // past the first backoff
        await app.DeliverOutbox();
        app.ClaimFor(refill).Status.ShouldBe(ClaimStatus.Submitted);
        app.Hr.Claims.ShouldHaveSingleItem();

        // A duplicate delivery of the same outbox message must not create a second HR claim.
        var calls = app.Hr.CreateCalls;
        app.Db.Outbox.Add(new OutboxMessage(new SubmitClaimToHr(app.ClaimFor(refill).Id), app.Clock.GetUtcNow()));
        await app.DeliverOutbox();
        app.Hr.Claims.ShouldHaveSingleItem();
        app.Hr.CreateCalls.ShouldBe(calls);
    }

    [Fact]
    public async Task Claim_waiting_more_than_ten_days_is_flagged_to_the_admin_once()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var (refill, _) = await app.RunRefillCycle(app.OsloCounter, asha, 850);
        await app.DeliverOutbox();

        app.Clock.Advance(TimeSpan.FromDays(9));
        (await app.Ok(new SyncClaimStatuses())).FlaggedOverdue.ShouldBe(0);

        app.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromHours(1));
        (await app.Ok(new SyncClaimStatuses())).FlaggedOverdue.ShouldBe(1);
        (await app.Ok(new SyncClaimStatuses())).FlaggedOverdue.ShouldBe(0);

        await app.DeliverOutbox();
        app.Chat.AdminMessages(app.Oslo.Id).ShouldHaveSingleItem().Text.ShouldContain("more than 10 days");
        app.ClaimFor(refill).Status.ShouldBe(ClaimStatus.Submitted);
    }

    [Fact]
    public async Task Rejected_claim_can_be_resubmitted_with_a_new_receipt()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var (refill, _) = await app.RunRefillCycle(app.OsloCounter, asha, 850);
        await app.DeliverOutbox();
        var first = app.Hr.Claims.Single();

        app.Hr.Reject(first.HrClaimId, "Receipt unreadable");
        await app.Ok(new SyncClaimStatuses());
        await app.DeliverOutbox();
        app.ClaimFor(refill).Status.ShouldBe(ClaimStatus.Rejected);
        app.Chat.DirectMessagesTo(asha.Id).Last().Text.ShouldContain("Receipt unreadable");

        await app.Ok(new ResubmitClaim(app.ClaimFor(refill).Id, asha.Id, [9, 9, 9], "image/png"));
        await app.DeliverOutbox();

        app.ClaimFor(refill).Status.ShouldBe(ClaimStatus.Submitted);
        app.Hr.Claims.Count.ShouldBe(2);
        app.Hr.Claims.Select(c => c.IdempotencyKey).Distinct().Count().ShouldBe(2);
    }

    // Fairness: nobody should end up fronting lots of money while waiting on HR.
    [Fact]
    public async Task Volunteer_already_owed_two_claims_is_skipped_for_the_next_refill()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, 5, daysAgo: 2); // on score alone, Asha would always be picked

        (await app.RunRefillCycle(app.OsloCounter, bjorn, 600)).Buyer.ShouldBe(asha.Id);
        (await app.RunRefillCycle(app.OsloCounter, bjorn, 600)).Buyer.ShouldBe(asha.Id);

        var third = await app.Ok(new ReportEmpty(app.OsloCounter.Id, asha.Id));

        third.AssigneeId.ShouldBe(bjorn.Id);
    }
}
