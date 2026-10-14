using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace TixFlow.Infrastructure.Processing;

public sealed class QrTokens
{
    private readonly string directory;
    public string ActiveKeyId { get; }

    public QrTokens(IConfiguration? configuration = null)
    {
        directory = configuration?["Tickets:KeyDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "ticket-keys");
        ActiveKeyId = configuration?["Tickets:ActiveKeyId"] ?? "v1";
    }

    public string Create(Guid ticketId, string nonce, string keyId)
    {
        if (keyId.Length is < 1 or > 40 || keyId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Invalid QR key identifier.");
        string path = Path.Combine(directory, keyId + ".key");
        if (!File.Exists(path))
        {
            if (keyId != ActiveKeyId) throw new InvalidOperationException("The original QR signing key is unavailable.");
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, RandomNumberGenerator.GetBytes(32));
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                try { File.Move(temporary, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        byte[] key = File.ReadAllBytes(path);
        if (key.Length != 32) throw new InvalidOperationException("Invalid QR signing key.");
        byte[] proof = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"tixflow:ticket-qr:v1:{ticketId:N}:{nonce}"));
        return "tf1_" + Convert.ToHexString(proof);
    }
}
