using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Assignment;
using SnackCounter.Domain.Budgeting;

namespace SnackCounter.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers use cases, domain services and policies. Persistence and integrations (the ports) are
    /// registered separately by Infrastructure, so the same application runs in memory or on PostgreSQL.
    /// </summary>
    public static IServiceCollection AddSnackCounterApplication(this IServiceCollection services)
    {
        services.AddOptions<AssignmentOptions>();
        services.AddOptions<ReimbursementOptions>();
        services.AddOptions<ReportingOptions>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<BudgetService>();
        services.AddScoped<CandidateBuilder>();
        services.AddScoped<RefillAssigner>();

        // Who-buys rules: add a class here (or in the host) to change the policy. No existing code changes.
        foreach (var rule in AssignmentEngine.DefaultRules()) services.AddSingleton(rule);
        foreach (var scorer in AssignmentEngine.DefaultScorers()) services.AddSingleton(scorer);
        services.AddSingleton(sp => new AssignmentEngine(sp.GetServices<IEligibilityRule>(), sp.GetServices<IScoringRule>()));

        services.TryAddSingleton<IBudgetPolicy, SoftCapBudgetPolicy>();

        RegisterClosedImplementations(services, typeof(IRequestHandler<,>));
        RegisterClosedImplementations(services, typeof(IDomainEventHandler<>));
        RegisterClosedImplementations(services, typeof(IOutboxMessageHandler<>));
        return services;
    }

    private static void RegisterClosedImplementations(IServiceCollection services, Type openGeneric)
    {
        var implementations = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in implementations)
        foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric))
            services.AddScoped(contract, type);
    }
}
