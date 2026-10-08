using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
        if (!string.Equals(audience, TokenClaims.ApiAudience, StringComparison.Ordinal))
            throw new InvalidOperationException("Authentication:Audience must be tixflow-api.");
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
                RequireAudience = true,
                ValidAudience = audience,
                IgnoreTrailingSlashWhenValidatingAudience = false,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                NameClaimType = TokenClaims.Username,
                RoleClaimType = TokenClaims.Roles
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    ILogger logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("TixFlow.Api.Authentication");
                    // Exception messages can contain token data. Log only the failure type and route.
                    logger.LogWarning("JWT authentication failed ({FailureType}) for {Path}",
                        context.Exception.GetType().Name, context.Request.Path);
                    if (environment.IsDevelopment())
                        context.Response.Headers["X-TixFlow-Authentication-Error"] = context.Exception.GetType().Name;
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (context.Principal is null || !TokenClaims.HasRequiredIdentityClaims(context.Principal))
                        context.Fail("Valid sub, email and preferred_username claims are required.");
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization(options =>
        {
            // RequireAuthorization() and every named business policy share this invariant.
            // Technical Keycloak roles do not count; Admin does not inherit Customer/Organizer.
            AuthorizationPolicy userPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => TokenClaims.HasSingleApplicationRole(context.User))
                .Build();
            options.DefaultPolicy = userPolicy;
            foreach (string role in TokenClaims.ApplicationRoles)
                options.AddPolicy(role, policy => policy.Combine(userPolicy).RequireRole(role));
            // RequireRole(a, b) means either role. Combining two named policies would require both.
            options.AddPolicy("OrganizerOrAdmin", policy =>
                policy.Combine(userPolicy).RequireRole("Organizer", "Admin"));
            options.AddPolicy("CustomerOrAdmin", policy =>
                policy.Combine(userPolicy).RequireRole("Customer", "Admin"));
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
