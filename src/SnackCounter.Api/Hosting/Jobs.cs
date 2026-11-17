using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Features.Claims;
using SnackCounter.Application.Features.Outbox;
using SnackCounter.Application.Features.Refills;

namespace SnackCounter.Api.Hosting;

public sealed class JobOptions
{
    public const string Section = "Jobs";

    public bool Enabled { get; set; } = true;
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan OutboxInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ClaimSyncInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan ResumeInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Runs one application command on a fixed interval, in a fresh DI scope each tick (like a request).
/// The jobs hold no logic of their own: everything they do is a normal, tested use case.
/// </summary>
public sealed class PeriodicCommandJob<TResult>(
    string name,
    TimeSpan interval,
    Func<IRequest<TResult>> command,
    IServiceScopeFactory scopes,
    ILogger<PeriodicCommandJob<TResult>> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command(), stoppingToken);
                if (result.IsFailure) logger.LogWarning("Job {Job} failed: {Error}", name, result.Error!.Message);
                else logger.LogDebug("Job {Job} ran: {Result}", name, result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Job {Job} crashed; it will run again next tick", name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class JobRegistration
{
    public static IServiceCollection AddSnackCounterJobs(this IServiceCollection services, JobOptions options)
    {
        if (!options.Enabled) return services;

        Add("deadline-sweeper", options.SweepInterval, () => new SweepDeadlines());
        Add("outbox-dispatcher", options.OutboxInterval, () => new ProcessOutbox());
        Add("claim-status-poller", options.ClaimSyncInterval, () => new SyncClaimStatuses());
        Add("resume-on-hold", options.ResumeInterval, () => new ResumeOnHoldRefills());
        return services;

        void Add<TResult>(string name, TimeSpan interval, Func<IRequest<TResult>> command) =>
            services.AddSingleton<IHostedService>(sp => new PeriodicCommandJob<TResult>(
                name, interval, command,
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<PeriodicCommandJob<TResult>>>()));
    }
}
