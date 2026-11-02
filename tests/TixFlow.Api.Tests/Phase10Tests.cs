using System.Net;
using Npgsql;

namespace TixFlow.Api.Tests;

[Collection("Integration")]
public sealed class Phase10Tests(AuthApiFactory factory)
{
    [Fact]
    public async Task CheckoutCanProceedWhileCatalogRowsHaveSharedReaders()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        try
        {
            using var client = data.Client();
            using var created = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "checkout-reader-hold");
            Guid hold = (await BaselineDataset.Data(created)).GetProperty("id").GetGuid();
            await using var reader = new NpgsqlConnection(factory.ConnectionString);
            await reader.OpenAsync();
            await using var tx = await reader.BeginTransactionAsync();
            await using var command = new NpgsqlCommand($"""
                SELECT id FROM events WHERE id='{BaselineDataset.Event}' FOR SHARE;
                SELECT id FROM event_sessions WHERE id='{BaselineDataset.Session}' FOR SHARE;
                """, reader, tx);
            await command.ExecuteNonQueryAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await BaselineDataset.Post(client, $"/api/v1/holds/{hold}/checkout",
                null, "checkout-reader", timeout.Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally { await data.ClearAsync(); }
    }

    [Fact]
    public async Task CheckoutWaitsForLifecycleWriterAndRejectsCancelledEvent()
    {
        var data = new BaselineDataset(factory);
        await data.ResetAsync();
        try
        {
            using var client = data.Client();
            using var created = await BaselineDataset.Post(client, "/api/v1/holds", BaselineDataset.Selection(), "checkout-writer-hold");
            Guid hold = (await BaselineDataset.Data(created)).GetProperty("id").GetGuid();
            await using var writer = new NpgsqlConnection(factory.ConnectionString);
            await writer.OpenAsync();
            await using var tx = await writer.BeginTransactionAsync();
            await using var command = new NpgsqlCommand($"UPDATE events SET status='Cancelled' WHERE id='{BaselineDataset.Event}'", writer, tx);
            await command.ExecuteNonQueryAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var checkout = BaselineDataset.Post(client, $"/api/v1/holds/{hold}/checkout", null, "checkout-writer", timeout.Token);
            bool blocked = false;
            for (int i = 0; i < 100; i++)
            {
                blocked = await data.Count($"SELECT count(*) FROM pg_stat_activity WHERE {writer.ProcessID}=ANY(pg_blocking_pids(pid))") > 0;
                if (blocked) break;
                await Task.Delay(50);
            }
            Assert.True(blocked, "Checkout must retain a catalog lock that conflicts with lifecycle updates.");
            Assert.False(checkout.IsCompleted);
            await tx.CommitAsync();
            using var response = await checkout;
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("EVENT_CANCELLED", await response.Content.ReadAsStringAsync());
            Assert.Equal(0, await data.Count($"SELECT count(*) FROM orders WHERE hold_id='{hold}'"));
        }
        finally { await data.ClearAsync(); }
    }
}
