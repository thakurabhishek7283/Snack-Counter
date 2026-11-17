namespace SnackCounter.Domain.Assignment.Rules;

public sealed class MustBeVolunteer : IEligibilityRule
{
    public string Name => nameof(MustBeVolunteer);

    public EligibilityVerdict Evaluate(Candidate candidate, AssignmentContext context) =>
        candidate.IsVolunteer ? EligibilityVerdict.Eligible : EligibilityVerdict.Ineligible("not a volunteer");
}

/// <summary>Home-office volunteers, plus visitors from another office if the counter's office allows it.</summary>
public sealed class MustServeCounterOffice : IEligibilityRule
{
    public string Name => nameof(MustServeCounterOffice);

    public EligibilityVerdict Evaluate(Candidate candidate, AssignmentContext context)
    {
        if (candidate.HomeOfficeId == context.CounterOfficeId) return EligibilityVerdict.Eligible;

        var isVisiting = candidate.PresenceToday.IsAt(context.CounterOfficeId) ||
                         candidate.PresenceNextWorkingDay.IsAt(context.CounterOfficeId);

        if (!isVisiting) return EligibilityVerdict.Ineligible("belongs to another office");
        return context.OfficeAllowsVisitors
            ? EligibilityVerdict.Eligible
            : EligibilityVerdict.Ineligible("visitors cannot volunteer at this office");
    }
}

/// <summary>At the counter's office today or the next working day. Excludes holiday, remote, and away at another office.</summary>
public sealed class MustBePresentSoon : IEligibilityRule
{
    public string Name => nameof(MustBePresentSoon);

    public EligibilityVerdict Evaluate(Candidate candidate, AssignmentContext context) =>
        candidate.PresenceToday.IsAt(context.CounterOfficeId) || candidate.PresenceNextWorkingDay.IsAt(context.CounterOfficeId)
            ? EligibilityVerdict.Eligible
            : EligibilityVerdict.Ineligible($"{candidate.PresenceToday.Describe()} today and not at this office next working day");
}

/// <summary>Someone who declined, timed out, or missed the deadline is never asked again for the same refill.</summary>
public sealed class MustNotHaveBeenAttempted : IEligibilityRule
{
    public string Name => nameof(MustNotHaveBeenAttempted);

    public EligibilityVerdict Evaluate(Candidate candidate, AssignmentContext context) =>
        context.AlreadyAttempted.Contains(candidate.Id)
            ? EligibilityVerdict.Ineligible("already had a turn at this refill")
            : EligibilityVerdict.Eligible;
}

/// <summary>
/// Fairness: nobody should carry too much of the company's money. Uses a count rather than an amount
/// so there's no need to compare NOK with INR.
/// </summary>
public sealed class UnpaidClaimsBelowCap : IEligibilityRule
{
    public string Name => nameof(UnpaidClaimsBelowCap);

    public EligibilityVerdict Evaluate(Candidate candidate, AssignmentContext context) =>
        candidate.UnpaidClaims < context.UnpaidClaimsCap
            ? EligibilityVerdict.Eligible
            : EligibilityVerdict.Ineligible($"already waiting on {candidate.UnpaidClaims} unpaid claims");
}
