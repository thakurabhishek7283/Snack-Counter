using System.Globalization;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Application.Features.Refills;

/// <summary>
/// The buyer uploads the receipt. In one transaction: record the spend, close the refill, and create the
/// reimbursement claim, which the outbox then sends to HR automatically.
/// </summary>
public sealed record CompleteRefill(
    RefillId RefillId,
    EmployeeId BuyerId,
    decimal Amount,
    string Currency,
    byte[] Receipt,
    string ReceiptContentType,
    IReadOnlyList<PurchaseItem>? Items = null) : ICommand<CompleteResult>;

public sealed record CompleteResult(RefillId RefillId, ClaimId ClaimId, Money Amount, bool OverBudget, bool IsLate);

public sealed class CompleteRefillHandler(
    IRefillRepository refills,
    IOrganisationRepository organisation,
    IBudgetRepository budgetRepository,
    BudgetService budgets,
    IBudgetPolicy budgetPolicy,
    IClaimRepository claims,
    IReceiptStore receipts,
    IUnitOfWork unitOfWork,
    TimeProvider time) : IRequestHandler<CompleteRefill, CompleteResult>
{
    public async Task<Result<CompleteResult>> Handle(CompleteRefill command, CancellationToken ct)
    {
        if (command.Receipt is not { Length: > 0 }) return AppErrors.ReceiptRequired;
        if (!Currency.TryParse(command.Currency, out var currency)) return AppErrors.InvalidCurrency(command.Currency);

        var refill = await refills.Get(command.RefillId, ct);
        if (refill is null) return RefillErrors.NotFound(command.RefillId);
        var office = await organisation.GetOffice(refill.OfficeId, ct);
        if (office is null) return AppErrors.OfficeNotFound(refill.OfficeId);

        var amount = new Money(command.Amount, currency);
        var allowed = refill.CanComplete(command.BuyerId, amount);
        if (allowed.IsFailure) return allowed.Error!;

        var now = time.GetUtcNow();

        // Spend counts in the month of purchase (office time). The reservation may sit in an earlier month
        // if the buyer committed on the 31st and bought on the 1st.
        var spendPeriod = await budgets.GetOrOpen(office, office.BudgetMonthOf(now), ct);
        var spend = spendPeriod.RecordSpend(refill.Id, amount, budgetPolicy, now);
        if (spend.IsFailure) return spend.Error!;

        var reservedIn = await budgetRepository.FindWithOpenReservation(refill.Id, ct);
        reservedIn?.Release(refill.Id, now);

        var receiptKey = await receipts.Save(command.Receipt, command.ReceiptContentType, ct);
        refill.Complete(command.BuyerId, amount, receiptKey, command.Items, now);

        var description = string.Create(CultureInfo.InvariantCulture,
            $"Snack refill, {office.Name} office, {office.LocalDate(now):yyyy-MM-dd} (refill {refill.Id})");
        var claim = Claim.Create(refill.Id, command.BuyerId, office.Id, amount, description, receiptKey, now);
        claims.Add(claim);

        await unitOfWork.SaveChanges(ct);
        return new CompleteResult(refill.Id, claim.Id, amount, spend.Value.OverBudget, refill.Purchase!.IsLate);
    }
}
