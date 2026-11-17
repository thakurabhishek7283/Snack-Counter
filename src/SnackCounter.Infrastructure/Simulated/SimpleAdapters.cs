using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;

namespace SnackCounter.Infrastructure.Simulated;

/// <summary>Receipts kept in memory. Production would use private blob storage.</summary>
public sealed class InMemoryReceiptStore : IReceiptStore
{
    private readonly ConcurrentDictionary<string, StoredReceipt> _receipts = new();

    public Task<string> Save(byte[] content, string contentType, CancellationToken ct)
    {
        var key = $"receipts/{Guid.CreateVersion7():N}";
        _receipts[key] = new StoredReceipt(content.ToArray(), contentType);
        return Task.FromResult(key);
    }

    public Task<StoredReceipt> Get(string key, CancellationToken ct) =>
        _receipts.TryGetValue(key, out var receipt)
            ? Task.FromResult(receipt)
            : throw new KeyNotFoundException($"Receipt {key} not found.");
}

public sealed class FxRateOptions
{
    public const string Section = "Fx";

    /// <summary>Keyed "FROM->TO", e.g. "INR->NOK": 0.125. The inverse is derived if missing.</summary>
    public Dictionary<string, decimal> Rates { get; set; } = [];
}

/// <summary>Fixed rates from configuration. Swap for a real provider (central bank feed) behind the same interface.</summary>
public sealed class ConfiguredFxRateProvider(IOptions<FxRateOptions> options) : IFxRateProvider
{
    public Task<decimal?> GetMonthlyRate(Currency from, Currency to, BudgetMonth month, CancellationToken ct)
    {
        if (from == to) return Task.FromResult<decimal?>(1m);

        var rates = options.Value.Rates;
        if (rates.TryGetValue($"{from}->{to}", out var direct)) return Task.FromResult<decimal?>(direct);
        if (rates.TryGetValue($"{to}->{from}", out var inverse) && inverse > 0)
            return Task.FromResult<decimal?>(decimal.Round(1m / inverse, 8));

        return Task.FromResult<decimal?>(null);
    }
}
