using System.Globalization;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Domain.Assignment;

/// <summary>A read-only snapshot of one possible buyer, built by the application layer.</summary>
public sealed record Candidate(
    EmployeeId Id,
    OfficeId HomeOfficeId,
    bool IsVolunteer,
    Presence PresenceToday,
    Presence PresenceNextWorkingDay,
    int RecentRefills,
    DateTimeOffset? LastRefillAt,
    int LifetimeRefills,
    int UnpaidClaims);

/// <summary>Everything the rules need about the refill being assigned. Plain data, no aggregates.</summary>
public sealed record AssignmentContext(
    OfficeId CounterOfficeId,
    bool OfficeAllowsVisitors,
    IReadOnlySet<EmployeeId> AlreadyAttempted,
    int UnpaidClaimsCap,
    IReadOnlyDictionary<string, double> Weights,
    DateTimeOffset Now)
{
    public double WeightOf(string scorer) => Weights.TryGetValue(scorer, out var weight) ? weight : 1.0;
}

public sealed record ScoringContext(AssignmentContext Assignment, IReadOnlyList<Candidate> EligiblePool);

public readonly record struct EligibilityVerdict(bool IsEligible, string? Reason)
{
    public static EligibilityVerdict Eligible => new(true, null);
    public static EligibilityVerdict Ineligible(string reason) => new(false, reason);
}

/// <summary>A hard filter. Every rule must pass. Add a rule by adding a class (open/closed).</summary>
public interface IEligibilityRule
{
    string Name { get; }
    EligibilityVerdict Evaluate(Candidate candidate, AssignmentContext context);
}

/// <summary>A soft preference, normalised to 0..1 (higher means a better pick). Weighted by config.</summary>
public interface IScoringRule
{
    string Name { get; }
    double Score(Candidate candidate, ScoringContext context);
}

public sealed record RankedCandidate(Candidate Candidate, double Total, IReadOnlyDictionary<string, double> Breakdown)
{
    /// <summary>Human-readable "why you", shown in the offer so the choice feels fair rather than arbitrary.</summary>
    public string Explanation
    {
        get
        {
            var c = Candidate;
            var last = c.LastRefillAt is null ? "never refilled before" : "last refill " + c.LastRefillAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var where = c.PresenceToday.Kind == PresenceKind.AtOffice ? "in the office today" : "in the office next working day";
            return string.Create(CultureInfo.InvariantCulture, $"{c.RecentRefills} refills in the last 90 days, {last}, {where}");
        }
    }
}

public sealed record ExcludedCandidate(Candidate Candidate, string Rule, string Reason);

public sealed record AssignmentDecision(IReadOnlyList<RankedCandidate> Ranked, IReadOnlyList<ExcludedCandidate> Excluded)
{
    public RankedCandidate? Top => Ranked.Count > 0 ? Ranked[0] : null;
}
