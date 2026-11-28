using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TixFlow.Infrastructure.Persistence;
using TixFlow.Application.Identity;
using TixFlow.Infrastructure.Identity;
using StackExchange.Redis;
using TixFlow.Application.WaitingRoom;
using TixFlow.Infrastructure.WaitingRoom;

namespace TixFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

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

        services.AddScoped<ILocalUserSynchronizer, LocalUserSynchronizer>();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            string redis = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
            ConfigurationOptions options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IWaitingRoomStore, RedisWaitingRoomStore>();
        return services;
    }
}
