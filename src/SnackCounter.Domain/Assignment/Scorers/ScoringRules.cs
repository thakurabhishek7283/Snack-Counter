namespace SnackCounter.Domain.Assignment.Scorers;

/// <summary>Spreads the work: whoever has done the fewest refills recently scores highest.</summary>
public sealed class FewestRecentRefills : IScoringRule
{
    public string Name => nameof(FewestRecentRefills);

    public double Score(Candidate candidate, ScoringContext context)
    {
        var max = context.EligiblePool.Count == 0 ? 0 : context.EligiblePool.Max(c => c.RecentRefills);
        return max == 0 ? 1.0 : 1.0 - (double)candidate.RecentRefills / max;
    }
}

/// <summary>Longer since the last refill scores higher. Saturates at 60 days. Never refilled scores 1.</summary>
public sealed class LongestSinceLastRefill : IScoringRule
{
    public const double SaturationDays = 60;

    public string Name => nameof(LongestSinceLastRefill);

    public double Score(Candidate candidate, ScoringContext context)
    {
        if (candidate.LastRefillAt is null) return 1.0;
        var days = (context.Assignment.Now - candidate.LastRefillAt.Value).TotalDays;
        return Math.Clamp(days / SaturationDays, 0, 1);
    }
}

/// <summary>Being at the office today beats only being there the next working day: the box gets refilled sooner.</summary>
public sealed class InOfficeToday : IScoringRule
{
    public string Name => nameof(InOfficeToday);

    public double Score(Candidate candidate, ScoringContext context)
    {
        var office = context.Assignment.CounterOfficeId;
        if (candidate.PresenceToday.IsAt(office)) return 1.0;
        return candidate.PresenceNextWorkingDay.IsAt(office) ? 0.5 : 0.0;
    }
}
