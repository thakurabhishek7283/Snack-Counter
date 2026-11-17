namespace SnackCounter.Application;

/// <summary>Bound from the "Assignment" config section. Office-specific values (currency, budget, purchase range) live with the office.</summary>
public sealed class AssignmentOptions
{
    public const string Section = "Assignment";

    /// <summary>How long an offered volunteer has to accept or decline.</summary>
    public TimeSpan OfferTtl { get; set; } = TimeSpan.FromHours(2);

    /// <summary>How long a committed volunteer has to buy and upload the receipt.</summary>
    public TimeSpan PurchaseWindow { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Send one reminder after this fraction of the purchase window.</summary>
    public double ReminderAtFraction { get; set; } = 0.5;

    /// <summary>After this many directed offers fail, broadcast to everyone eligible.</summary>
    public int MaxDirectedOffers { get; set; } = 3;

    /// <summary>Volunteers with this many unpaid claims are skipped (fairness).</summary>
    public int OutstandingClaimsCap { get; set; } = 2;

    /// <summary>Window for "recent refills" in the fairness score.</summary>
    public TimeSpan RecentWindow { get; set; } = TimeSpan.FromDays(90);

    public Dictionary<string, double> Weights { get; set; } = new()
    {
        ["FewestRecentRefills"] = 0.5,
        ["LongestSinceLastRefill"] = 0.3,
        ["InOfficeToday"] = 0.2,
    };
}

public sealed class ReimbursementOptions
{
    public const string Section = "Reimbursement";

    /// <summary>HR says approval takes 1 to 10 days. Beyond that we chase.</summary>
    public TimeSpan OverdueAfter { get; set; } = TimeSpan.FromDays(10);
}

public sealed class ReportingOptions
{
    public const string Section = "Reporting";

    public string Currency { get; set; } = "NOK";
}
