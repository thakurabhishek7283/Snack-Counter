using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using SnackCounter.Application;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Chat;
using SnackCounter.Application.Features.Outbox;
using SnackCounter.Application.Features.Refills;
using SnackCounter.Domain.Budgeting;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Domain.Refills;
using SnackCounter.Domain.Reimbursement;
using SnackCounter.Infrastructure;
using SnackCounter.Infrastructure.InMemory;
using SnackCounter.Infrastructure.Simulated;

namespace SnackCounter.Application.Tests;

/// <summary>
/// The real application wired through DI, with in-memory persistence, simulated chat and HR,
/// and a fake clock. Each Send runs in its own scope, like one HTTP request or one job tick.
/// </summary>
public sealed class TestApp : IDisposable
{
    /// <summary>Tuesday 17 Nov 2026: 09:00 in Oslo, 13:30 in Bengaluru.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Illustrative test rate, not a real quote.</summary>
    public const decimal InrToNok = 0.125m;

    private static readonly byte[] ReceiptImage = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private readonly ServiceProvider _services;

    public TestApp(Action<AssignmentOptions>? configure = null, DateTimeOffset? start = null)
    {
        Clock = new FakeTimeProvider(start ?? DefaultStart);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSnackCounterApplication()
                .AddInMemoryPersistence()
                .AddSimulatedIntegrations(fx => fx.Rates["INR->NOK"] = InrToNok);
        if (configure is not null) services.Configure(configure);

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Oslo = new Office(OfficeId.New(), "Oslo", Currency.Of("NOK"), "Europe/Oslo",
            Money.Of(6000, "NOK"), Money.Of(300, "NOK"), Money.Of(1200, "NOK"));
        Bengaluru = new Office(OfficeId.New(), "Bengaluru", Currency.Of("INR"), "Asia/Kolkata",
            Money.Of(15000, "INR"), Money.Of(500, "INR"), Money.Of(3000, "INR"));
        OsloCounter = new Counter(CounterId.New(), Oslo.Id, "Kitchen");
        BengaluruCounter = new Counter(CounterId.New(), Bengaluru.Id, "Pantry");

        Db.Offices.AddRange([Oslo, Bengaluru]);
        Db.Counters.AddRange([OsloCounter, BengaluruCounter]);
    }

    public FakeTimeProvider Clock { get; }
    public Office Oslo { get; }
    public Office Bengaluru { get; }
    public Counter OsloCounter { get; }
    public Counter BengaluruCounter { get; }

    public InMemoryDatabase Db => _services.GetRequiredService<InMemoryDatabase>();
    public SimulatedChatGateway Chat => _services.GetRequiredService<SimulatedChatGateway>();
    public SimulatedHrGateway Hr => _services.GetRequiredService<SimulatedHrGateway>();

    // ---------- People ----------

    public Employee Volunteer(string name, Office office, params DayOfWeek[] officeDays) =>
        AddEmployee(name, office, isVolunteer: true, isAdmin: false, officeDays);

    public Employee Admin(string name, Office office) => AddEmployee(name, office, isVolunteer: false, isAdmin: true, []);

    public Employee Colleague(string name, Office office) => AddEmployee(name, office, isVolunteer: false, isAdmin: false, []);

    /// <summary>Gives someone a refill history, so the fairness score has something to work with.</summary>
    public void SeedPastRefills(Employee buyer, Office office, int count, int daysAgo)
    {
        var at = Clock.GetUtcNow().AddDays(-daysAgo);
        for (var i = 0; i < count; i++)
        {
            var refill = Domain.Refills.Refill.Report(CounterId.New(), office.Id, office.Currency, buyer.Id, at);
            refill.Escalate(at);
            refill.Commit(buyer.Id, at, TimeSpan.FromDays(1));
            refill.Complete(buyer.Id, office.TypicalMinPurchase, "seed-receipt", null, at);
            refill.DequeueDomainEvents();
            Db.Refills.Add(refill);
        }
    }

    // ---------- Driving the app ----------

    public async Task<Result<T>> Send<T>(IRequest<T> request)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(request);
    }

    public async Task<T> Ok<T>(IRequest<T> request)
    {
        var result = await Send(request);
        result.IsSuccess.ShouldBeTrue($"{request.GetType().Name} failed: {result.Error?.Code} {result.Error?.Message}");
        return result.Value;
    }

    public async Task<Error> Fails<T>(IRequest<T> request)
    {
        var result = await Send(request);
        result.IsFailure.ShouldBeTrue($"{request.GetType().Name} was expected to fail but succeeded.");
        return result.Error!;
    }

    public async Task<string> ChatAs(Employee employee, string text) =>
        (await Ok(new HandleChatEvent(employee.ChatUserId, text))).Text;

    public Task<CompleteResult> Complete(RefillId refill, Employee buyer, decimal amount, string? currency = null)
    {
        var office = Db.Offices.Single(o => o.Id == Refill(refill).OfficeId);
        return Ok(new CompleteRefill(refill, buyer.Id, amount, currency ?? office.Currency.Code, ReceiptImage, "image/jpeg"));
    }

    /// <summary>Report, let the system pick, commit as the picked person, buy. Returns the refill and the buyer.</summary>
    public async Task<(RefillId Refill, EmployeeId Buyer)> RunRefillCycle(Counter counter, Employee reporter, decimal amount)
    {
        var report = await Ok(new ReportEmpty(counter.Id, reporter.Id));
        var buyer = report.AssigneeId ?? throw new InvalidOperationException($"Nobody was offered the refill ({report.Status}).");
        await Ok(new CommitToRefill(report.RefillId, buyer));
        await Complete(report.RefillId, Db.Employees.Single(e => e.Id == buyer), amount);
        return (report.RefillId, buyer);
    }

    /// <summary>Runs the outbox until nothing more is due (handlers can enqueue follow-up messages).</summary>
    public async Task DeliverOutbox()
    {
        for (var round = 0; round < 10; round++)
            if (await Ok(new ProcessOutbox()) == 0) return;
    }

    // ---------- Inspecting state ----------

    public Refill Refill(RefillId id) => Db.Refills.Single(r => r.Id == id);
    public Refill ActiveRefill(Counter counter) => Db.Refills.Single(r => r.CounterId == counter.Id && r.IsActive);
    public Claim ClaimFor(RefillId refill) => Db.Claims.Single(c => c.RefillId == refill);
    public Employee Person(EmployeeId id) => Db.Employees.Single(e => e.Id == id);

    public BudgetPeriod? Budget(Office office, BudgetMonth? month = null)
    {
        var m = month ?? office.BudgetMonthOf(Clock.GetUtcNow());
        return Db.Budgets.SingleOrDefault(b => b.OfficeId == office.Id && b.Month == m);
    }

    public void Dispose() => _services.Dispose();

    private Employee AddEmployee(string name, Office office, bool isVolunteer, bool isAdmin, DayOfWeek[] officeDays)
    {
        var employee = new Employee(
            EmployeeId.New(), name, $"HR-{name}", $"chat-{name}", office.Id, office.Currency,
            (officeDays.Length == 0 ? Weekdays : officeDays).ToHashSet(), isVolunteer, isAdmin);
        Db.Employees.Add(employee);
        return employee;
    }
}
