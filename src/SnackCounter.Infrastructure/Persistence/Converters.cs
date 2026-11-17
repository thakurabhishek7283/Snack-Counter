using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Infrastructure.Persistence;

// Strongly typed ids are stored as plain uuid columns.
internal sealed class OfficeIdConverter() : ValueConverter<OfficeId, Guid>(v => v.Value, v => new OfficeId(v));
internal sealed class CounterIdConverter() : ValueConverter<CounterId, Guid>(v => v.Value, v => new CounterId(v));
internal sealed class EmployeeIdConverter() : ValueConverter<EmployeeId, Guid>(v => v.Value, v => new EmployeeId(v));
internal sealed class RefillIdConverter() : ValueConverter<RefillId, Guid>(v => v.Value, v => new RefillId(v));
internal sealed class ClaimIdConverter() : ValueConverter<ClaimId, Guid>(v => v.Value, v => new ClaimId(v));
internal sealed class BudgetPeriodIdConverter() : ValueConverter<BudgetPeriodId, Guid>(v => v.Value, v => new BudgetPeriodId(v));

internal sealed class CurrencyConverter() : ValueConverter<Currency, string>(v => v.Code, v => Currency.Of(v));

/// <summary>"2026-11". Sorts correctly as text and reads well in SQL.</summary>
internal sealed class BudgetMonthConverter() : ValueConverter<BudgetMonth, string>(v => v.ToString(), v => BudgetMonth.Parse(v));

/// <summary>Default office days as a bit mask (bit 0 = Sunday ... bit 6 = Saturday).</summary>
internal sealed class OfficeDaysConverter() : ValueConverter<IReadOnlySet<DayOfWeek>, int>(
    days => days.Aggregate(0, (mask, day) => mask | (1 << (int)day)),
    mask => Enum.GetValues<DayOfWeek>().Where(day => (mask & (1 << (int)day)) != 0).ToHashSet());

internal sealed class OfficeDaysComparer() : ValueComparer<IReadOnlySet<DayOfWeek>>(
    (a, b) => a != null && b != null ? a.SetEquals(b) : a == b,
    days => days.Aggregate(0, (mask, day) => mask | (1 << (int)day)),
    days => days.ToHashSet());

/// <summary>
/// The purchase is written once, when the refill completes, and only ever read as a whole, so it lives in
/// a jsonb column. Queries never need its insides: the buyer is also the refill's assignee once completed.
/// </summary>
internal sealed class PurchaseConverter() : ValueConverter<Purchase?, string>(
    p => JsonSerializer.Serialize(PurchaseDocument.From(p!), Json.Options),
    s => JsonSerializer.Deserialize<PurchaseDocument>(s, Json.Options)!.ToDomain())
{
    private sealed record PurchaseDocument(Guid BuyerId, decimal Amount, string Currency, string ReceiptKey, DateTimeOffset PurchasedAt, List<PurchaseItem> Items, bool IsLate)
    {
        public static PurchaseDocument From(Purchase p) =>
            new(p.BuyerId.Value, p.Amount.Amount, p.Amount.Currency.Code, p.ReceiptKey, p.PurchasedAt, p.Items.ToList(), p.IsLate);

        public Purchase ToDomain() =>
            new(new EmployeeId(BuyerId), new Money(Amount, Domain.Common.Currency.Of(Currency)), ReceiptKey, PurchasedAt, Items, IsLate);
    }
}

/// <summary>
/// Outbox payloads are stored as {"type": "...", "data": {...}}. Only known payload types can be
/// deserialised: the type name is never used to load arbitrary CLR types.
/// </summary>
internal sealed class OutboxPayloadConverter() : ValueConverter<object, string>(
    payload => OutboxPayloadSerializer.Serialize(payload),
    json => OutboxPayloadSerializer.Deserialize(json));

internal static class OutboxPayloadSerializer
{
    private static readonly Dictionary<string, Type> KnownTypes = new()
    {
        [nameof(SendChatMessage)] = typeof(SendChatMessage),
        [nameof(SubmitClaimToHr)] = typeof(SubmitClaimToHr),
    };

    public static string Serialize(object payload)
    {
        var name = payload.GetType().Name;
        if (!KnownTypes.ContainsKey(name))
            throw new InvalidOperationException($"Outbox payload type {name} is not registered in {nameof(OutboxPayloadSerializer)}.");

        return new JsonObject
        {
            ["type"] = name,
            ["data"] = JsonSerializer.SerializeToNode(payload, payload.GetType(), Json.Options),
        }.ToJsonString();
    }

    public static object Deserialize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Empty outbox payload.");
        var name = node["type"]?.GetValue<string>() ?? throw new InvalidOperationException("Outbox payload has no type.");
        var type = KnownTypes.TryGetValue(name, out var t) ? t : throw new InvalidOperationException($"Unknown outbox payload type {name}.");
        return node["data"].Deserialize(type, Json.Options) ?? throw new InvalidOperationException($"Outbox payload {name} has no data.");
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
