using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace TixFlow.Api.Tests;

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<AuthApiFactory>;

[CollectionDefinition("Baseline")]
public sealed class BaselineCollection : ICollectionFixture<AuthApiFactory>;

public sealed partial class AuthApiFactory
{
    // Immutable images and resource ceilings are part of baseline-v1, not tunable per run.
    public const string PostgresImage = "postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea";
    public const string RabbitImage = "rabbitmq:4-management-alpine@sha256:628bd74c1c7e2a820bf417b32e75eed1bd8d517345d9749ee8bd4076ae393abe";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage(PostgresImage).WithDatabase("tixflow_auth_tests")
        .WithUsername("tixflow_test").WithPassword("isolated_test_only")
        .WithCommand("-c", "shared_preload_libraries=pg_stat_statements", "-c", "track_io_timing=on",
            "-c", "max_connections=120", "-c", "shared_buffers=128MB")
        .WithCreateParameterModifier(p => { p.HostConfig.NanoCPUs = 1_000_000_000; p.HostConfig.Memory = 512L * 1024 * 1024; })
        .Build();
    private readonly RabbitMqContainer rabbit = new RabbitMqBuilder()
        .WithImage(RabbitImage).WithUsername("tixflow_test").WithPassword("isolated_test_only")
        .WithCreateParameterModifier(p => { p.HostConfig.NanoCPUs = 1_000_000_000; p.HostConfig.Memory = 512L * 1024 * 1024; })
        .Build();

    public string BrokerHost => rabbit.Hostname;
    public int BrokerPort => rabbit.GetMappedPublicPort(5672);
    public IConfiguration BrokerConfig => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RabbitMq:Host"] = BrokerHost, ["RabbitMq:Port"] = BrokerPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RabbitMq:Username"] = "tixflow_test", ["RabbitMq:Password"] = "isolated_test_only"
    }).Build();

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
            ConnectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            {
                MaxPoolSize = 40, MinPoolSize = 0, Timeout = 120, CommandTimeout = 120,
                ApplicationName = "tixflow-baseline-api"
            }.ConnectionString;
            await ExecuteSqlAsync("CREATE EXTENSION pg_stat_statements;");
            string database = Path.Combine(AppContext.BaseDirectory, "database");
            await ExecuteSqlAsync(await File.ReadAllTextAsync(Path.Combine(database, "init", "001_initial_schema.sql")));
            foreach (string path in Directory.GetFiles(Path.Combine(database, "migrations"), "*.sql").Order(StringComparer.Ordinal))
                await ExecuteSqlAsync(await File.ReadAllTextAsync(path));
            await ExecuteSqlAsync("CREATE TABLE baseline_control (dataset text PRIMARY KEY); INSERT INTO baseline_control VALUES ('baseline-v1');");
            // Booking demo dates must not turn correctness tests into calendar-dependent failures.
            // Preserve the November catalog fixture used by the date-filter test.
            await ExecuteSqlAsync("""
                UPDATE event_sessions SET starts_at_utc = '2099-12-20 12:00Z', ends_at_utc = '2099-12-20 15:00Z'
                  WHERE id = '40000000-0000-0000-0000-000000000001';
                UPDATE events SET sale_starts_at_utc = '2020-01-01', sale_ends_at_utc = '2099-12-20 11:00Z'
                  WHERE id = (SELECT event_id FROM event_sessions WHERE id = '40000000-0000-0000-0000-000000000001');
                UPDATE ticket_types SET sale_starts_at_utc = '2020-01-01', sale_ends_at_utc = '2099-12-20 11:00Z'
                  WHERE event_session_id = '40000000-0000-0000-0000-000000000001';
                """);
        }
        catch
        {
            await rabbit.DisposeAsync();
            await postgres.DisposeAsync();
            throw;
        }
    }

    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        try { await rabbit.DisposeAsync(); }
        finally { await postgres.DisposeAsync(); }
    }
}
