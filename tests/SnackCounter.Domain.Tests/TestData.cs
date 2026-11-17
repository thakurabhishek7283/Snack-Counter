using Shouldly;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;

namespace SnackCounter.Domain.Tests;

internal static class TestData
{
    public static readonly Currency Nok = Currency.Of("NOK");
    public static readonly Currency Inr = Currency.Of("INR");

    public static Money NOK(decimal amount) => new(amount, Nok);
    public static Money INR(decimal amount) => new(amount, Inr);

    /// <summary>Tuesday 17 Nov 2026, 09:00 in Oslo (UTC+1).</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    public static Office Oslo { get; } =
        new(OfficeId.New(), "Oslo", Nok, "Europe/Oslo", NOK(6000), NOK(300), NOK(1200));

    public static Office Bengaluru { get; } =
        new(OfficeId.New(), "Bengaluru", Inr, "Asia/Kolkata", INR(15000), INR(500), INR(3000));

    public static readonly CounterId OsloCounter = CounterId.New();

    public static Employee Employee(OfficeId home, params DayOfWeek[] officeDays) =>
        new(EmployeeId.New(), "Test", "HR-1", "chat-1", home, Nok,
            (officeDays.Length == 0 ? Weekdays : officeDays).ToHashSet());

    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
}

internal static class ResultAssertions
{
    public static void ShouldSucceed(this Result result) =>
        result.IsSuccess.ShouldBeTrue($"Expected success but got {result.Error?.Code}: {result.Error?.Message}");

    public static T ShouldSucceed<T>(this Result<T> result)
    {
        result.IsSuccess.ShouldBeTrue($"Expected success but got {result.Error?.Code}: {result.Error?.Message}");
        return result.Value;
    }

    public static void ShouldFailWith(this Result result, string code)
    {
        result.IsFailure.ShouldBeTrue($"Expected failure {code} but the operation succeeded.");
        result.Error!.Code.ShouldBe(code);
    }
}
