using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Common;

namespace SnackCounter.Application.Features.Budget;

/// <summary>Office admin sets or raises a month's limit. Refills on hold for lack of budget resume.</summary>
public sealed record SetMonthlyBudget(OfficeId OfficeId, BudgetMonth Month, decimal Amount, EmployeeId AdminId) : ICommand<BudgetStatus>;

public sealed record GetBudgetStatus(OfficeId OfficeId, BudgetMonth? Month = null) : IQuery<BudgetStatus>;

public sealed record BudgetStatus(OfficeId OfficeId, BudgetMonth Month, Money Limit, Money Spent, Money Reserved, Money Remaining);

public sealed class SetMonthlyBudgetHandler(
    IOrganisationRepository organisation,
    BudgetService budgets,
    RefillAssigner assigner,
    IUnitOfWork unitOfWork,
    TimeProvider time) : IRequestHandler<SetMonthlyBudget, BudgetStatus>
{
    public async Task<Result<BudgetStatus>> Handle(SetMonthlyBudget command, CancellationToken ct)
    {
        var office = await organisation.GetOffice(command.OfficeId, ct);
        if (office is null) return AppErrors.OfficeNotFound(command.OfficeId);
        var admin = await organisation.GetEmployee(command.AdminId, ct);
        if (admin is null || !admin.IsAdminOf(office.Id)) return AppErrors.NotOfficeAdmin;

        var now = time.GetUtcNow();
        var period = await budgets.GetOrOpen(office, command.Month, ct);
        var changed = period.SetLimit(new Money(command.Amount, office.Currency), now);
        if (changed.IsFailure) return changed.Error!;

        if (command.Month == office.BudgetMonthOf(now)) await assigner.ResumeOnHold(office, ct);

        await unitOfWork.SaveChanges(ct);
        return new BudgetStatus(office.Id, period.Month, period.Limit, period.Spent, period.Reserved, period.Remaining);
    }
}

public sealed class GetBudgetStatusHandler(
    IOrganisationRepository organisation,
    IBudgetRepository budgets,
    TimeProvider time) : IRequestHandler<GetBudgetStatus, BudgetStatus>
{
    public async Task<Result<BudgetStatus>> Handle(GetBudgetStatus query, CancellationToken ct)
    {
        var office = await organisation.GetOffice(query.OfficeId, ct);
        if (office is null) return AppErrors.OfficeNotFound(query.OfficeId);

        var month = query.Month ?? office.BudgetMonthOf(time.GetUtcNow());
        var period = await budgets.Get(office.Id, month, ct);
        if (period is null)
        {
            var zero = Money.Zero(office.Currency);
            return new BudgetStatus(office.Id, month, office.MonthlyBudget, zero, zero, office.MonthlyBudget);
        }

        return new BudgetStatus(office.Id, month, period.Limit, period.Spent, period.Reserved, period.Remaining);
    }
}
