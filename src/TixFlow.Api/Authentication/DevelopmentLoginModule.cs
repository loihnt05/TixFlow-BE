using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TixFlow.Api.Authentication;

public static class DevelopmentLoginModule
{
    public sealed record LoginRequest(string UsernameOrEmail, string Password);
    public sealed record LoginUser(string Sub, string Username, string Email, string Name, string[] Roles);
    public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn,
        LoginUser User, JsonElement Claims);

    public static IEndpointRouteBuilder MapDevelopmentLogin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/login", LoginAsync)
            .WithTags("Authentication")
            .WithSummary("Development only: log in with username/email and password")
            .WithDescription("Returns an access token and Keycloak identity claims. Paste accessToken into Swagger Authorize → bearer. This endpoint is absent outside Development.")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(LoginRequest? request, HttpContext context,
        IConfiguration configuration, IHttpClientFactory clientFactory,
        IOptionsMonitor<JwtBearerOptions> bearerOptions, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request is null || string.IsNullOrWhiteSpace(request.UsernameOrEmail)
            || string.IsNullOrEmpty(request.Password) || request.UsernameOrEmail.Length > 320
            || request.Password.Length > 1024)
            return Results.Problem(statusCode: 400, title: "VALIDATION_ERROR",
                detail: "Username/email and password are required.");

        string authority = (configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException("Authentication:Authority is required for development login."))
            .TrimEnd('/');
        string metadata = configuration["Authentication:MetadataAddress"]
            ?? $"{authority}/.well-known/openid-configuration";
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
            JsonElement root = token.RootElement;
            if (!root.TryGetProperty("access_token", out JsonElement accessTokenValue)
                || accessTokenValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(accessTokenValue.GetString())
                || !root.TryGetProperty("token_type", out JsonElement tokenTypeValue)
                || tokenTypeValue.ValueKind != JsonValueKind.String
                || !string.Equals(tokenTypeValue.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("expires_in", out JsonElement expiresInValue)
                || !expiresInValue.TryGetInt32(out int expiresIn) || expiresIn <= 0)
                return Results.Problem(statusCode: 503, title: "IDENTITY_INVALID_TOKEN");

            string accessToken = accessTokenValue.GetString()!;
            JwtBearerOptions options = bearerOptions.Get(JwtBearerDefaults.AuthenticationScheme);
            if (options.ConfigurationManager is null)
                return Results.Problem(statusCode: 503, title: "IDENTITY_UNAVAILABLE");
            OpenIdConnectConfiguration discovery;
            try { discovery = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken); }
            catch (InvalidOperationException)
            {
                return Results.Problem(statusCode: 503, title: "IDENTITY_UNAVAILABLE");
            }
            TokenValidationParameters parameters = options.TokenValidationParameters.Clone();
            parameters.IssuerSigningKeys = discovery.SigningKeys;
            ClaimsPrincipal principal;
            SecurityToken validatedToken;
            try
            {
                principal = new JwtSecurityTokenHandler { MapInboundClaims = false }
                    .ValidateToken(accessToken, parameters, out validatedToken);
            }
            catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
            {
                return Results.Problem(statusCode: 503, title: "IDENTITY_INVALID_TOKEN");
            }
            if (validatedToken is not JwtSecurityToken jwt)
                return Results.Problem(statusCode: 503, title: "IDENTITY_INVALID_TOKEN");
            var profile = TokenClaims.ReadProfile(principal);
            string? username = principal.FindFirst(TokenClaims.Username)?.Value;
            if (profile is null || username is null)
                return Results.Problem(statusCode: 503, title: "IDENTITY_INVALID_TOKEN",
                    detail: "Identity provider returned a token without the required API audience, identity or role.");

            using JsonDocument claims = JsonDocument.Parse(jwt.Payload.SerializeToJson());
            return Results.Ok(new LoginResponse(accessToken, tokenTypeValue.GetString()!, expiresIn,
                new LoginUser(profile.Subject, username, profile.Email, profile.Name, profile.Roles),
                claims.RootElement.Clone()));
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 503, title: "IDENTITY_UNAVAILABLE");
        }
    }
}
