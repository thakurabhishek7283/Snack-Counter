using Shouldly;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using static SnackCounter.Domain.Tests.TestData;

namespace SnackCounter.Domain.Tests;

public class BudgetPeriodTests
{
    private static readonly BudgetMonth November = new(2026, 11);
    private static readonly IBudgetPolicy SoftCap = new SoftCapBudgetPolicy();

    private static BudgetPeriod OsloNovember() => BudgetPeriod.Open(Oslo.Id, November, NOK(6000));

    private static Result<Money> Reserve(BudgetPeriod budget, RefillId refill) =>
        budget.ReserveForRefill(refill, Oslo.TypicalMinPurchase, Oslo.TypicalMaxPurchase, Now);

    [Fact]
    public void Remaining_is_limit_minus_spent_minus_open_reservations()
    {
        var budget = OsloNovember();
        Reserve(budget, RefillId.New()).ShouldSucceed();
        budget.RecordSpend(RefillId.New(), NOK(850), SoftCap, Now).ShouldSucceed();

        budget.Reserved.ShouldBe(NOK(1200));
        budget.Spent.ShouldBe(NOK(850));
        budget.Remaining.ShouldBe(NOK(3950));
    }

    [Fact]
    public void Reservation_is_the_typical_maximum_when_there_is_room() =>
        Reserve(OsloNovember(), RefillId.New()).ShouldSucceed().ShouldBe(NOK(1200));

    [Fact]
    public void Reservation_is_capped_at_what_is_left()
    {
        var budget = OsloNovember();
        budget.RecordSpend(RefillId.New(), NOK(5500), SoftCap, Now).ShouldSucceed();

        Reserve(budget, RefillId.New()).ShouldSucceed().ShouldBe(NOK(500));
    }

    [Fact]
    public void Reserving_fails_when_less_than_the_minimum_purchase_is_left()
    {
        var budget = OsloNovember();
        budget.RecordSpend(RefillId.New(), NOK(5800), SoftCap, Now).ShouldSucceed();

        Reserve(budget, RefillId.New()).ShouldFailWith("budget.exhausted");
        budget.Reserved.ShouldBe(NOK(0));
    }

    [Fact]
    public void Reserving_twice_for_the_same_refill_is_idempotent()
    {
        var budget = OsloNovember();
        var refill = RefillId.New();

        Reserve(budget, refill).ShouldSucceed();
        Reserve(budget, refill).ShouldSucceed();

        budget.Reserved.ShouldBe(NOK(1200));
    }

    [Fact]
    public void Recording_the_spend_replaces_the_reservation()
    {
        var budget = OsloNovember();
        var refill = RefillId.New();
        Reserve(budget, refill).ShouldSucceed();

        var outcome = budget.RecordSpend(refill, NOK(850), SoftCap, Now).ShouldSucceed();

        outcome.OverBudget.ShouldBeFalse();
        budget.Reserved.ShouldBe(NOK(0));
        budget.Spent.ShouldBe(NOK(850));
        budget.Remaining.ShouldBe(NOK(5150));
    }

    [Fact]
    public void Spending_up_to_the_reservation_on_a_nearly_empty_budget_is_not_over_budget()
    {
        var budget = OsloNovember();
        budget.RecordSpend(RefillId.New(), NOK(5500), SoftCap, Now).ShouldSucceed();
        var refill = RefillId.New();
        Reserve(budget, refill).ShouldSucceed().ShouldBe(NOK(500));

        budget.RecordSpend(refill, NOK(500), SoftCap, Now).ShouldSucceed().OverBudget.ShouldBeFalse();
        budget.Remaining.ShouldBe(NOK(0));
    }

    [Fact]
    public void Soft_cap_records_an_overspend_and_flags_it()
    {
        var budget = OsloNovember();
        budget.RecordSpend(RefillId.New(), NOK(5800), SoftCap, Now).ShouldSucceed();

        var outcome = budget.RecordSpend(RefillId.New(), NOK(600), SoftCap, Now).ShouldSucceed();

        outcome.OverBudget.ShouldBeTrue();
        budget.Remaining.ShouldBe(NOK(-400));
        budget.OverBudgetCount.ShouldBe(1);
        budget.DomainEvents.OfType<OverBudgetSpendRecorded>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Hard_cap_rejects_an_overspend_and_changes_nothing()
    {
        var budget = OsloNovember();
        var refill = RefillId.New();
        Reserve(budget, refill).ShouldSucceed();

        budget.RecordSpend(refill, NOK(6500), new HardCapBudgetPolicy(), Now).ShouldFailWith("budget.spend_rejected");

        budget.Reserved.ShouldBe(NOK(1200));
        budget.Spent.ShouldBe(NOK(0));
    }

    [Fact]
    public void Recording_the_same_spend_twice_is_idempotent()
    {
        var budget = OsloNovember();
        var refill = RefillId.New();

        budget.RecordSpend(refill, NOK(850), SoftCap, Now).ShouldSucceed();
        budget.RecordSpend(refill, NOK(850), SoftCap, Now).ShouldSucceed().AlreadyRecorded.ShouldBeTrue();

        budget.Spent.ShouldBe(NOK(850));
    }

    [Fact]
    public void A_budget_only_accepts_its_own_currency() =>
        OsloNovember().RecordSpend(RefillId.New(), INR(850), SoftCap, Now).ShouldFailWith("budget.currency_mismatch");

    [Fact]
    public void Releasing_is_idempotent()
    {
        var budget = OsloNovember();
        var refill = RefillId.New();
        Reserve(budget, refill).ShouldSucceed();

        budget.Release(refill, Now).ShouldBeTrue();
        budget.Release(refill, Now).ShouldBeFalse();
        budget.Remaining.ShouldBe(NOK(6000));
    }

    [Fact]
    public void Raising_the_limit_increases_what_is_left()
    {
        var budget = OsloNovember();
        budget.RecordSpend(RefillId.New(), NOK(5800), SoftCap, Now).ShouldSucceed();

        budget.SetLimit(NOK(7000), Now).ShouldSucceed();

        budget.Remaining.ShouldBe(NOK(1200));
        budget.DomainEvents.OfType<BudgetLimitChanged>().ShouldHaveSingleItem();
    }
}
