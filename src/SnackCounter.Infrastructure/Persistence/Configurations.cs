using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;

namespace SnackCounter.Infrastructure.Persistence;

internal static class MappingExtensions
{
    /// <summary>xmin is PostgreSQL's built-in row version: optimistic concurrency with no extra column.</summary>
    public const string RowVersion = "xmin";

    public const string ActiveRefillFilter = "status IN ('Open','Offered','Escalated','OnHold','Committed')";

    public static void MapMoney(this ComplexPropertyBuilder<Money> money, string prefix)
    {
        money.Property(m => m.Amount).HasColumnName($"{prefix}_amount");
        money.Property(m => m.Currency).HasColumnName($"{prefix}_currency");
    }
}

internal sealed class OfficeConfiguration : IEntityTypeConfiguration<Office>
{
    public void Configure(EntityTypeBuilder<Office> b)
    {
        b.ToTable("offices");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.Property(o => o.Name).HasMaxLength(100);
        b.Property(o => o.TimeZoneId).HasMaxLength(64);
        b.Property(o => o.Currency);                 // getter-only properties must be mapped explicitly
        b.Property(o => o.AllowVisitorsToVolunteer);
        b.ComplexProperty(o => o.MonthlyBudget, m => m.MapMoney("monthly_budget"));
        b.ComplexProperty(o => o.TypicalMinPurchase, m => m.MapMoney("typical_min_purchase"));
        b.ComplexProperty(o => o.TypicalMaxPurchase, m => m.MapMoney("typical_max_purchase"));
        b.Ignore(o => o.TimeZone);
    }
}

internal sealed class CounterConfiguration : IEntityTypeConfiguration<Counter>
{
    public void Configure(EntityTypeBuilder<Counter> b)
    {
        b.ToTable("counters");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Name).HasMaxLength(100);
        b.HasOne<Office>().WithMany().HasForeignKey(c => c.OfficeId);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employees");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.DisplayName).HasMaxLength(200);
        b.Property(e => e.HrEmployeeId).HasMaxLength(64);
        b.Property(e => e.ChatUserId).HasMaxLength(128);
        b.Property(e => e.DefaultOfficeDays)
            .HasConversion(new OfficeDaysConverter(), new OfficeDaysComparer())
            .HasColumnName("default_office_days_mask");
        b.HasIndex(e => e.HrEmployeeId).IsUnique();
        b.HasIndex(e => e.ChatUserId).IsUnique();
        b.HasOne<Office>().WithMany().HasForeignKey(e => e.HomeOfficeId);
    }
}

internal sealed class AvailabilityConfiguration : IEntityTypeConfiguration<Availability>
{
    public void Configure(EntityTypeBuilder<Availability> b)
    {
        b.ToTable("availability");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.From).HasColumnName("from_date");
        b.Property(a => a.To).HasColumnName("to_date");
        b.Property(a => a.OfficeId).HasColumnName("visiting_office_id");
        b.Property(a => a.Kind);
        b.HasIndex(a => new { a.EmployeeId, a.From });
        b.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId);
        b.HasOne<Office>().WithMany().HasForeignKey(a => a.OfficeId);
    }
}

internal sealed class RefillConfiguration : IEntityTypeConfiguration<Refill>
{
    public void Configure(EntityTypeBuilder<Refill> b)
    {
        b.ToTable("refills");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property<uint>(MappingExtensions.RowVersion).IsRowVersion();
        b.Property(r => r.Purchase).HasConversion(new PurchaseConverter()).HasColumnType("jsonb");
        b.Property(r => r.CancellationReason).HasMaxLength(500);

        b.HasMany(r => r.Attempts).WithOne().HasForeignKey("RefillId").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Attempts).AutoInclude().UsePropertyAccessMode(PropertyAccessMode.Field);

        // The database is the real guard against two active refills for one box (two people buying the same day).
        b.HasIndex(r => r.CounterId)
            .IsUnique()
            .HasFilter(MappingExtensions.ActiveRefillFilter)
            .HasDatabaseName("ux_refills_one_active_per_counter");

        // The sweeper scans only refills with a running clock.
        b.HasIndex(r => r.DeadlineUtc)
            .HasFilter("status IN ('Offered','Committed')")
            .HasDatabaseName("ix_refills_due");

        b.HasIndex(r => new { r.OfficeId, r.Status });
        b.HasOne<Counter>().WithMany().HasForeignKey(r => r.CounterId);
        b.HasOne<Office>().WithMany().HasForeignKey(r => r.OfficeId);
        b.HasOne<Employee>().WithMany().HasForeignKey(r => r.AssigneeId);
        b.HasOne<Employee>().WithMany().HasForeignKey(r => r.ReportedBy);

        b.Ignore(r => r.IsActive);
        b.Ignore(r => r.DirectedOfferCount);
        b.Ignore(r => r.AttemptedEmployees);
        b.Ignore(r => r.DomainEvents);
    }
}

internal sealed class AssignmentAttemptConfiguration : IEntityTypeConfiguration<AssignmentAttempt>
{
    public void Configure(EntityTypeBuilder<AssignmentAttempt> b)
    {
        b.ToTable("assignment_attempts");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever(); // client-generated, so new attempts are INSERTed, not UPDATEd
        b.Property(a => a.Explanation).HasMaxLength(500);
        b.Property(a => a.Kind);
        b.Property(a => a.OfferedAt);
        b.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId);
        b.Ignore(a => a.IsPending);
    }
}

internal sealed class BudgetPeriodConfiguration : IEntityTypeConfiguration<BudgetPeriod>
{
    public void Configure(EntityTypeBuilder<BudgetPeriod> b)
    {
        b.ToTable("budget_periods");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property<uint>(MappingExtensions.RowVersion).IsRowVersion();
        b.ComplexProperty(p => p.Limit, m => m.MapMoney("limit"));

        b.HasMany(p => p.Entries).WithOne().HasForeignKey("BudgetPeriodId").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Entries).AutoInclude().UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(p => new { p.OfficeId, p.Month }).IsUnique().HasDatabaseName("ux_budget_period");
        b.HasOne<Office>().WithMany().HasForeignKey(p => p.OfficeId);

        b.Ignore(p => p.Currency);
        b.Ignore(p => p.Spent);
        b.Ignore(p => p.Reserved);
        b.Ignore(p => p.Remaining);
        b.Ignore(p => p.SpendCount);
        b.Ignore(p => p.OverBudgetCount);
        b.Ignore(p => p.DomainEvents);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("budget_ledger_entries");
        b.Property<long>("Id").UseIdentityAlwaysColumn();
        b.HasKey("Id");
        b.ComplexProperty(e => e.Amount, m => m.MapMoney("amount"));
        b.Property(e => e.Note).HasMaxLength(500);
        b.HasIndex(e => e.RefillId);
    }
}

internal sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> b)
    {
        b.ToTable("claims");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property<uint>(MappingExtensions.RowVersion).IsRowVersion();
        b.ComplexProperty(c => c.Amount, m => m.MapMoney("amount"));
        b.Property(c => c.Description).HasMaxLength(500);
        b.Property(c => c.ReceiptKey).HasMaxLength(200);
        b.Property(c => c.HrClaimId).HasMaxLength(64);
        b.Property(c => c.RejectionReason).HasMaxLength(500);
        b.HasIndex(c => c.RefillId);
        b.HasIndex(c => new { c.EmployeeId, c.Status });
        b.HasOne<Refill>().WithMany().HasForeignKey(c => c.RefillId);
        b.HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId);
        b.HasOne<Office>().WithMany().HasForeignKey(c => c.OfficeId);

        b.Ignore(c => c.IdempotencyKey);
        b.Ignore(c => c.IsUnpaid);
        b.Ignore(c => c.IsAwaitingHr);
        b.Ignore(c => c.DomainEvents);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Payload).HasConversion(new OutboxPayloadConverter()).HasColumnType("jsonb");
        b.Property(m => m.OccurredAt);
        b.Property(m => m.LastError).HasMaxLength(2000);
        b.HasIndex(m => m.NextAttemptAt).HasFilter("processed_at IS NULL").HasDatabaseName("ix_outbox_due");
    }
}
