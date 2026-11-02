using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Performance;

// Each replica keeps a bounded metadata cache. The committed DB generation invalidates all replicas.
public sealed class CatalogCache(IConfiguration configuration) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();
    private long hits, misses;
    public object Snapshot() => new { hits = Interlocked.Read(ref hits), misses = Interlocked.Read(ref misses) };

    public async ValueTask<object?> GetAsync(HttpContext context, TixFlowDbContext db, Func<ValueTask<object?>> build)
    {
        if (!configuration.GetValue("CatalogCache:Enabled", true)) return await build();
        string path = context.Request.Path + context.Request.QueryString;
        if (path.Length > 2048) return await build();
        long version = await db.Database.SqlQueryRaw<long>("SELECT version AS \"Value\" FROM catalog_generation WHERE id=1")
            .SingleAsync(context.RequestAborted);
        string key = version + ":" + path;
        var gate = gates[(int)((uint)StringComparer.Ordinal.GetHashCode(key) % (uint)gates.Length)];
        await gate.WaitAsync(context.RequestAborted);
        try
        {
            if (cache.TryGetValue(key, out object? value))
            {
                Interlocked.Increment(ref hits);
                context.Response.Headers["X-Catalog-Cache"] = "HIT";
                return value;
            }
            Interlocked.Increment(ref misses);
            context.Response.Headers["X-Catalog-Cache"] = "MISS";
            value = await build();
            if (value is IStatusCodeHttpResult { StatusCode: 200 })
                cache.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });
            return value;
        }
        finally { gate.Release(); }
    }

    public void Dispose() { cache.Dispose(); foreach (var gate in gates) gate.Dispose(); }
}
