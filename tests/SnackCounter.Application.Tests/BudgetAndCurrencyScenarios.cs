using Shouldly;
using SnackCounter.Application.Features.Budget;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Application.Features.Reports;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Tests;

public class BudgetAndCurrencyScenarios
{
    [Fact]
    public async Task Refill_goes_on_hold_when_the_budget_is_used_up_and_resumes_when_it_is_raised()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var admin = app.Admin("Ingrid", app.Oslo);
        var november = app.Oslo.BudgetMonthOf(app.Clock.GetUtcNow());
        await app.Ok(new SetMonthlyBudget(app.Oslo.Id, november, 200, admin.Id)); // below the 300 NOK minimum purchase

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, asha.Id));

        report.Status.ShouldBe(RefillStatus.OnHold);
        await app.DeliverOutbox();
        app.Chat.AdminMessages(app.Oslo.Id).ShouldHaveSingleItem().Text.ShouldContain("on hold");

        await app.Ok(new SetMonthlyBudget(app.Oslo.Id, november, 6000, admin.Id));

        var refill = app.Refill(report.RefillId);
        refill.Status.ShouldBe(RefillStatus.Offered);
        refill.AssigneeId.ShouldBe(asha.Id);
    }

    [Fact]
    public async Task Only_an_admin_of_that_office_can_change_its_budget()
    {
        using var app = new TestApp();
        var bengaluruAdmin = app.Admin("Ravi", app.Bengaluru);
        var month = new BudgetMonth(2026, 11);

        (await app.Fails(new SetMonthlyBudget(app.Oslo.Id, month, 7000, bengaluruAdmin.Id))).Code.ShouldBe("employee.not_office_admin");
    }

    [Fact]
    public async Task Overspend_is_recorded_reimbursed_and_flagged_to_the_admin()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var admin = app.Admin("Ingrid", app.Oslo);
        await app.Ok(new SetMonthlyBudget(app.Oslo.Id, new BudgetMonth(2026, 11), 1000, admin.Id));

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, asha.Id));
        var commit = await app.Ok(new CommitToRefill(report.RefillId, asha.Id));
        commit.SpendUpTo.ShouldBe(Money.Of(1000, "NOK")); // told the cap up front

        var completed = await app.Complete(report.RefillId, asha, 1100);

        completed.OverBudget.ShouldBeTrue();
        app.Budget(app.Oslo)!.Remaining.ShouldBe(Money.Of(-100, "NOK"));
        app.ClaimFor(report.RefillId).Amount.ShouldBe(Money.Of(1100, "NOK"));
        await app.DeliverOutbox();
        app.Chat.AdminMessages(app.Oslo.Id).ShouldContain(m => m.Text.Contains("over the limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Visitor_from_Bengaluru_buys_in_Oslo_charged_to_Oslo_in_NOK()
    {
        using var app = new TestApp();
        var priya = app.Volunteer("Priya", app.Bengaluru);
        var ola = app.Volunteer("Ola", app.Oslo, DayOfWeek.Monday); // not in this week
        var reporter = app.Colleague("Lars", app.Oslo);

        (await app.ChatAs(priya, "/snacks visiting Oslo 2026-11-16 2026-11-27")).ShouldStartWith("Got it");

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, reporter.Id));
        report.AssigneeId.ShouldBe(priya.Id);
        await app.Ok(new CommitToRefill(report.RefillId, priya.Id));

        (await app.Fails(new CompleteRefill(report.RefillId, priya.Id, 5000, "INR", [1], "image/jpeg")))
            .Code.ShouldBe("refill.wrong_currency");
        await app.Complete(report.RefillId, priya, 640);

        app.Budget(app.Oslo)!.Spent.ShouldBe(Money.Of(640, "NOK"));
        app.Budget(app.Bengaluru).ShouldBeNull(); // Bengaluru's budget is untouched
        app.ClaimFor(report.RefillId).Amount.ShouldBe(Money.Of(640, "NOK")); // HR converts to her INR payroll

        // While she is in Oslo, she is not asked to refill Bengaluru.
        var bengaluruReport = await app.Ok(new ReportEmpty(app.BengaluruCounter.Id, priya.Id));
        bengaluruReport.AssigneeId.ShouldNotBe(priya.Id);
        bengaluruReport.Status.ShouldBe(RefillStatus.Escalated);
    }

    [Fact]
    public async Task Declaring_a_holiday_in_chat_removes_you_from_the_rota()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var bjorn = app.Volunteer("Bjorn", app.Oslo);
        app.SeedPastRefills(bjorn, app.Oslo, 4, daysAgo: 3); // Bjorn would normally be last in line

        await app.ChatAs(asha, "/snacks away 2026-11-16 2026-11-20");
        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, bjorn.Id));

        report.AssigneeId.ShouldBe(bjorn.Id);
    }

    [Fact]
    public async Task Purchase_after_midnight_on_the_first_counts_toward_the_new_month()
    {
        // Monday 30 Nov, 22:00 in Oslo.
        using var app = new TestApp(start: new DateTimeOffset(2026, 11, 30, 21, 0, 0, TimeSpan.Zero));
        var asha = app.Volunteer("Asha", app.Oslo);

        var report = await app.Ok(new ReportEmpty(app.OsloCounter.Id, asha.Id));
        await app.Ok(new CommitToRefill(report.RefillId, asha.Id));
        var november = new BudgetMonth(2026, 11);
        var december = new BudgetMonth(2026, 12);
        app.Budget(app.Oslo, november)!.Reserved.ShouldBe(Money.Of(1200, "NOK"));

        app.Clock.Advance(TimeSpan.FromHours(3)); // 01:00 on 1 Dec in Oslo
        await app.Complete(report.RefillId, asha, 900);

        app.Budget(app.Oslo, november)!.Reserved.ShouldBe(Money.Of(0, "NOK"));
        app.Budget(app.Oslo, november)!.Spent.ShouldBe(Money.Of(0, "NOK"));
        app.Budget(app.Oslo, december)!.Spent.ShouldBe(Money.Of(900, "NOK"));
    }

    // Rule: budgets and totals are correct across both currencies.
    [Fact]
    public async Task Monthly_report_totals_both_offices_in_one_currency()
    {
        using var app = new TestApp();
        var asha = app.Volunteer("Asha", app.Oslo);
        var priya = app.Volunteer("Priya", app.Bengaluru);

        await app.RunRefillCycle(app.OsloCounter, asha, 850);
        await app.RunRefillCycle(app.OsloCounter, asha, 1150);
        await app.RunRefillCycle(app.BengaluruCounter, priya, 2400);
        await app.RunRefillCycle(app.BengaluruCounter, priya, 1600);

        var report = await app.Ok(new GetMonthlyReport(new BudgetMonth(2026, 11)));

        var oslo = report.Offices.Single(o => o.OfficeName == "Oslo");
        var bengaluru = report.Offices.Single(o => o.OfficeName == "Bengaluru");
        oslo.Spent.ShouldBe(Money.Of(2000, "NOK"));
        oslo.Remaining.ShouldBe(Money.Of(4000, "NOK"));
        oslo.RefillCount.ShouldBe(2);
        bengaluru.Spent.ShouldBe(Money.Of(4000, "INR"));
        bengaluru.Remaining.ShouldBe(Money.Of(11000, "INR"));
        bengaluru.SpentInReportingCurrency.ShouldBe(Money.Of(500, "NOK")); // 4000 x 0.125
        report.Total.ShouldBe(Money.Of(2500, "NOK"));
        report.RatesUsed["INR->NOK"].ShouldBe(TestApp.InrToNok);

        var inInr = await app.Ok(new GetMonthlyReport(new BudgetMonth(2026, 11), "INR"));
        inInr.Total.ShouldBe(Money.Of(20000, "INR")); // 2000 NOK x 8 + 4000 INR
    }
}
