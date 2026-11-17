using static SnackCounter.Domain.Refills.RefillStatus;
using static SnackCounter.Domain.Refills.RefillTrigger;

namespace SnackCounter.Domain.Refills;

public enum RefillStatus { Open, Offered, Escalated, OnHold, Committed, Completed, Cancelled }

public enum RefillTrigger { Offer, Decline, Expire, Commit, Complete, Escalate, Hold, Resume, Cancel }

/// <summary>
/// The single source of truth for which lifecycle moves are legal (LLD section 4.2).
/// Guards such as "only the offered person may commit" live in the <see cref="Refill"/> methods.
/// </summary>
public static class RefillTransitions
{
    private static readonly Dictionary<(RefillStatus From, RefillTrigger Trigger), RefillStatus> Map = new()
    {
        [(Open, Offer)] = Offered,
        [(Open, Escalate)] = Escalated,

        [(Open, Hold)] = OnHold,
        [(Offered, Hold)] = OnHold,
        [(Escalated, Hold)] = OnHold,
        [(OnHold, Resume)] = Open,

        [(Offered, Decline)] = Open,
        [(Offered, Expire)] = Open,
        [(Offered, Commit)] = Committed,
        [(Escalated, Commit)] = Committed,

        [(Committed, Expire)] = Open,
        [(Committed, Complete)] = Completed,

        // Late completion: a previous assignee who missed the deadline but still bought the snacks.
        [(Open, Complete)] = Completed,
        [(Offered, Complete)] = Completed,
        [(Escalated, Complete)] = Completed,
        [(OnHold, Complete)] = Completed,

        [(Open, Cancel)] = Cancelled,
        [(Offered, Cancel)] = Cancelled,
        [(Escalated, Cancel)] = Cancelled,
        [(OnHold, Cancel)] = Cancelled,
        [(Committed, Cancel)] = Cancelled,
    };

    public static IReadOnlyCollection<RefillStatus> ActiveStatuses { get; } = [Open, Offered, Escalated, OnHold, Committed];

    public static bool IsActive(RefillStatus status) => status is not (Completed or Cancelled);

    public static bool CanFire(RefillStatus from, RefillTrigger trigger) => Map.ContainsKey((from, trigger));

    public static RefillStatus Next(RefillStatus from, RefillTrigger trigger) =>
        Map.TryGetValue((from, trigger), out var to)
            ? to
            : throw new InvalidOperationException($"Illegal refill transition {from} --{trigger}-->.");

    public static IEnumerable<(RefillStatus From, RefillTrigger Trigger, RefillStatus To)> All =>
        Map.Select(kv => (kv.Key.From, kv.Key.Trigger, kv.Value));
}
