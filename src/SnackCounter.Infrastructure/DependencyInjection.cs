using Microsoft.Extensions.DependencyInjection;
using SnackCounter.Application.Abstractions;
using SnackCounter.Infrastructure.InMemory;
using SnackCounter.Infrastructure.Persistence;
using SnackCounter.Infrastructure.Simulated;

namespace SnackCounter.Infrastructure;

public static class DependencyInjection
{
    /// <summary>In-memory persistence for tests and the quick demo. PostgreSQL (EF Core) plugs into the same ports.</summary>
    public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryDatabase>();
        services.AddScoped<AggregateTracker>();
        services.AddScoped<IUnitOfWork, InMemoryUnitOfWork>();
        services.AddScoped<IOrganisationRepository, InMemoryOrganisationRepository>();
        services.AddScoped<IAvailabilityRepository, InMemoryAvailabilityRepository>();
        services.AddScoped<IRefillRepository, InMemoryRefillRepository>();
        services.AddScoped<IBudgetRepository, InMemoryBudgetRepository>();
        services.AddScoped<IClaimRepository, InMemoryClaimRepository>();
        services.AddScoped<InMemoryOutbox>();
        services.AddScoped<IOutbox>(sp => sp.GetRequiredService<InMemoryOutbox>());
        services.AddScoped<IOutboxStore>(sp => sp.GetRequiredService<InMemoryOutbox>());
        return services;
    }

    /// <summary>EF Core + PostgreSQL behind the same ports. Handlers and tests above this line don't change.</summary>
    public static IServiceCollection AddPostgresPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SnackDbContext>(options => PostgresSetup.Configure(options, connectionString));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOrganisationRepository, EfOrganisationRepository>();
        services.AddScoped<IAvailabilityRepository, EfAvailabilityRepository>();
        services.AddScoped<IRefillRepository, EfRefillRepository>();
        services.AddScoped<IBudgetRepository, EfBudgetRepository>();
        services.AddScoped<IClaimRepository, EfClaimRepository>();
        services.AddScoped<EfOutbox>();
        services.AddScoped<IOutbox>(sp => sp.GetRequiredService<EfOutbox>());
        services.AddScoped<IOutboxStore>(sp => sp.GetRequiredService<EfOutbox>());
        return services;
    }

    /// <summary>Simulated stand-ins for the chat tool and the HR system, plus in-memory receipts and configured FX rates.</summary>
    public static IServiceCollection AddSimulatedIntegrations(this IServiceCollection services, Action<FxRateOptions>? configureFx = null)
    {
        services.AddSingleton<SimulatedChatGateway>();
        services.AddSingleton<IChatGateway>(sp => sp.GetRequiredService<SimulatedChatGateway>());
        services.AddSingleton<SimulatedHrGateway>();
        services.AddSingleton<IHrGateway>(sp => sp.GetRequiredService<SimulatedHrGateway>());
        services.AddSingleton<IReceiptStore, InMemoryReceiptStore>();
        services.AddSingleton<IFxRateProvider, ConfiguredFxRateProvider>();

        var fx = services.AddOptions<FxRateOptions>();
        if (configureFx is not null) fx.Configure(configureFx);
        return services;
    }
}
