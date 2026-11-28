using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace TixFlow.Api.Tests;

[Collection("Integration")]
public sealed class DevelopmentLoginTests(AuthApiFactory factory)
{
    [Fact]
    public async Task LoginReturnsRealmClaimsAndTokenAuthorizesProfile()
    {
        string subject = Guid.NewGuid().ToString();
        string email = $"login-{subject}@example.test";
        string accessToken = factory.Token(subject, email, ["Customer"]);
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("LoginToken",
                options => options.HttpMessageHandlerBuilderActions.Add(handler =>
                    handler.PrimaryHandler = new TokenEndpointHandler(accessToken)))));
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { usernameOrEmail = email, password = "test-password" });
        Assert.True(login.StatusCode == HttpStatusCode.OK,
            $"Login returned {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
        using JsonDocument body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        Assert.Equal(accessToken, root.GetProperty("accessToken").GetString());
        Assert.Equal("Bearer", root.GetProperty("tokenType").GetString());
        Assert.Equal(subject, root.GetProperty("user").GetProperty("sub").GetString());
        Assert.Equal(email, root.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Customer", Assert.Single(root.GetProperty("user").GetProperty("roles").EnumerateArray()).GetString());
        Assert.Equal("Customer", Assert.Single(root.GetProperty("claims").GetProperty("roles").EnumerateArray()).GetString());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage profile = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task LoginRejectsTokenWithoutBusinessRole()
    {
        string subject = Guid.NewGuid().ToString();
        string email = $"login-{subject}@example.test";
        string accessToken = factory.Token(subject, email, []);
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("LoginToken",
                options => options.HttpMessageHandlerBuilderActions.Add(handler =>
                    handler.PrimaryHandler = new TokenEndpointHandler(accessToken)))));
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { usernameOrEmail = email, password = "test-password" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
    }

    private sealed class TokenEndpointHandler(string token) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = token,
                    token_type = "Bearer",
                    expires_in = 300
                })
            });
    }
}
