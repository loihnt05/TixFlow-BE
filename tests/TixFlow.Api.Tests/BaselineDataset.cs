using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace TixFlow.Api.Tests;

internal sealed class BaselineDataset(AuthApiFactory factory)
{
    public const string Event = "80000000-0000-0000-0000-000000000001";
    public const string Session = "80000000-0000-0000-0000-000000000002";
    public const string Type = "80000000-0000-0000-0000-000000000003";
    public static object Selection(int quantity = 1) => new
    {
        eventSessionId = Session,
        items = new[] { new { ticketTypeId = Type, inventoryMode = "GeneralAdmission", quantity } }
    };

    public async Task ResetAsync()
    {
        foreach (string file in new[] { "seed.sql", "reset.sql" })
            await factory.ExecuteSqlAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "baseline", file)));
    }

    public async Task ClearAsync()
    {
        await ResetAsync();
        await factory.ExecuteSqlAsync($"DELETE FROM events WHERE id='{Event}'");
    }

    public HttpClient Client(int user = 1, HttpMessageHandler? handler = null)
    {
        var client = handler is null ? factory.CreateClient() : new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken(user));
        return client;
    }

    public string UserToken(int user) => factory.Token($"baseline-v1-{user}", $"baseline-v1-{user}@example.test", ["Customer"], lifetimeMinutes: 30);

    public static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body, string key,
        CancellationToken ct = default, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Expected-Version", "0");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, ct);
    }

    public async Task<long> Count(string expression)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(expression, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
    }
}
