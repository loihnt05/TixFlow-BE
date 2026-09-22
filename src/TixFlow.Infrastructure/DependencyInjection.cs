<<<<<<< HEAD
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TixFlow.Infrastructure.Persistence;
=======
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88

namespace TixFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

<<<<<<< HEAD
        string connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Connection string 'Postgres' is not configured.");

        services.AddDbContext<TixFlowDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null))
                .UseSnakeCaseNamingConvention());

        return services;
    }
}
=======
        // EF Core, Redis and RabbitMQ registrations will be added in phase 2.
        return services;
    }
}

>>>>>>> 89d75a4e2fee96441a9fc51381ee676f0216da88
