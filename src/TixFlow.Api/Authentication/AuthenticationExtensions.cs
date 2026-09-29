using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace TixFlow.Api.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        string authority = (configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException("Authentication:Authority is required.")).TrimEnd('/');
        string metadata = configuration["Authentication:MetadataAddress"]
            ?? $"{authority}/.well-known/openid-configuration";
        string audience = configuration["Authentication:Audience"]
            ?? throw new InvalidOperationException("Authentication:Audience is required.");
        ValidateUrl(authority, environment.IsDevelopment());
        ValidateUrl(metadata, environment.IsDevelopment());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.MetadataAddress = metadata;
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.MapInboundClaims = false;
            options.SaveToken = false;
            options.IncludeErrorDetails = environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidateIssuer = true,
                ValidIssuer = authority,
                // Keep issuer validation fixed to the public URL even when discovery is internal.
                IssuerValidator = (issuer, _, _) => string.Equals(issuer, authority, StringComparison.Ordinal)
                    ? issuer : throw new SecurityTokenInvalidIssuerException("Unexpected token issuer."),
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                NameClaimType = "name",
                RoleClaimType = "roles"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    string? subject = context.Principal?.FindFirst("sub")?.Value;
                    if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
                        context.Fail("A valid subject is required.");
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization(options =>
        {
            foreach (string role in TokenClaims.ApplicationRoles)
                options.AddPolicy(role, policy => policy.RequireAuthenticatedUser().RequireRole(role));
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "TixFlow API", Version = "v1" });
            options.AddSecurityDefinition("oidc", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth"),
                        TokenUrl = new Uri($"{authority}/protocol/openid-connect/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            ["openid"] = "OpenID Connect", ["profile"] = "Name", ["email"] = "Email"
                        }
                    }
                }
            });
            options.OperationFilter<AuthorizationOperationFilter>();
        });
        return services;
    }

    private static void ValidateUrl(string value, bool allowHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException("Authentication URLs must use HTTPS outside Development.");
    }
}
