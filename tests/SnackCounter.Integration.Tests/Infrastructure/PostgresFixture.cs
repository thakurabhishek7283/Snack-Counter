using Npgsql;
using Testcontainers.PostgreSql;

namespace SnackCounter.Integration.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL container for the whole test run; every test gets its own fresh database in it.
/// If Docker isn't available the tests are skipped (with the reason), not failed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"PostgreSQL container could not start (is Docker running?): {ex.GetBaseException().Message}";
        }
    }

    /// <summary>Connection string to a brand-new, empty database. EF migrations create it.</summary>
    public string NewDatabase()
    {
        Skip.If(SkipReason is not null, SkipReason);
        return new NpgsqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            Database = $"snack_{Guid.NewGuid():N}",
            IncludeErrorDetail = true,
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
