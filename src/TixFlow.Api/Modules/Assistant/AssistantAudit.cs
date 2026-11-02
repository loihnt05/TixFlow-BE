using System.Text.Json;
using TixFlow.Api.Authentication;
using TixFlow.Domain.Common;
using TixFlow.Domain.Identity;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Api.Modules.Assistant;

public interface IAssistantAudit
{
    Task WriteAsync(string tool, string outcome, CancellationToken ct);
}

// Separate infrastructure boundary: tools and the provider never receive a DbContext.
public sealed class AssistantAudit(IServiceScopeFactory scopes, IHttpContextAccessor accessor) : IAssistantAudit
{
    public async Task WriteAsync(string tool, string outcome, CancellationToken ct)
    {
        var context = accessor.HttpContext!;
        var user = (User)context.Items[LocalUserMiddleware.UserKey]!;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TixFlowDbContext>();
        db.AuditLogs.Add(new AuditLog(user.Id, "assistant." + tool, "AssistantTool", context.TraceIdentifier,
            JsonSerializer.Serialize(new { outcome }), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(ct);
    }
}
