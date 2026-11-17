using Microsoft.EntityFrameworkCore;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Infrastructure.Persistence;

/// <summary>
/// Maps the domain model directly (no separate persistence model). Concessions to EF are limited to
/// private parameterless constructors, each marked "EF Core" in the domain.
/// </summary>
public sealed class SnackDbContext(DbContextOptions<SnackDbContext> options) : DbContext(options)
{
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<Counter> Counters => Set<Counter>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Availability> Availability => Set<Availability>();
    public DbSet<Refill> Refills => Set<Refill>();
    public DbSet<BudgetPeriod> Budgets => Set<BudgetPeriod>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<OfficeId>().HaveConversion<OfficeIdConverter>();
        configurationBuilder.Properties<CounterId>().HaveConversion<CounterIdConverter>();
        configurationBuilder.Properties<EmployeeId>().HaveConversion<EmployeeIdConverter>();
        configurationBuilder.Properties<RefillId>().HaveConversion<RefillIdConverter>();
        configurationBuilder.Properties<ClaimId>().HaveConversion<ClaimIdConverter>();
        configurationBuilder.Properties<BudgetPeriodId>().HaveConversion<BudgetPeriodIdConverter>();
        configurationBuilder.Properties<Currency>().HaveConversion<CurrencyConverter>().HaveMaxLength(3).AreFixedLength();
        configurationBuilder.Properties<BudgetMonth>().HaveConversion<BudgetMonthConverter>().HaveMaxLength(7).AreFixedLength();
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SnackDbContext).Assembly);
}
