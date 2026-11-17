using Shouldly;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Budget;
using SnackCounter.Application.Features.Claims;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Tests;

public class RefillLifecycleScenarios
{
    [Fact]
    public async Task Empty_to_reimbursed_end_to_end_through_chat()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        var kari = app.Volunteer("Kari", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, count: 2, daysAgo: 10);
        app.SeedPastRefills(kari, app.Oslo, count: 1, daysAgo: 5);

        // 1. Kari reports the empty box in chat. Asha has done the fewest refills, so she is asked.
        (await app.ChatAs(kari, "/snacks empty")).ShouldBe("Thanks for reporting it! I've asked Asha to refill it.");
        var refill = app.ActiveRefill(app.OsloCounter);
        refill.Status.ShouldBe(RefillStatus.Offered);
        refill.AssigneeId.ShouldBe(asha.Id);

        // 2. Asha gets a DM with buttons and accepts.
        await app.DeliverOutbox();
        var offer = app.Chat.DirectMessagesTo(asha.Id).ShouldHaveSingleItem();
        offer.Text.ShouldContain("0 refills in the last 90 days");
        var accept = offer.Actions.Single(a => a.Value.StartsWith("accept:", StringComparison.Ordinal));
        (await app.ChatAs(asha, accept.Value)).ShouldStartWith("You're on it, thank you! Spend up to 1200.00 NOK");
        app.Budget(app.Oslo)!.Reserved.ShouldBe(Money.Of(1200, "NOK"));

        // 3. Three hours later she uploads an 850 NOK receipt.
        app.Clock.Advance(TimeSpan.FromHours(3));
        var completed = await app.Complete(refill.Id, asha, 850);
        completed.OverBudget.ShouldBeFalse();
        app.Refill(refill.Id).Status.ShouldBe(RefillStatus.Completed);
        app.Budget(app.Oslo)!.Spent.ShouldBe(Money.Of(850, "NOK"));
        app.Budget(app.Oslo)!.Reserved.ShouldBe(Money.Of(0, "NOK"));
        app.Budget(app.Oslo)!.Remaining.ShouldBe(Money.Of(5150, "NOK"));

        // 4. The claim goes to HR automatically, with everything HR asks for.
        await app.DeliverOutbox();
        var hrClaim = app.Hr.Claims.ShouldHaveSingleItem();
        hrClaim.Request.HrEmployeeId.ShouldBe("HR-Asha");
        hrClaim.Request.Amount.ShouldBe(850m);
        hrClaim.Request.Currency.ShouldBe("NOK");
        hrClaim.Request.ReceiptImage.ShouldNotBeEmpty();
        hrClaim.Request.Description.ShouldContain("Oslo");
        app.ClaimFor(refill.Id).Status.ShouldBe(ClaimStatus.Submitted);

        // 5. HR approves after 3 days and pays after 5. Our poller picks it up.
        app.Clock.Advance(TimeSpan.FromDays(3));
        app.Hr.Approve(hrClaim.HrClaimId);
        await app.Ok(new SyncClaimStatuses());
        app.ClaimFor(refill.Id).Status.ShouldBe(ClaimStatus.Approved);

        app.Clock.Advance(TimeSpan.FromDays(2));
        app.Hr.PayOut(hrClaim.HrClaimId);
        await app.Ok(new SyncClaimStatuses());
        app.ClaimFor(refill.Id).Status.ShouldBe(ClaimStatus.Paid);

        // 6. Everyone was kept in the loop.
        await app.DeliverOutbox();
        app.Chat.DirectMessagesTo(asha.Id).Last().Text.ShouldContain("has been reimbursed");
        var channel = app.Chat.ChannelMessages(app.Oslo.Id).Select(m => m.Text).ToList();
        channel.ShouldContain(t => t.Contains("Asha is refilling", StringComparison.Ordinal));
        channel.ShouldContain(t => t.Contains("refilled by Asha", StringComparison.Ordinal));
    }

    // Rule: no duplicate refills (the "two people buy on the same day" problem starts here).
    [Fact]
    public async Task Second_empty_report_returns_the_existing_refill()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var kari = app.Colleague("Kari", app.Oslo);
        var lars = app.Colleague("Lars", app.Oslo);

        var first = await app.Ok(new ReportEmpty(app.OsloCounter.Id, kari.Id));
        var second = await app.Ok(new ReportEmpty(app.OsloCounter.Id, lars.Id));

        second.AlreadyReported.ShouldBeTrue();
        second.RefillId.ShouldBe(first.RefillId);
        second.AssigneeId.ShouldBe(asha.Id);
        app.Db.Refills.Count.ShouldBe(1);
    }

    // Rule: two people cannot both commit to the same refill.
    [Fact]
    public async Task Two_volunteers_cannot_both_commit_to_the_same_refill()
    {
        using var app = new TestApp(o => o.MaxDirectedOffers = 0); // straight to "first to accept gets it"
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, asha.Id));
        report.Status.ShouldBe(RefillStatus.Escalated);

        await app.Ok(new CommitToRefill(report.RefillId, asha.Id));
        var error = await app.Fails(new CommitToRefill(report.RefillId, bjorn.Id));

        error.Code.ShouldBe("refill.already_taken");
        app.Refill(report.RefillId).AssigneeId.ShouldBe(asha.Id);
        app.Budget(app.Oslo)!.Reserved.ShouldBe(Money.Of(1200, "NOK")); // reserved once, not twice
        (await app.ChatAs(bjorn, $"accept:{report.RefillId}")).ShouldBe("Thanks, but someone else already took this refill.");
    }

    [Fact]
    public async Task Nobody_but_the_offered_volunteer_can_take_a_directed_offer()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, 3, daysAgo: 7);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, bjorn.Id));
        report.AssigneeId.ShouldBe(asha.Id);

        (await app.Fails(new CommitToRefill(report.RefillId, bjorn.Id))).Code.ShouldBe("refill.not_offered_to_you");
    }

    // Rule: a refill nobody completes is handed to someone else (unanswered offer).
    [Fact]
    public async Task Unanswered_offer_is_handed_to_the_next_volunteer_after_the_ttl()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, 1, daysAgo: 20);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, bjorn.Id));
        report.AssigneeId.ShouldBe(asha.Id);

        app.Clock.Advance(TimeSpan.FromHours(1));
        (await app.Ok(new SweepDeadlines())).Expired.ShouldBe(0);

        app.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        (await app.Ok(new SweepDeadlines())).Expired.ShouldBe(1);

        var refill = app.Refill(report.RefillId);
        refill.Status.ShouldBe(RefillStatus.Offered);
        refill.AssigneeId.ShouldBe(bjorn.Id);
        refill.Attempts.Single(a => a.EmployeeId == asha.Id).Outcome.ShouldBe(AttemptOutcome.OfferExpired);
    }

    // Rule: a refill nobody completes is handed to someone else (committed but never bought).
    [Fact]
    public async Task Missed_purchase_deadline_releases_the_budget_and_hands_the_refill_on()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, 1, daysAgo: 20);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, bjorn.Id));
        await app.Ok(new CommitToRefill(report.RefillId, asha.Id));
        app.Budget(app.Oslo)!.Reserved.ShouldBe(Money.Of(1200, "NOK"));

        app.Clock.Advance(TimeSpan.FromHours(12));
        (await app.Ok(new SweepDeadlines())).Reminded.ShouldBe(1);

        app.Clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromSeconds(1));
        (await app.Ok(new SweepDeadlines())).Expired.ShouldBe(1);

        var refill = app.Refill(report.RefillId);
        refill.AssigneeId.ShouldBe(bjorn.Id);
        refill.Status.ShouldBe(RefillStatus.Offered);
        app.Budget(app.Oslo)!.Reserved.ShouldBe(Money.Of(0, "NOK"));

        await app.DeliverOutbox();
        var toAsha = app.Chat.DirectMessagesTo(asha.Id).Select(m => m.Text).ToList();
        toAsha.ShouldContain(t => t.StartsWith("Reminder:", StringComparison.Ordinal));
        toAsha.ShouldContain(t => t.Contains("upload the receipt anyway", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Volunteer_who_bought_after_missing_the_deadline_is_still_reimbursed()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, 1, daysAgo: 20);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, bjorn.Id));
        await app.Ok(new CommitToRefill(report.RefillId, asha.Id));
        app.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        await app.Ok(new SweepDeadlines()); // now offered to Bjorn

        var completed = await app.Complete(report.RefillId, asha, 700);

        completed.IsLate.ShouldBeTrue();
        var refill = app.Refill(report.RefillId);
        refill.Status.ShouldBe(RefillStatus.Completed);
        refill.Attempts.Single(a => a.EmployeeId == bjorn.Id).Outcome.ShouldBe(AttemptOutcome.Superseded);
        app.Budget(app.Oslo)!.Spent.ShouldBe(Money.Of(700, "NOK"));
        app.ClaimFor(report.RefillId).EmployeeId.ShouldBe(asha.Id);
    }

    [Fact]
    public async Task After_three_declines_the_refill_is_broadcast_and_anyone_can_take_it()
    {
        using var app = new TestApp();
        var volunteers = new[] { app.Volunteer("Asha", app.Oslo), app.Volunteer("Bjorn", app.Oslo), app.Volunteer("Kari", app.Oslo) };
        var reporter = app.Colleague("Lars", app.Oslo);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, reporter.Id));
        for (var i = 0; i < 3; i++)
        {
            var assignee = app.Refill(report.RefillId).AssigneeId!.Value;
            await app.Ok(new DeclineRefill(report.RefillId, assignee));
        }

        var refill = app.Refill(report.RefillId);
        refill.Status.ShouldBe(RefillStatus.Escalated);
        refill.Attempts.Select(a => a.EmployeeId).ShouldBe(volunteers.Select(v => v.Id), ignoreOrder: true);

        await app.DeliverOutbox();
        app.Chat.ChannelMessages(app.Oslo.Id).Last().Actions.ShouldHaveSingleItem().Value.ShouldBe($"accept:{report.RefillId}");
        app.Chat.AdminMessages(app.Oslo.Id).ShouldHaveSingleItem().Text.ShouldContain("escalated");

        await app.Ok(new CommitToRefill(report.RefillId, volunteers[1].Id)); // changed their mind
        app.Refill(report.RefillId).Status.ShouldBe(RefillStatus.Committed);
    }

    [Fact]
    public async Task Refill_is_escalated_straight_away_when_nobody_is_available()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo, DayOfWeek.Monday); // not in on Tuesday or Wednesday
        var reporter = app.Colleague("Lars", app.Oslo);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, reporter.Id));

        report.Status.ShouldBe(RefillStatus.Escalated);
        app.Refill(report.RefillId).Attempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Admin_can_cancel_a_committed_refill_and_the_reservation_is_released()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var admin = app.Admin("Ingrid", app.Oslo);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, asha.Id));
        await app.Ok(new CommitToRefill(report.RefillId, asha.Id));

        (await app.Fails(new CancelRefill(report.RefillId, asha.Id, "false alarm"))).Code.ShouldBe("employee.not_office_admin");
        await app.Ok(new CancelRefill(report.RefillId, admin.Id, "false alarm"));

        app.Refill(report.RefillId).Status.ShouldBe(RefillStatus.Cancelled);
        app.Budget(app.Oslo)!.Reserved.ShouldBe(Money.Of(0, "NOK"));
    }

    [Fact]
    public async Task Budget_status_query_reflects_reservations_and_spend()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        await app.RunRefillCycle(app.OsloCounter, asha, 900);

        var status = await app.Ok(new GetBudgetStatus(app.Oslo.Id));

        status.Limit.ShouldBe(Money.Of(6000, "NOK"));
        status.Spent.ShouldBe(Money.Of(900, "NOK"));
        status.Remaining.ShouldBe(Money.Of(5100, "NOK"));
    }

    [Fact]
    public async Task Request_handlers_are_all_resolvable_from_di()
    {
        // ValidateOnBuild in TestApp already proves every registration resolves. This guards the dispatcher path.
        using var app = new TestApp();
        (await app.Send(new SweepDeadlines())).IsSuccess.ShouldBeTrue();
        (await app.Send(new SyncClaimStatuses())).IsSuccess.ShouldBeTrue();
        (await app.Send<ChatMessage>(new Features.Chat.HandleChatEvent("unknown", "/snacks status"))).Value.Text.ShouldContain("don't know you");
    }
}
