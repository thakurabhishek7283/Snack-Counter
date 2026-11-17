using System.Text.Json.Serialization;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Abstractions;

// ---------- Chat tool ----------

/// <summary>Who a chat message goes to. Polymorphic JSON so it survives the outbox table.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Direct), "direct")]
[JsonDerivedType(typeof(OfficeChannel), "channel")]
[JsonDerivedType(typeof(OfficeAdmins), "admins")]
public abstract record ChatAudience
{
    public sealed record Direct(EmployeeId EmployeeId) : ChatAudience;
    public sealed record OfficeChannel(OfficeId OfficeId) : ChatAudience;
    public sealed record OfficeAdmins(OfficeId OfficeId) : ChatAudience;
}

/// <summary>A button. Its value comes back to us in the chat webhook, e.g. "accept:{refillId}".</summary>
public sealed record ChatAction(string Label, string Value);

public sealed record ChatMessage(string Text, IReadOnlyList<ChatAction> Actions)
{
    public static ChatMessage Plain(string text) => new(text, []);
}

public interface IChatGateway
{
    Task Send(ChatAudience audience, ChatMessage message, CancellationToken ct);
}

// ---------- HR system (anti-corruption layer: HR's own model never reaches the domain) ----------

public sealed record HrClaimRequest(
    string HrEmployeeId,
    decimal Amount,
    string Currency,
    string Description,
    byte[] ReceiptImage,
    string ReceiptContentType);

/// <summary>HR's answer, already translated to our <see cref="ClaimStatus"/> by the adapter.</summary>
public sealed record HrClaimStatusReport(ClaimStatus Status, string? Reason = null);

public interface IHrGateway
{
    /// <summary>Same idempotency key gives the same HR claim id, so retries never create duplicates.</summary>
    Task<string> CreateClaim(HrClaimRequest request, string idempotencyKey, CancellationToken ct);

    Task<HrClaimStatusReport> GetClaimStatus(string hrClaimId, CancellationToken ct);
}

// ---------- Receipts and FX ----------

public sealed record StoredReceipt(byte[] Content, string ContentType);

public interface IReceiptStore
{
    Task<string> Save(byte[] content, string contentType, CancellationToken ct);
    Task<StoredReceipt> Get(string key, CancellationToken ct);
}

public interface IFxRateProvider
{
    /// <summary>The month's rate for converting <paramref name="from"/> into <paramref name="to"/>, or null if unknown.</summary>
    Task<decimal?> GetMonthlyRate(Currency from, Currency to, BudgetMonth month, CancellationToken ct);
}
