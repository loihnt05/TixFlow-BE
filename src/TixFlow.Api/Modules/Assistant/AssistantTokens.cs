using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace TixFlow.Api.Modules.Assistant;

// Encrypted client-carried state keeps replicas stateless; purposes prevent token substitution.
public sealed class AssistantTokens(IDataProtectionProvider provider, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    public string Protect<T>(string purpose, T value) => provider.CreateProtector("TixFlow.Assistant.v1", purpose)
        .Protect(JsonSerializer.Serialize(value, Options));

    public T Read<T>(string purpose, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 48000) throw new AssistantProblem(400, "INVALID_ASSISTANT_TOKEN");
        try
        {
            return JsonSerializer.Deserialize<T>(provider.CreateProtector("TixFlow.Assistant.v1", purpose).Unprotect(token), Options)
                ?? throw new AssistantProblem(400, "INVALID_ASSISTANT_TOKEN");
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            throw new AssistantProblem(400, "INVALID_ASSISTANT_TOKEN");
        }
    }

    public ConversationTurn[] History(string subject, string? token)
    {
        if (string.IsNullOrEmpty(token)) return [];
        var state = Read<ConversationState>("conversation", token);
        Validate(subject, state.Subject, state.ExpiresAtUtc);
        return state.Turns;
    }

    public ConfirmationState Confirmation(string subject, string token)
    {
        var state = Read<ConfirmationState>("confirmation", token);
        Validate(subject, state.Subject, state.Preview.ConfirmBeforeUtc);
        return state;
    }

    private void Validate(string current, string owner, DateTimeOffset expiry)
    {
        if (!string.Equals(current, owner, StringComparison.Ordinal)) throw new AssistantProblem(403, "ASSISTANT_OWNER_MISMATCH");
        if (clock.GetUtcNow() >= expiry) throw new AssistantProblem(409, "ASSISTANT_TOKEN_EXPIRED");
    }
}
