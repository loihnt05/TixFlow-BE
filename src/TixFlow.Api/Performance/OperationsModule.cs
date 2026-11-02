using Npgsql;
using System.Text.Json;

namespace TixFlow.Api.Performance;

public static class OperationsModule
{
    public static void MapOperations(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/metrics", async (HttpContext context, IConfiguration configuration, CatalogCache cache, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT coalesce(json_agg(m), '[]')::text FROM (SELECT worker_id, sampled_at_utc, clock_timestamp()-sampled_at_utc > interval '20 seconds' AS stale, values_json FROM processing_metrics ORDER BY sampled_at_utc DESC LIMIT 20) m", connection);
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
            return Results.Ok(new { instance = Environment.MachineName, catalogCache = cache.Snapshot(), workers = document.RootElement.Clone() });
        }).RequireAuthorization("Admin");
    }
}
