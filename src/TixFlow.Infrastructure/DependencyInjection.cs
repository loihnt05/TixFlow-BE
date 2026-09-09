using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TixFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // EF Core, Redis and RabbitMQ registrations will be added in phase 2.
        return services;
    }
}

