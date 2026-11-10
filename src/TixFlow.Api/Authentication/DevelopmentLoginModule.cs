using System.Net;
using System.Text.Json;

namespace TixFlow.Api.Authentication;

public static class DevelopmentLoginModule
{
    public sealed record LoginRequest(string UsernameOrEmail, string Password);

    public static IEndpointRouteBuilder MapDevelopmentLogin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/login", LoginAsync)
            .WithTags("Authentication")
            .WithSummary("Development only: exchange username/email and password for an access token")
            .WithDescription("Use the returned accessToken in Swagger Authorize. This endpoint is absent outside Development.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(LoginRequest? request, HttpContext context,
        IConfiguration configuration, IHttpClientFactory clientFactory, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request is null || string.IsNullOrWhiteSpace(request.UsernameOrEmail)
            || string.IsNullOrEmpty(request.Password) || request.UsernameOrEmail.Length > 320
            || request.Password.Length > 1024)
            return Results.Problem(statusCode: 400, title: "VALIDATION_ERROR",
                detail: "Username/email and password are required.");

        string metadata = configuration["Authentication:MetadataAddress"]
            ?? throw new InvalidOperationException("Authentication:MetadataAddress is required for development login.");
        var tokenUri = new Uri(metadata.Replace("/.well-known/openid-configuration",
            "/protocol/openid-connect/token", StringComparison.Ordinal));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "tixflow-dev-login",
            ["username"] = request.UsernameOrEmail.Trim(),
            ["password"] = request.Password,
            ["scope"] = "openid profile email"
        });
        try
        {
            using HttpResponseMessage response = await clientFactory.CreateClient("LoginToken")
                .PostAsync(tokenUri, form, cancellationToken);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                return Results.Problem(statusCode: 401, title: "INVALID_CREDENTIALS",
                    detail: "Invalid credentials or account is not ready for login.");
            if (!response.IsSuccessStatusCode)
                return Results.Problem(statusCode: 503, title: "IDENTITY_UNAVAILABLE",
                    detail: "Identity service is unavailable.");
            using JsonDocument token = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return Results.Ok(new
            {
                accessToken = token.RootElement.GetProperty("access_token").GetString(),
                tokenType = token.RootElement.GetProperty("token_type").GetString(),
                expiresIn = token.RootElement.GetProperty("expires_in").GetInt32()
            });
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 503, title: "IDENTITY_UNAVAILABLE");
        }
    }
}
