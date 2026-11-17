using Microsoft.EntityFrameworkCore;
using SnackCounter.Api.Endpoints;
using SnackCounter.Api.Hosting;
using SnackCounter.Api.Http;
using SnackCounter.Application;
using SnackCounter.Infrastructure;
using SnackCounter.Infrastructure.InMemory;
using SnackCounter.Infrastructure.Persistence;
using SnackCounter.Infrastructure.Seed;
using SnackCounter.Infrastructure.Simulated;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Clock: adjustable in dev so timeouts can be demoed live ----------
var devToolsEnabled = config.GetValue("DevTools:Enabled", builder.Environment.IsDevelopment());
if (devToolsEnabled)
{
    var clock = new AdjustableTimeProvider();
    builder.Services.AddSingleton(clock);
    builder.Services.AddSingleton<TimeProvider>(clock);
}

// ---------- Application core ----------
builder.Services.AddSnackCounterApplication();
builder.Services.Configure<AssignmentOptions>(config.GetSection(AssignmentOptions.Section));
builder.Services.Configure<ReimbursementOptions>(config.GetSection(ReimbursementOptions.Section));
builder.Services.Configure<ReportingOptions>(config.GetSection(ReportingOptions.Section));

// ---------- Adapters: persistence is chosen by configuration, integrations are simulated ----------
var usePostgres = string.Equals(config["Persistence:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase);
if (usePostgres)
    builder.Services.AddPostgresPersistence(config.GetConnectionString("Snack")
        ?? throw new InvalidOperationException("ConnectionStrings:Snack is required when Persistence:Provider is Postgres."));
else
    builder.Services.AddInMemoryPersistence();

builder.Services.AddSimulatedIntegrations(fx => config.GetSection(FxRateOptions.Section).Bind(fx));
builder.Services.AddSnackCounterJobs(config.GetSection(JobOptions.Section).Get<JobOptions>() ?? new JobOptions());

// ---------- HTTP ----------
builder.Services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false); // 400/401 from binding, not exceptions
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

await InitialiseStorage(app, usePostgres);

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Binding failures (e.g. the missing employee header) carry their own status: 400/401, not 500.
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseCors();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

var api = app.MapGroup("/api");
api.MapRefillEndpoints()
   .MapOfficeEndpoints()
   .MapPeopleEndpoints()
   .MapIntegrationEndpoints();
if (devToolsEnabled) api.MapDevEndpoints();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" })).ExcludeFromDescription();

await app.RunAsync();

static async Task InitialiseStorage(WebApplication app, bool usePostgres)
{
    var config = app.Configuration;
    await using var scope = app.Services.CreateAsyncScope();

    if (usePostgres)
    {
        var db = scope.ServiceProvider.GetRequiredService<SnackDbContext>();
        if (config.GetValue("Database:MigrateOnStartup", true)) await db.Database.MigrateAsync();
        if (config.GetValue("Seed:Demo", true)) await DemoData.SeedPostgres(db);
    }
    else if (config.GetValue("Seed:Demo", true))
    {
        DemoData.SeedInMemory(scope.ServiceProvider.GetRequiredService<InMemoryDatabase>());
    }
}

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
