using Shouldly;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;
using static SnackCounter.Domain.Tests.TestData;

namespace SnackCounter.Domain.Tests;

public class RefillLifecycleTests
{
    private static readonly EmployeeId Reporter = EmployeeId.New();
    private static readonly EmployeeId Asha = EmployeeId.New();
    private static readonly EmployeeId Bjorn = EmployeeId.New();
    private static readonly TimeSpan OfferTtl = TimeSpan.FromHours(2);
    private static readonly TimeSpan PurchaseWindow = TimeSpan.FromDays(1);

    private static Refill NewRefill() => Refill.Report(OsloCounter, Oslo.Id, Nok, Reporter, Now);

    private static Refill OfferedTo(EmployeeId employee)
    {
        var refill = NewRefill();
        refill.Offer(employee, Now, OfferTtl).ShouldSucceed();
        return refill;
    }

    private static Refill CommittedBy(EmployeeId employee)
    {
        var refill = OfferedTo(employee);
        refill.Commit(employee, Now, PurchaseWindow).ShouldSucceed();
        return refill;
    }

    [Fact]
    public void Reporting_starts_an_open_refill()
    {
        var refill = NewRefill();

        refill.Status.ShouldBe(RefillStatus.Open);
        refill.IsActive.ShouldBeTrue();
        refill.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RefillReported>();
    }

    [Fact]
    public void Happy_path_offer_commit_complete()
    {
        var refill = CommittedBy(Asha);

        refill.Complete(Asha, NOK(850), "receipt-1", null, Now.AddHours(3)).ShouldSucceed();

        refill.Status.ShouldBe(RefillStatus.Completed);
        refill.Purchase!.Amount.ShouldBe(NOK(850));
        refill.Purchase.IsLate.ShouldBeFalse();
        refill.Attempts.ShouldHaveSingleItem().Outcome.ShouldBe(AttemptOutcome.Accepted);
        refill.DomainEvents.OfType<RefillCompleted>().ShouldHaveSingleItem();
    }

    // Rule: two people cannot both commit to the same refill.
    [Fact]
    public void Second_person_cannot_commit_once_someone_has_committed()
    {
        var refill = CommittedBy(Asha);

        refill.Commit(Bjorn, Now, PurchaseWindow).ShouldFailWith("refill.already_taken");
        refill.AssigneeId.ShouldBe(Asha);
    }

    [Fact]
    public void Committing_twice_is_idempotent_for_the_same_person()
    {
        var refill = CommittedBy(Asha);
        var events = refill.DomainEvents.Count;

        refill.Commit(Asha, Now.AddMinutes(1), PurchaseWindow).ShouldSucceed();

        refill.DomainEvents.Count.ShouldBe(events);
    }

    [Fact]
    public void Only_the_offered_person_can_commit_a_directed_offer() =>
        OfferedTo(Asha).Commit(Bjorn, Now, PurchaseWindow).ShouldFailWith("refill.not_offered_to_you");

    [Fact]
    public void Escalated_refill_goes_to_the_first_committer()
    {
        var refill = NewRefill();
        refill.Escalate(Now).ShouldSucceed();

        refill.Commit(Bjorn, Now, PurchaseWindow).ShouldSucceed();
        refill.Commit(Asha, Now, PurchaseWindow).ShouldFailWith("refill.already_taken");

        refill.AssigneeId.ShouldBe(Bjorn);
        refill.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(AttemptKind.Broadcast);
    }

    [Fact]
    public void Declining_reopens_the_refill()
    {
        var refill = OfferedTo(Asha);

        refill.Decline(Asha, Now.AddMinutes(5)).ShouldSucceed();

        refill.Status.ShouldBe(RefillStatus.Open);
        refill.AssigneeId.ShouldBeNull();
        refill.Attempts.Single().Outcome.ShouldBe(AttemptOutcome.Declined);
    }

    [Fact]
    public void Offer_cannot_expire_before_its_deadline() =>
        OfferedTo(Asha).ExpireDeadline(Now.AddHours(1)).ShouldFailWith("refill.deadline_not_due");

    // Rule: a refill nobody completes is handed on (domain half; the sweeper scenario test covers the rest).
    [Fact]
    public void Unanswered_offer_expires_and_reopens()
    {
        var refill = OfferedTo(Asha);

        refill.ExpireDeadline(Now + OfferTtl).ShouldSucceed();

        refill.Status.ShouldBe(RefillStatus.Open);
        refill.Attempts.Single().Outcome.ShouldBe(AttemptOutcome.OfferExpired);
        refill.DomainEvents.OfType<OfferExpired>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Missed_purchase_deadline_reopens_and_records_it()
    {
        var refill = CommittedBy(Asha);

        refill.ExpireDeadline(Now + PurchaseWindow).ShouldSucceed();

        refill.Status.ShouldBe(RefillStatus.Open);
        refill.Attempts.Single().Outcome.ShouldBe(AttemptOutcome.PurchaseDeadlineMissed);
        refill.DomainEvents.OfType<PurchaseDeadlineMissed>().ShouldHaveSingleItem().PreviousAssignee.ShouldBe(Asha);
    }

    [Fact]
    public void The_same_person_is_never_offered_the_same_refill_twice()
    {
        var refill = OfferedTo(Asha);
        refill.Decline(Asha, Now).ShouldSucceed();

        refill.Offer(Asha, Now, OfferTtl).ShouldFailWith("refill.already_attempted");
    }

    [Fact]
    public void Previous_assignee_who_bought_late_can_still_complete()
    {
        var refill = CommittedBy(Asha);
        refill.ExpireDeadline(Now + PurchaseWindow).ShouldSucceed();
        refill.Offer(Bjorn, Now + PurchaseWindow, OfferTtl).ShouldSucceed();

        refill.Complete(Asha, NOK(700), "receipt-late", null, Now + PurchaseWindow + TimeSpan.FromMinutes(10)).ShouldSucceed();

        refill.Status.ShouldBe(RefillStatus.Completed);
        refill.Purchase!.IsLate.ShouldBeTrue();
        refill.Attempts.Single(a => a.EmployeeId == Bjorn).Outcome.ShouldBe(AttemptOutcome.Superseded);
    }

    [Fact]
    public void Late_completion_is_refused_once_someone_else_has_committed()
    {
        var refill = CommittedBy(Asha);
        refill.ExpireDeadline(Now + PurchaseWindow).ShouldSucceed();
        refill.Offer(Bjorn, Now + PurchaseWindow, OfferTtl).ShouldSucceed();
        refill.Commit(Bjorn, Now + PurchaseWindow, PurchaseWindow).ShouldSucceed();

        refill.Complete(Asha, NOK(700), "receipt-late", null, Now + PurchaseWindow).ShouldFailWith("refill.not_assignee");
    }

    [Fact]
    public void Someone_who_never_had_a_turn_cannot_complete() =>
        CommittedBy(Asha).Complete(Bjorn, NOK(500), "r", null, Now).ShouldFailWith("refill.not_assignee");

    [Fact]
    public void Purchase_must_be_in_the_office_currency() =>
        CommittedBy(Asha).Complete(Asha, INR(500), "r", null, Now).ShouldFailWith("refill.wrong_currency");

    [Fact]
    public void Purchase_amount_must_be_positive() =>
        CommittedBy(Asha).Complete(Asha, NOK(0), "r", null, Now).ShouldFailWith("refill.amount_not_positive");

    [Fact]
    public void Reminder_is_sent_once_after_half_the_purchase_window()
    {
        var refill = CommittedBy(Asha);

        refill.TryRemind(Now.AddHours(11), 0.5).ShouldBeFalse();
        refill.TryRemind(Now.AddHours(12), 0.5).ShouldBeTrue();
        refill.TryRemind(Now.AddHours(13), 0.5).ShouldBeFalse();
        refill.DomainEvents.OfType<PurchaseReminderDue>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Hold_and_resume_when_budget_runs_out()
    {
        var refill = OfferedTo(Asha);

        refill.PutOnHold(Now).ShouldSucceed();
        refill.Status.ShouldBe(RefillStatus.OnHold);
        refill.Attempts.Single().Outcome.ShouldBe(AttemptOutcome.Superseded);

        refill.Resume(Now).ShouldSucceed();
        refill.Status.ShouldBe(RefillStatus.Open);
    }

    [Fact]
    public void Completed_refill_cannot_be_cancelled()
    {
        var refill = CommittedBy(Asha);
        refill.Complete(Asha, NOK(500), "r", null, Now).ShouldSucceed();

        refill.Cancel("duplicate", Now).ShouldFailWith("refill.invalid_transition");
    }

    [Fact]
    public void Transition_table_has_no_way_out_of_terminal_states_and_every_active_state_can_be_cancelled()
    {
        RefillTransitions.All.ShouldNotContain(t => t.From == RefillStatus.Completed || t.From == RefillStatus.Cancelled);

        foreach (var status in RefillTransitions.ActiveStatuses)
            RefillTransitions.CanFire(status, RefillTrigger.Cancel).ShouldBeTrue($"{status} should be cancellable");
    }
}
