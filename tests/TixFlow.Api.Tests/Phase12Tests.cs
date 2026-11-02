using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TixFlow.Api.Tests;

public sealed class Phase12Tests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task MetricsAreOptInAndCorrelationIsGeneratedByServer()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        request.Headers.Add("X-Correlation-ID", "untrusted-client-value");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        Assert.NotEqual("untrusted-client-value", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/internal/metrics")).StatusCode);
    }

    [Fact]
    public async Task EnabledMetricsExposePrometheusWithoutRequestQueryValues()
    {
        using var monitored = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:Enabled"] = "true" })));
        using var client = monitored.CreateClient();
        await client.GetAsync("/api/v1/system/info?private=phase12-secret-sentinel");
        string metrics = await client.GetStringAsync("/internal/metrics");
        Assert.Contains("http_requests_received_total", metrics);
        Assert.DoesNotContain("phase12-secret-sentinel", metrics);
    }
}
