using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TixFlow.Api.Authentication;

namespace TixFlow.Api.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("Staging", "http://id.example/realms/tixflow", "https://id.example/discovery")]
    [InlineData("Production", "https://id.example/realms/tixflow", "http://keycloak:8080/discovery")]
    public void NonDevelopmentRejectsHttpAuthorityOrMetadata(string environment, string authority, string metadata)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = authority, ["Authentication:MetadataAddress"] = metadata,
            ["Authentication:Audience"] = "tixflow-api"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddIdentityAuthentication(configuration, new TestEnvironment(environment)));
    }

    [Fact]
    public void ProfileMappingFiltersRolesAndUsesUsernameFallback()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "subject"), new Claim("email", "user@example.test"),
            new Claim("preferred_username", "Display username"), new Claim("roles", "Customer"),
            new Claim("roles", "Customer"), new Claim("roles", "realm-admin"), new Claim("roles", "admin")
        }, "Bearer", "name", "roles"));
        var profile = TokenClaims.ReadProfile(principal);
        Assert.NotNull(profile);
        Assert.Equal("Display username", profile.Name);
        Assert.Equal(new[] { "Customer" }, profile.Roles);
        Assert.True(principal.IsInRole("Customer"));
        Assert.False(principal.IsInRole("Admin"));
    }

    [Fact]
    public void JwtBearerKeepsOriginalClaimsAndSeparatesPublicIssuerFromInternalMetadata()
    {
        const string authority = "http://localhost:8180/realms/tixflow";
        const string metadata = "http://keycloak:8080/realms/tixflow/.well-known/openid-configuration";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = authority,
            ["Authentication:MetadataAddress"] = metadata,
            ["Authentication:Audience"] = "tixflow-api"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityAuthentication(configuration, new TestEnvironment("Development"));
        using ServiceProvider provider = services.BuildServiceProvider();

        JwtBearerOptions options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(options.MapInboundClaims);
        Assert.Equal(authority, options.Authority);
        Assert.Equal(metadata, options.MetadataAddress);
        Assert.Equal("preferred_username", options.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
        Assert.Equal("tixflow-api", options.TokenValidationParameters.ValidAudience);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
