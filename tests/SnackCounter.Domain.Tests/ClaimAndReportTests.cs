using Shouldly;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Reimbursement;
using SnackCounter.Domain.Reporting;
using static SnackCounter.Domain.Tests.TestData;

namespace SnackCounter.Domain.Tests;

public class ClaimTests
{
    private static readonly TimeSpan TenDays = TimeSpan.FromDays(10);

    private static Claim NewClaim() =>
        Claim.Create(RefillId.New(), EmployeeId.New(), Oslo.Id, NOK(850), "Snack refill Oslo", "receipt-1", Now);

    private static Claim Submitted()
    {
        var claim = NewClaim();
        claim.MarkSubmitted("hr-1", Now).ShouldSucceed();
        return claim;
    }

    [Fact]
    public void New_claim_is_pending_and_ready_for_submission()
    {
        var claim = NewClaim();

        claim.Status.ShouldBe(ClaimStatus.Pending);
        claim.IsUnpaid.ShouldBeTrue();
        claim.DomainEvents.OfType<ClaimReadyForSubmission>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Moves_from_submitted_to_approved_to_paid()
    {
        var claim = Submitted();

        claim.ApplyHrStatus(ClaimStatus.Approved, Now.AddDays(3)).ShouldSucceed();
        claim.ApplyHrStatus(ClaimStatus.Paid, Now.AddDays(5)).ShouldSucceed();

        claim.Status.ShouldBe(ClaimStatus.Paid);
        claim.IsUnpaid.ShouldBeFalse();
        claim.PaidAt.ShouldBe(Now.AddDays(5));
    }

    [Fact]
    public void Applying_the_same_hr_status_twice_is_a_no_op()
    {
        var claim = Submitted();
        claim.ApplyHrStatus(ClaimStatus.Approved, Now).ShouldSucceed();
        var events = claim.DomainEvents.Count;

        claim.ApplyHrStatus(ClaimStatus.Approved, Now.AddHours(1)).ShouldSucceed();

        claim.DomainEvents.Count.ShouldBe(events);
    }

    [Fact]
    public void A_paid_claim_cannot_go_back_to_approved()
    {
        var claim = Submitted();
        claim.ApplyHrStatus(ClaimStatus.Paid, Now).ShouldSucceed();

        claim.ApplyHrStatus(ClaimStatus.Approved, Now).ShouldFailWith("claim.invalid_transition");
    }

    [Fact]
    public void Claim_is_overdue_only_after_ten_days_and_flagged_once()
    {
        var claim = Submitted();

        claim.FlagIfOverdue(Now.AddDays(9), TenDays).ShouldBeFalse();
        claim.FlagIfOverdue(Now.AddDays(10).AddSeconds(1), TenDays).ShouldBeTrue();
        claim.FlagIfOverdue(Now.AddDays(11), TenDays).ShouldBeFalse();
        claim.DomainEvents.OfType<ClaimOverdue>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Resubmitting_after_rejection_uses_a_new_idempotency_key()
    {
        var claim = Submitted();
        var firstKey = claim.IdempotencyKey;
        claim.ApplyHrStatus(ClaimStatus.Rejected, Now, "Receipt unreadable").ShouldSucceed();
        claim.RejectionReason.ShouldBe("Receipt unreadable");

        claim.Resubmit("receipt-2", Now).ShouldSucceed();

        claim.Status.ShouldBe(ClaimStatus.Pending);
        claim.IdempotencyKey.ShouldNotBe(firstKey);
        claim.HrClaimId.ShouldBeNull();
    }
}

public class MonthlyReportCalculatorTests
{
    private static readonly BudgetMonth November = new(2026, 11);

    // Illustrative rate for tests only, not a real quote.
    private static readonly Dictionary<Currency, decimal> Rates = new() { [Inr] = 0.125m };

    private static OfficeMonthFigures OsloFigures(decimal spent, int refills = 6) =>
        new(Oslo.Id, "Oslo", NOK(6000), NOK(spent), refills, 0, TimeSpan.FromHours(20));

    private static OfficeMonthFigures BengaluruFigures(decimal spent, int refills = 8) =>
        new(Bengaluru.Id, "Bengaluru", INR(15000), INR(spent), refills, 0, null);

    // Rule: budgets and totals are correct across both currencies.
    [Fact]
    public void Totals_each_office_in_its_own_currency_then_converts_once()
    {
        var report = MonthlyReportCalculator
            .Build(November, Nok, [OsloFigures(5400), BengaluruFigures(13200)], Rates)
            .ShouldSucceed();

        report.Offices.Single(o => o.OfficeName == "Oslo").Spent.ShouldBe(NOK(5400));
        report.Offices.Single(o => o.OfficeName == "Bengaluru").Spent.ShouldBe(INR(13200));
        report.Offices.Single(o => o.OfficeName == "Bengaluru").SpentInReportingCurrency.ShouldBe(NOK(1650));
        report.Total.ShouldBe(NOK(7050));
        report.RatesUsed.ShouldContainKeyAndValue("INR->NOK", 0.125m);
    }

    [Fact]
    public void Full_budgets_in_both_offices_add_up()
    {
        var report = MonthlyReportCalculator
            .Build(November, Nok, [OsloFigures(6000), BengaluruFigures(15000)], Rates)
            .ShouldSucceed();

        report.Total.ShouldBe(NOK(7875));
        report.Offices.ShouldAllBe(o => o.PercentUsed == 100m);
    }

    [Fact]
    public void Remaining_percent_and_average_are_per_office()
    {
        var oslo = MonthlyReportCalculator.Build(November, Nok, [OsloFigures(4500, refills: 6)], Rates)
            .ShouldSucceed().Offices.Single();

        oslo.Remaining.ShouldBe(NOK(1500));
        oslo.PercentUsed.ShouldBe(75m);
        oslo.AveragePurchase.ShouldBe(NOK(750));
    }

    [Fact]
    public void Missing_fx_rate_is_an_error_not_a_silent_zero() =>
        MonthlyReportCalculator.Build(November, Nok, [BengaluruFigures(1000)], new Dictionary<Currency, decimal>())
            .ShouldFailWith("report.missing_fx_rate");
}
