using SnackCounter.Domain.Assignment.Rules;
using SnackCounter.Domain.Assignment.Scorers;

namespace SnackCounter.Domain.Assignment;

/// <summary>
/// Chooses who buys. Pipeline: eligibility filters, then weighted scorers, then a deterministic tie-break.
/// Stateless and pure, so every rule is unit-testable without I/O or a clock.
/// </summary>
public sealed class AssignmentEngine
{
    private readonly IReadOnlyList<IEligibilityRule> _rules;
    private readonly IReadOnlyList<IScoringRule> _scorers;

    public AssignmentEngine(IEnumerable<IEligibilityRule> rules, IEnumerable<IScoringRule> scorers)
    {
        _rules = rules.ToList();
        _scorers = scorers.ToList();
    }

    public static IReadOnlyList<IEligibilityRule> DefaultRules() =>
    [
        new MustBeVolunteer(),
        new MustServeCounterOffice(),
        new MustBePresentSoon(),
        new MustNotHaveBeenAttempted(),
        new UnpaidClaimsBelowCap(),
    ];

    public static IReadOnlyList<IScoringRule> DefaultScorers() =>
    [
        new FewestRecentRefills(),
        new LongestSinceLastRefill(),
        new InOfficeToday(),
    ];

    public static AssignmentEngine CreateDefault() => new(DefaultRules(), DefaultScorers());

    public AssignmentDecision Rank(IReadOnlyCollection<Candidate> candidates, AssignmentContext context)
    {
        var eligible = new List<Candidate>();
        var excluded = new List<ExcludedCandidate>();

        foreach (var candidate in candidates)
        {
            var failure = _rules
                .Select(rule => (Rule: rule, Verdict: rule.Evaluate(candidate, context)))
                .FirstOrDefault(x => !x.Verdict.IsEligible);

            if (failure.Rule is null) eligible.Add(candidate);
            else excluded.Add(new ExcludedCandidate(candidate, failure.Rule.Name, failure.Verdict.Reason ?? "not eligible"));
        }

        var scoring = new ScoringContext(context, eligible);
        var ranked = eligible
            .Select(candidate =>
            {
                var breakdown = _scorers.ToDictionary(s => s.Name, s => s.Score(candidate, scoring));
                var total = breakdown.Sum(kv => context.WeightOf(kv.Key) * kv.Value);
                return new RankedCandidate(candidate, Math.Round(total, 6), breakdown);
            })
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Candidate.LifetimeRefills)
            .ThenBy(r => r.Candidate.Id.Value) // deterministic, so the same input always gives the same pick
            .ToList();

        return new AssignmentDecision(ranked, excluded);
    }
}
