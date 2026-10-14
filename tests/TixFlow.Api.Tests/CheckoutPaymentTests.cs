using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TixFlow.Api.Tests;

public sealed class CheckoutPaymentTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly Guid SessionId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid TicketTypeId = Guid.Parse("50000000-0000-0000-0000-000000000012");

    [Fact]
    public async Task CheckoutCreatesOneOrderAndMockSuccessSettlesInventoryOnce()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string email = $"phase6-{suffix}@example.test";
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token($"phase6-{suffix}", email, ["Customer"]));
        try
        {
            using HttpResponseMessage profile = await client.GetAsync("/api/v1/users/me");
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            using HttpResponseMessage holdResponse = await Send(client, HttpMethod.Post, "/api/v1/holds", "hold-key",
                new { eventSessionId = SessionId, items = new[] { new { ticketTypeId = TicketTypeId, inventoryMode = "GeneralAdmission", quantity = 1 } } }, 0);
            Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
            JsonElement hold = await Json(holdResponse);
            Guid holdId = hold.GetProperty("data").GetProperty("id").GetGuid();

            using HttpResponseMessage checkout = await Send(client, HttpMethod.Post, $"/api/v1/holds/{holdId}/checkout", "checkout-key", null, 0);
            Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
            JsonElement order = await Json(checkout);
            Guid orderId = order.GetProperty("data").GetProperty("id").GetGuid();
            Assert.Equal("PendingPayment", order.GetProperty("data").GetProperty("status").GetString());
            Assert.Equal(0, order.GetProperty("data").GetProperty("items")[0].GetProperty("serviceFee").GetDecimal());

            using HttpResponseMessage exactCheckoutReplay = await Send(client, HttpMethod.Post,
                $"/api/v1/holds/{holdId}/checkout", "checkout-key", null, 0);
            Assert.Equal(HttpStatusCode.Created, exactCheckoutReplay.StatusCode);
            Assert.Equal("true", exactCheckoutReplay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal(orderId, (await Json(exactCheckoutReplay)).GetProperty("data").GetProperty("id").GetGuid());

            using HttpResponseMessage checkoutReplay = await Send(client, HttpMethod.Post,
                $"/api/v1/holds/{holdId}/checkout", "different-key", null, 999);
            Assert.Equal(HttpStatusCode.OK, checkoutReplay.StatusCode);
            Assert.Equal(orderId, (await Json(checkoutReplay)).GetProperty("data").GetProperty("id").GetGuid());

            using HttpResponseMessage paymentResponse = await Send(client, HttpMethod.Post,
                $"/api/v1/orders/{orderId}/payments/mock", "payment-key", new { outcome = "Succeeded" }, 0);
            Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
            Guid paymentId = (await Json(paymentResponse)).GetProperty("data").GetProperty("id").GetGuid();
            using HttpResponseMessage exactPaymentReplay = await Send(client, HttpMethod.Post,
                $"/api/v1/orders/{orderId}/payments/mock", "payment-key", new { outcome = "Succeeded" }, 0);
            Assert.Equal(HttpStatusCode.Created, exactPaymentReplay.StatusCode);
            Assert.Equal("true", exactPaymentReplay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal("Pending", (await Json(exactPaymentReplay)).GetProperty("data").GetProperty("status").GetString());

            JsonElement settled = default;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                using HttpResponseMessage current = await client.GetAsync($"/api/v1/orders/{orderId}");
                settled = (await Json(current)).GetProperty("data");
                if (settled.GetProperty("status").GetString() == "Paid") break;
                await Task.Delay(100);
            }
            Assert.Equal("Paid", settled.GetProperty("status").GetString());
            using HttpResponseMessage paymentStatus = await client.GetAsync($"/api/v1/payments/{paymentId}");
            Assert.Equal("Succeeded", (await Json(paymentStatus)).GetProperty("data").GetProperty("status").GetString());
            Assert.Equal(1, settled.GetProperty("items").GetArrayLength());
        }
        finally
        {
            await Cleanup(email);
        }
    }

    [Fact]
    public async Task HumanTokenCannotCallServiceCallback()
    {
        using HttpClient human = factory.CreateClient();
        human.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token("phase6-human", "phase6-human@example.test", ["Customer"]));
        var callback = new { providerEventId = Guid.NewGuid().ToString("N"), paymentId = Guid.NewGuid(),
            providerTransactionId = "test-txn", outcome = "Succeeded", amount = 1m, currency = "VND", occurredAtUtc = DateTimeOffset.UtcNow };
        using HttpResponseMessage denied = await human.PostAsJsonAsync("/api/v1/payment-webhooks/mock", callback);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using HttpClient service = factory.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.MockServiceToken());
        using HttpResponseMessage accepted = await service.PostAsJsonAsync("/api/v1/payment-webhooks/mock", callback);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Ignored", (await Json(accepted)).GetProperty("data").GetProperty("disposition").GetString());
        using HttpResponseMessage duplicate = await service.PostAsJsonAsync("/api/v1/payment-webhooks/mock", callback);
        Assert.Equal("Duplicate", (await Json(duplicate)).GetProperty("data").GetProperty("disposition").GetString());
    }

    [Fact]
    public async Task MockTimeoutFailsAttemptAndAllowsAnIdempotentRetry()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string email = $"phase6-timeout-{suffix}@example.test";
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Token($"phase6-timeout-{suffix}", email, ["Customer"]));
        try
        {
            using HttpResponseMessage profile = await client.GetAsync("/api/v1/users/me");
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            using HttpResponseMessage holdResponse = await Send(client, HttpMethod.Post, "/api/v1/holds", "timeout-hold",
                new { eventSessionId = SessionId, items = new[] { new { ticketTypeId = TicketTypeId, inventoryMode = "GeneralAdmission", quantity = 1 } } }, 0);
            Guid holdId = (await Json(holdResponse)).GetProperty("data").GetProperty("id").GetGuid();
            using HttpResponseMessage checkout = await Send(client, HttpMethod.Post, $"/api/v1/holds/{holdId}/checkout", "timeout-checkout", null, 0);
            Guid orderId = (await Json(checkout)).GetProperty("data").GetProperty("id").GetGuid();
            using HttpResponseMessage timeout = await Send(client, HttpMethod.Post, $"/api/v1/orders/{orderId}/payments/mock",
                "timeout-payment", new { outcome = "Timeout" }, 0);
            Guid firstPaymentId = (await Json(timeout)).GetProperty("data").GetProperty("id").GetGuid();
            JsonElement firstStatus = default;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                using HttpResponseMessage current = await client.GetAsync($"/api/v1/payments/{firstPaymentId}");
                firstStatus = (await Json(current)).GetProperty("data");
                if (firstStatus.GetProperty("status").GetString() != "Pending") break;
                await Task.Delay(100);
            }
            Assert.Equal("Failed", firstStatus.GetProperty("status").GetString());
            Assert.Equal("MOCK_TIMEOUT", firstStatus.GetProperty("failureReason").GetString());

            using HttpResponseMessage retry = await Send(client, HttpMethod.Post, $"/api/v1/orders/{orderId}/payments/mock",
                "retry-payment", new { outcome = "Succeeded" }, 0);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Guid retryPaymentId = (await Json(retry)).GetProperty("data").GetProperty("id").GetGuid();
            JsonElement finalStatus = default;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                using HttpResponseMessage current = await client.GetAsync($"/api/v1/payments/{retryPaymentId}");
                finalStatus = (await Json(current)).GetProperty("data");
                if (finalStatus.GetProperty("status").GetString() != "Pending") break;
                await Task.Delay(100);
            }
            Assert.Equal("Succeeded", finalStatus.GetProperty("status").GetString());
        }
        finally
        {
            await Cleanup(email);
        }
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string key,
        object? body, long expectedVersion)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Expected-Version", expectedVersion.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task Cleanup(string email)
    {
        await using var connection = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("""
            DELETE FROM payment_webhook_events WHERE provider = 'mock' AND provider_event_id LIKE 'mock-event-%';
            DELETE FROM orders WHERE user_id IN (SELECT id FROM users WHERE email = @email);
            DELETE FROM holds WHERE user_id IN (SELECT id FROM users WHERE email = @email);
            DELETE FROM idempotency_records WHERE user_id IN (SELECT id FROM users WHERE email = @email);
            UPDATE ticket_inventories SET held_quantity = 0, sold_quantity = 0, version = version + 1, updated_at_utc = now()
            WHERE ticket_type_id = @ticket_type;
            """, connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("ticket_type", TicketTypeId);
        await command.ExecuteNonQueryAsync();
    }
}
