using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TixFlow.Application.WaitingRoom;

namespace TixFlow.Api.Modules.WaitingRoom;

public sealed record AdmissionClaim(string Subject, Guid EventSessionId, Guid QueueId, Guid GrantId,
    Guid Nonce, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed class AdmissionTokens(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly IDataProtector protector = provider.CreateProtector("TixFlow.WaitingRoom.Admission.v1");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Issue(QueueEntrySnapshot entry, string subject)
    {
        if (entry.Status != "Admitted" || entry.GrantId is null)
            throw new InvalidOperationException("Only an admitted entry can receive a token.");
        var claim = new AdmissionClaim(subject, entry.EventSessionId, entry.QueueId, entry.GrantId.Value,
            Guid.NewGuid(), clock.GetUtcNow(), DateTimeOffset.FromUnixTimeMilliseconds(entry.ExpiresAtUnixMs));
        return protector.Protect(JsonSerializer.Serialize(claim, Options));
    }

    public AdmissionClaim? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
        try { return JsonSerializer.Deserialize<AdmissionClaim>(protector.Unprotect(token), Options); }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        { return null; }
    }
}
