using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace TixFlow.Api.Tests;

public sealed class AuthenticationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private HttpClient Client(string? token = null)
    {
        HttpClient client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static (string Sub, string Email) Identity()
    {
        string id = Guid.NewGuid().ToString();
        return (id, $"test-{id}@example.test");
    }

    [Fact]
    public async Task PublicEndpointsRemainAvailableAndProtectedEndpointsChallenge()
    {
        using HttpClient client = Client();
        foreach (string path in new[] { "/health", "/api/v1/events", "/api/v1/system/info", "/api/v1/bookings/status", "/api/v1/assistant/status" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        using HttpResponseMessage swagger = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        using JsonDocument openApi = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        Assert.True(openApi.RootElement.GetProperty("components")
            .GetProperty("securitySchemes").TryGetProperty("oidc", out _));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("internal-issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("subject")]
    [InlineData("username")]
    public async Task InvalidTokensAreRejected(string defect)
    {
        var identity = Identity();
        string token = factory.Token(identity.Sub, identity.Email, ["Customer"],
            issuer: defect == "issuer" ? "https://attacker.invalid/realms/tixflow"
                : defect == "internal-issuer" ? "http://keycloak:8080/realms/tixflow" : null,
            audience: defect == "audience" ? "tixflow-web" : "tixflow-api",
            expired: defect == "expired", wrongSignature: defect == "signature", includeSubject: defect != "subject");
        if (defect == "username")
            token = factory.Token(identity.Sub, identity.Email, ["Customer"], includeUsername: false);
        using HttpClient client = Client(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Theory]
    [InlineData("Admin", "admin")]
    [InlineData("Organizer", "organizer")]
    [InlineData("Customer", "customer")]
    public async Task PoliciesRequireTheExactRole(string role, string path)
    {
        var identity = Identity();
        using HttpClient permitted = Client(factory.Token(identity.Sub, identity.Email, [role]));
        Assert.Equal(HttpStatusCode.OK, (await permitted.GetAsync($"/api/v1/users/access/{path}")).StatusCode);
        using HttpClient denied = Client(factory.Token(identity.Sub, identity.Email, [role.ToLowerInvariant()]));
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync($"/api/v1/users/access/{path}")).StatusCode);
    }

    [Fact]
    public async Task CustomerCannotAccessAdminOrOrganizer()
    {
        var identity = Identity();
        using HttpClient client = Client(factory.Token(identity.Sub, identity.Email, ["Customer"]));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users/access/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users/access/organizer")).StatusCode);
    }

    public static IEnumerable<object[]> InvalidApplicationRoleSets()
    {
        yield return new object[] { Array.Empty<string>() };
        yield return new object[] { new[] { "Customer", "Organizer" } };
    }

    [Theory]
    [MemberData(nameof(InvalidApplicationRoleSets))]
    public async Task TokenMustHaveExactlyOneApplicationRoleAndDeniedRequestsDoNotCreateUsers(string[] roles)
    {
        var identity = Identity();
        using HttpClient client = Client(factory.Token(identity.Sub, identity.Email, roles));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users/me")).StatusCode);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM users WHERE identity_subject = @sub", connection);
        command.Parameters.AddWithValue("sub", identity.Sub);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task TechnicalRolesAreIgnoredWhenOneApplicationRoleIsPresent()
    {
        var identity = Identity();
        using HttpClient client = Client(factory.Token(identity.Sub, identity.Email,
            ["offline_access", "Customer", "uma_authorization"]));
        using HttpResponseMessage response = await client.GetAsync("/api/v1/users/me");
        response.EnsureSuccessStatusCode();
        Me user = (await response.Content.ReadFromJsonAsync<Me>())!;
        Assert.Equal(new[] { "Customer" }, user.Roles);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedRequestsCreateOnlyOnePasswordlessProfile()
    {
        var identity = Identity();
        using HttpClient client = Client(factory.Token(identity.Sub, identity.Email, ["Customer"]));
        Me[] users = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using HttpResponseMessage response = await client.GetAsync("/api/v1/users/me");
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<Me>())!;
        }));
        Assert.Single(users.Select(user => user.Id).Distinct());
        Assert.All(users, user =>
        {
            Assert.Equal(identity.Sub, user.Sub);
            Assert.Equal(identity.Email, user.Email);
            Assert.Equal("Auth Test", user.Name);
            Assert.Equal(new[] { "Customer" }, user.Roles);
        });
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM users WHERE identity_subject = @sub AND password_hash IS NULL", connection);
        command.Parameters.AddWithValue("sub", identity.Sub);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ConcurrentSubjectsCannotClaimTheSameEmail()
    {
        string email = $"shared-{Guid.NewGuid()}@example.test";
        using HttpClient first = Client(factory.Token(Guid.NewGuid().ToString(), email, ["Customer"]));
        using HttpClient second = Client(factory.Token(Guid.NewGuid().ToString(), email.ToUpperInvariant(), ["Customer"]));

        HttpResponseMessage[] responses = await Task.WhenAll(
            first.GetAsync("/api/v1/users/me"),
            second.GetAsync("/api/v1/users/me"));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM users WHERE normalized_email = @email AND password_hash IS NULL", connection);
        command.Parameters.AddWithValue("email", email.ToUpperInvariant());
        Assert.Equal(1L, await command.ExecuteScalarAsync());

        foreach (HttpResponseMessage response in responses) response.Dispose();
    }

    [Fact]
    public async Task SyncUpdatesProfileWithoutChangingLocalIdAndTokenRolesControlAccess()
    {
        var identity = Identity();
        using HttpClient first = Client(factory.Token(identity.Sub, identity.Email, ["Admin"]));
        Me original = (await first.GetFromJsonAsync<Me>("/api/v1/users/me"))!;
        using HttpClient changed = Client(factory.Token(identity.Sub, $"updated-{identity.Email}", ["Customer"], name: "Updated Name"));
        Me updated = (await changed.GetFromJsonAsync<Me>("/api/v1/users/me"))!;
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal($"updated-{identity.Email}", updated.Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await changed.GetAsync("/api/v1/users/access/admin")).StatusCode);
    }

    [Fact]
    public async Task ExistingSeedIsNeverLinkedByEmail()
    {
        using HttpClient client = Client(factory.Token(Guid.NewGuid().ToString(), "CUSTOMER@tixflow.local", ["Admin"]));
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM users WHERE id = '00000000-0000-0000-0000-000000000002' AND identity_subject IS NULL AND role = 'Customer' AND password_hash IS NOT NULL", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MissingEmailInvalidatesAuthentication()
    {
        var identity = Identity();
        using HttpClient client = Client(factory.Token(identity.Sub, identity.Email, ["Customer"], includeEmail: false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task SynchronizationDoesNotReactivateSuspendedLocalUsers()
    {
        var identity = Identity();
        using HttpClient client = Client(factory.Token(identity.Sub, identity.Email, ["Customer"]));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE users SET status = 'Suspended' WHERE identity_subject = @sub", connection);
        command.Parameters.AddWithValue("sub", identity.Sub);
        await command.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task MigrationCanBeReappliedWithoutChangingSeedData()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        foreach (string fileName in new[] { "002_keycloak_identity.sql", "004_phase3_catalog_seed.sql" })
        {
            string sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, fileName));
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await using var migration = new NpgsqlCommand(sql, connection);
                await migration.ExecuteNonQueryAsync();
            }
        }
        await using var command = new NpgsqlCommand("SELECT count(*) FROM events WHERE slug = 'tixflow-flash-sale-demo'", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        await using var count = new NpgsqlCommand("SELECT count(*) FROM events", connection);
        Assert.Equal(12L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CorsAllowsOnlyTheFrontendOrigin()
    {
        using HttpClient client = Client();
        foreach (string origin in new[] { "http://localhost:3000", "https://untrusted.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/users/me");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
            using HttpResponseMessage response = await client.SendAsync(request);
            bool allowed = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values);
            Assert.Equal(origin == "http://localhost:3000", allowed);
            if (allowed) Assert.Equal(origin, Assert.Single(values!));
        }
    }

    private sealed record Me(Guid Id, string Sub, string Email, string Name, string[] Roles);
}
