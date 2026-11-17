using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Application.Services;

public sealed class BudgetService(IBudgetRepository budgets)
{
    /// <summary>Gets the office's budget for the month, opening it with the office's default limit the first time.</summary>
    public Task<BudgetPeriod> GetOrOpen(Office office, BudgetMonth month, CancellationToken ct) =>
        budgets.GetOrOpen(office.Id, month, office.MonthlyBudget, ct);
}
