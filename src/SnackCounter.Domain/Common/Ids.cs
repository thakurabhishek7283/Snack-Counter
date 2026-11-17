namespace SnackCounter.Domain.Common;

// Strongly typed ids: the compiler stops an EmployeeId being passed where an OfficeId is expected.

public readonly record struct OfficeId(Guid Value)
{
    public static OfficeId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct CounterId(Guid Value)
{
    public static CounterId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct EmployeeId(Guid Value)
{
    public static EmployeeId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct RefillId(Guid Value)
{
    public static RefillId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct ClaimId(Guid Value)
{
    public static ClaimId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct BudgetPeriodId(Guid Value)
{
    public static BudgetPeriodId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
