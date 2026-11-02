using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace TixFlow.Api.Modules.Assistant;

public static class AssistantModule
{
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AssistantOptions>().Bind(config.GetSection("Assistant"))
            .Validate(o => o.Provider is "OpenAI" or "Disabled", "Assistant provider must be OpenAI or Disabled.")
            .Validate(o => o.TimeoutSeconds is >= 1 and <= 120 && o.MaxToolCalls is >= 1 and <= 12
                && o.MaxOutputTokens is >= 100 and <= 4000 && o.RequestsPerMinute is >= 1 and <= 60, "Invalid assistant limits.")
            .Validate(o => Uri.TryCreate(o.BookingApiBaseUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback))
                && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query),
                "Booking API must use HTTPS or loopback HTTP, with no path, query or credentials.")
            .ValidateOnStart();
        services.AddHttpContextAccessor();
        services.AddDataProtection().SetApplicationName("TixFlow.Assistant")
            .PersistKeysToFileSystem(new DirectoryInfo(config["Assistant:KeyDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "assistant-keys")));
        services.AddSingleton<AssistantTokens>();
        services.AddScoped<AssistantTools>();
        services.AddScoped<IAssistantAudit, AssistantAudit>();
        services.AddScoped<IAssistantProvider, OpenAiAssistantProvider>();
        services.AddHttpClient<IAssistantBookingApi, AssistantBookingApi>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<AssistantOptions>>().Value.BookingApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddRateLimiter(limits =>
        {
            if (!config.GetValue("TrafficLimits:Enabled", true)) limits.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(new { code = "RATE_LIMITED", detail = "Vui lòng chờ rồi thử lại." }, ct);
            };
            limits.AddPolicy("assistant", context =>
            RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("sub")?.Value ?? "anonymous", _ => new()
            {
                PermitLimit = Math.Clamp(config.GetValue("Assistant:RequestsPerMinute", 10), 1, 60),
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            }));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapAssistantModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/assistant").WithTags("Assistant");
        group.MapGet("/status", (IOptions<AssistantOptions> options) => Results.Ok(new
        {
            module = "AI Booking Assistant", status = options.Value.Provider == "OpenAI" && !string.IsNullOrWhiteSpace(options.Value.ApiKey) ? "ready" : "fallback",
            provider = options.Value.Provider, model = options.Value.Model, confirmationRequired = true
        }));
        var commands = group.MapGroup("").RequireAuthorization("Customer").RequireRateLimiting("assistant");
        commands.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (AssistantProblem problem)
            {
                return Results.Problem(statusCode: problem.Status, title: problem.Code,
                    detail: problem.Code, extensions: new Dictionary<string, object?> { ["code"] = problem.Code });
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                return Results.Problem(statusCode: 503, title: "BOOKING_UNAVAILABLE",
                    detail: "Chưa nhận được kết quả. Bạn có thể thử xác nhận lại cùng yêu cầu; hệ thống không tạo hold trùng.");
            }
        });
        commands.MapPost("/messages", MessageAsync);
        commands.MapPost("/prepare-hold", async (PrepareHoldRequest request, AssistantTools tools, CancellationToken ct) =>
        {
            await tools.PrepareHold(request, ct);
            return Results.Ok(tools.Proposals.Single());
        });
        commands.MapPost("/confirm-hold", async (ConfirmHoldRequest request, AssistantTokens tokens,
            AssistantTools tools, IAssistantBookingApi api, CancellationToken ct) =>
        {
            if (!request.Confirmed) throw new AssistantProblem(422, "EXPLICIT_CONFIRMATION_REQUIRED");
            var state = tokens.Confirmation(tools.Subject, request.ConfirmationToken);
            return await tools.Audited("CreateHold", () => api.CreateHoldAsync(state.Request, $"assistant-{state.Nonce:N}", ct), ct);
        });
        return endpoints;
    }

    private static async Task<IResult> MessageAsync(AssistantMessage request, AssistantTokens tokens,
        IAssistantProvider provider, AssistantTools tools, IOptions<AssistantOptions> options,
        TimeProvider clock, ILogger<AssistantOptions> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2000)
            throw new AssistantProblem(422, "MESSAGE_LENGTH_INVALID");
        var history = tokens.History(tools.Subject, request.ConversationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        string reply;
        bool fallback = false;
        try
        {
            reply = await provider.ReplyAsync(history, request.Message, tools, deadline.Token);
            if (string.IsNullOrWhiteSpace(reply)) throw new AssistantProblem(503, "EMPTY_PROVIDER_RESPONSE");
            reply = reply[..Math.Min(reply.Length, 4000)];
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Assistant provider unavailable: {FailureType}", ex.GetType().Name);
            fallback = true;
            tools.Proposals.Clear();
            reply = "Trợ lý AI hiện chưa khả dụng. Bạn vẫn có thể tìm sự kiện và đặt vé tại trang Sự kiện. Chưa có vé nào được giữ từ cuộc trò chuyện này.";
        }
        var turns = history.Append(new ConversationTurn(request.Message, reply)).TakeLast(3).ToArray();
        while (turns.Sum(t => t.User.Length + t.Assistant.Length) > 6000 && turns.Length > 1) turns = turns[1..];
        string token = tokens.Protect("conversation", new ConversationState(tools.Subject, clock.GetUtcNow().AddMinutes(30), turns));
        return Results.Ok(new AssistantReply(reply, token, fallback, tools.Proposals.ToArray(), tools.ToolsUsed.ToArray()));
    }
}

