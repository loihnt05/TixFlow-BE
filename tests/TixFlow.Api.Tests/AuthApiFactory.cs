using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "http://localhost:8180/realms/tixflow";
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("TIXFLOW_TEST_POSTGRES")
        ?? "Host=localhost;Port=15433;Database=tixflow_auth_tests;Username=tixflow_test;Password=isolated_test_only";
    private readonly RSA rsa = RSA.Create(2048);
    private RsaSecurityKey SigningKey => new(rsa) { KeyId = "test-key" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (new NpgsqlConnectionStringBuilder(ConnectionString).Database != "tixflow_auth_tests")
            throw new InvalidOperationException("Tests must use the isolated tixflow_auth_tests database.");
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
            ["Authentication:Authority"] = Issuer,
            ["Authentication:Audience"] = "tixflow-api"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TixFlowDbContext>>();
            services.AddDbContext<TixFlowDbContext>(options => options
                .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Replace only discovery transport; the production JwtBearer validation/policies run unchanged.
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        });
    }

    public string Token(string subject, string email, string[] roles, string? issuer = null,
        string audience = "tixflow-api", bool expired = false, bool wrongSignature = false,
        bool includeSubject = true, bool includeEmail = true, string name = "Auth Test")
    {
        var claims = new Dictionary<string, object> { ["name"] = name, ["roles"] = roles };
        if (includeSubject) claims["sub"] = subject;
        if (includeEmail) claims["email"] = email;
        using RSA otherKey = RSA.Create(2048);
        var key = wrongSignature ? new RsaSecurityKey(otherKey) { KeyId = "test-key" } : SigningKey;
        return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer, Audience = audience, Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-10), NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = expired ? DateTime.UtcNow.AddMinutes(-2) : DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) rsa.Dispose();
    }
}
