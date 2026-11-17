using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SnackCounter.Application;
using SnackCounter.Application.Abstractions;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Infrastructure;
using SnackCounter.Infrastructure.Persistence;
using SnackCounter.Infrastructure.Seed;

namespace SnackCounter.Integration.Tests.Infrastructure;

public static class TestClock
{
    /// <summary>Tuesday 17 Nov 2026, 09:00 in Oslo. Fixed, so presence (office days) is deterministic.</summary>
    public static FakeTimeProvider Create() => new(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero));
}

public static class People
{
    private static readonly IReadOnlyList<Employee> All = DemoData.Employees();

    public static Employee Named(string name) => All.Single(e => e.DisplayName == name);

    public static IReadOnlyList<Employee> OsloVolunteers => All.Where(e => e.HomeOfficeId == DemoData.OsloId && e.IsVolunteer).ToList();
}

/// <summary>The application wired to a real PostgreSQL database, without HTTP. Each scope is one unit of work.</summary>
public sealed class PostgresHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private PostgresHost(ServiceProvider services, FakeTimeProvider clock)
    {
        _services = services;
        Clock = clock;
    }

    public FakeTimeProvider Clock { get; }

    public static async Task<PostgresHost> Create(PostgresFixture postgres, Action<AssignmentOptions>? configure = null)
    {
        var connectionString = postgres.NewDatabase();
        var clock = TestClock.Create();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSnackCounterApplication()
                .AddPostgresPersistence(connectionString)
                .AddSimulatedIntegrations(fx => fx.Rates["INR->NOK"] = 0.125m);
        if (configure is not null) services.Configure(configure);

        var host = new PostgresHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), clock);
        await using var scope = host.Scope();
        var db = scope.ServiceProvider.GetRequiredService<SnackDbContext>();
        await db.Database.MigrateAsync();
        await DemoData.SeedPostgres(db);
        return host;
    }

    public AsyncServiceScope Scope() => _services.CreateAsyncScope();

    public async Task<Result<T>> Send<T>(IRequest<T> request)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(request);
    }

    public async Task<T> Query<T>(Func<SnackDbContext, Task<T>> query)
    {
        await using var scope = Scope();
        return await query(scope.ServiceProvider.GetRequiredService<SnackDbContext>());
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

/// <summary>The real API (Program.cs) on PostgreSQL, with jobs off and a fake clock, driven over HTTP.</summary>
public sealed class ApiHost(string connectionString, IReadOnlyDictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = TestClock.Create();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Persistence:Provider", "Postgres");
        builder.UseSetting("ConnectionStrings:Snack", connectionString);
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Seed:Demo", "true");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>()) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public HttpClient ClientAs(Employee employee)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Employee-Id", employee.Id.Value.ToString());
        return client;
    }
}
