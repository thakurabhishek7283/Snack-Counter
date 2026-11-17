using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;
using SnackCounter.Infrastructure.Persistence;
using SnackCounter.Infrastructure.Seed;
using SnackCounter.Integration.Tests.Infrastructure;

namespace SnackCounter.Integration.Tests;

[Collection(PostgresCollection.Name)]
public class PersistenceTests(PostgresFixture postgres)
{
    private static readonly Currency Nok = Currency.Of("NOK");

    [SkippableFact]
    public async Task Migrations_are_applied_and_match_the_model()
    {
        await using var host = await PostgresHost.Create(postgres);

        var (pending, modelChanged) = await host.Query(async db =>
            ((await db.Database.GetPendingMigrationsAsync()).ToList(), db.Database.HasPendingModelChanges()));

        pending.ShouldBeEmpty();
        modelChanged.ShouldBeFalse("The model changed without a migration. Run: dotnet ef migrations add <Name>");
    }

    [SkippableFact]
    public async Task Aggregates_round_trip_with_children_money_and_json()
    {
        await using var host = await PostgresHost.Create(postgres);
        var lars = People.Named("Lars").Id;
        var kari = People.Named("Kari").Id;
        var now = host.Clock.GetUtcNow();

        var refill = Refill.Report(DemoData.OsloKitchen, DemoData.OsloId, Nok, kari, now);
        refill.Offer(lars, now, TimeSpan.FromHours(2), "0 refills in the last 90 days").IsSuccess.ShouldBeTrue();
        refill.Commit(lars, now, TimeSpan.FromDays(1)).IsSuccess.ShouldBeTrue();
        refill.Complete(lars, Money.Of(850.50m, "NOK"), "receipts/1", [new PurchaseItem("Chips", 2, "salty")], now.AddHours(2)).IsSuccess.ShouldBeTrue();

        var budget = BudgetPeriod.Open(DemoData.OsloId, new BudgetMonth(2026, 11), Money.Of(6000, "NOK"));
        budget.RecordSpend(refill.Id, Money.Of(850.50m, "NOK"), new SoftCapBudgetPolicy(), now).IsSuccess.ShouldBeTrue();
        var claim = Claim.Create(refill.Id, lars, DemoData.OsloId, Money.Of(850.50m, "NOK"), "Snack refill", "receipts/1", now);

        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SnackDbContext>();
            db.AddRange(refill, budget, claim);
            await db.SaveChangesAsync();
        }

        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SnackDbContext>();
            var loaded = await db.Refills.SingleAsync(r => r.Id == refill.Id);
            loaded.Status.ShouldBe(RefillStatus.Completed);
            loaded.OfficeCurrency.ShouldBe(Nok);
            loaded.Attempts.ShouldHaveSingleItem().Outcome.ShouldBe(AttemptOutcome.Accepted);
            loaded.Attempts.Single().Explanation.ShouldBe("0 refills in the last 90 days");
            loaded.Purchase!.Amount.ShouldBe(Money.Of(850.50m, "NOK"));
            loaded.Purchase.Items.ShouldHaveSingleItem().ShouldBe(new PurchaseItem("Chips", 2, "salty"));

            var loadedBudget = await db.Budgets.SingleAsync(b => b.Id == budget.Id);
            loadedBudget.Month.ShouldBe(new BudgetMonth(2026, 11));
            loadedBudget.Spent.ShouldBe(Money.Of(850.50m, "NOK"));
            loadedBudget.Remaining.ShouldBe(Money.Of(5149.50m, "NOK"));

            (await db.Claims.SingleAsync(c => c.Id == claim.Id)).Amount.ShouldBe(Money.Of(850.50m, "NOK"));

            var office = await db.Offices.SingleAsync(o => o.Id == DemoData.OsloId);
            office.TimeZone.Id.ShouldBe("Europe/Oslo");
            office.TypicalMaxPurchase.ShouldBe(Money.Of(1200, "NOK"));
            (await db.Employees.SingleAsync(e => e.Id == lars)).DefaultOfficeDays.ShouldNotBeEmpty();
        }
    }

    [SkippableFact]
    public async Task Outbox_payloads_survive_the_database()
    {
        await using var host = await PostgresHost.Create(postgres);
        var lars = People.Named("Lars").Id;
        var claimId = ClaimId.New();

        await using (var scope = host.Scope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            outbox.Enqueue(new SendChatMessage(new ChatAudience.Direct(lars), new ChatMessage("Your turn", [new ChatAction("I'll buy it", "accept:x")])));
            outbox.Enqueue(new SubmitClaimToHr(claimId));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default);
        }

        var payloads = await host.Query(async db => (await db.Outbox.OrderBy(m => m.OccurredAt).ToListAsync()).Select(m => m.Payload).ToList());

        var chat = payloads.OfType<SendChatMessage>().ShouldHaveSingleItem();
        chat.Audience.ShouldBe(new ChatAudience.Direct(lars));
        chat.Message.Text.ShouldBe("Your turn");
        chat.Message.Actions.ShouldHaveSingleItem().ShouldBe(new ChatAction("I'll buy it", "accept:x"));
        payloads.OfType<SubmitClaimToHr>().ShouldHaveSingleItem().ClaimId.ShouldBe(claimId);
    }

    // Rule: no duplicate refills, enforced by PostgreSQL itself (partial unique index).
    [SkippableFact]
    public async Task Database_rejects_a_second_active_refill_for_the_same_box()
    {
        await using var host = await PostgresHost.Create(postgres);
        var reporter = People.Named("Lars").Id;
        var now = host.Clock.GetUtcNow();

        await using var first = host.Scope();
        await using var second = host.Scope();
        first.ServiceProvider.GetRequiredService<IRefillRepository>().Add(Refill.Report(DemoData.OsloKitchen, DemoData.OsloId, Nok, reporter, now));
        second.ServiceProvider.GetRequiredService<IRefillRepository>().Add(Refill.Report(DemoData.OsloKitchen, DemoData.OsloId, Nok, reporter, now));

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default);
        await Should.ThrowAsync<DuplicateActiveRefillException>(() =>
            second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default));
    }

    // Rule: two people cannot both commit, at the row-version level.
    [SkippableFact]
    public async Task Concurrent_commits_on_the_same_refill_are_detected()
    {
        await using var host = await PostgresHost.Create(postgres);
        var lars = People.Named("Lars").Id;
        var kari = People.Named("Kari").Id;
        var now = host.Clock.GetUtcNow();

        var refill = Refill.Report(DemoData.OsloKitchen, DemoData.OsloId, Nok, lars, now);
        refill.Escalate(now);
        await using (var setup = host.Scope())
        {
            setup.ServiceProvider.GetRequiredService<IRefillRepository>().Add(refill);
            await setup.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default);
        }

        await using var a = host.Scope();
        await using var b = host.Scope();
        var seenByA = (await a.ServiceProvider.GetRequiredService<IRefillRepository>().Get(refill.Id, default))!;
        var seenByB = (await b.ServiceProvider.GetRequiredService<IRefillRepository>().Get(refill.Id, default))!;

        seenByA.Commit(lars, now, TimeSpan.FromDays(1)).IsSuccess.ShouldBeTrue();
        seenByB.Commit(kari, now, TimeSpan.FromDays(1)).IsSuccess.ShouldBeTrue(); // both saw "Escalated"

        await a.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default);
        await Should.ThrowAsync<ConcurrencyConflictException>(() => b.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default));

        (await host.Query(db => db.Refills.SingleAsync(r => r.Id == refill.Id))).AssigneeId.ShouldBe(lars);
    }

    // Only a child row (ledger entry) changes here. The aggregate root must still be version-checked.
    [SkippableFact]
    public async Task Concurrent_reservations_on_the_same_budget_are_detected()
    {
        await using var host = await PostgresHost.Create(postgres);
        var month = new BudgetMonth(2026, 11);
        var nok = (decimal amount) => Money.Of(amount, "NOK");

        await using var a = host.Scope();
        await using var b = host.Scope();
        var budgetA = await a.ServiceProvider.GetRequiredService<IBudgetRepository>().GetOrOpen(DemoData.OsloId, month, nok(6000), default);
        var budgetB = await b.ServiceProvider.GetRequiredService<IBudgetRepository>().GetOrOpen(DemoData.OsloId, month, nok(6000), default);

        budgetA.ReserveForRefill(RefillId.New(), nok(300), nok(1200), host.Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();
        budgetB.ReserveForRefill(RefillId.New(), nok(300), nok(1200), host.Clock.GetUtcNow()).IsSuccess.ShouldBeTrue();

        await a.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default);
        await Should.ThrowAsync<ConcurrencyConflictException>(() => b.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChanges(default));
    }

    [SkippableFact]
    public async Task Opening_a_budget_month_concurrently_creates_exactly_one_period()
    {
        await using var host = await PostgresHost.Create(postgres);
        var month = new BudgetMonth(2026, 12);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = host.Scope();
            await scope.ServiceProvider.GetRequiredService<IBudgetRepository>()
                .GetOrOpen(DemoData.OsloId, month, Money.Of(6000, "NOK"), default);
        }));

        (await host.Query(db => db.Budgets.CountAsync(b => b.OfficeId == DemoData.OsloId && b.Month == month))).ShouldBe(1);
    }
}
