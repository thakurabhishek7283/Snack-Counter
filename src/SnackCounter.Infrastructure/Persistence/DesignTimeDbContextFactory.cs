using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SnackCounter.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef migrations add` build the model without starting the API.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SnackDbContext>
{
    public SnackDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SNACK_DB")
            ?? "Host=localhost;Port=5432;Database=snack;Username=snack;Password=snack";
        var options = new DbContextOptionsBuilder<SnackDbContext>();
        PostgresSetup.Configure(options, connectionString);
        return new SnackDbContext(options.Options);
    }
}

public static class PostgresSetup
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
