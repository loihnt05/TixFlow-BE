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
        byte[] key = ReadKey(keyId);
        byte[] proof = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"tixflow:ticket-qr:v1:{ticketId:N}:{nonce}"));
        return "tf1_" + Convert.ToHexString(proof);
    }

    // Store the pre-rendered QR encrypted: the image itself contains a bearer token.
    public byte[] RenderEncrypted(Guid ticketId, string token, string keyId)
    {
        byte[] png = QRCoder.PngByteQRCodeHelper.GetQRCode(token, QRCoder.QRCodeGenerator.ECCLevel.M, 5);
        byte[] result = new byte[12 + 16 + png.Length];
        RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        using var cipher = new AesGcm(ImageKey(keyId), 16);
        cipher.Encrypt(result.AsSpan(0, 12), png, result.AsSpan(28), result.AsSpan(12, 16), ticketId.ToByteArray());
        return result;
    }

    public byte[] ReadImage(Guid ticketId, byte[] encrypted, string keyId)
    {
        if (encrypted.Length < 28) throw new CryptographicException("Invalid QR image.");
        byte[] png = new byte[encrypted.Length - 28];
        using var cipher = new AesGcm(ImageKey(keyId), 16);
        cipher.Decrypt(encrypted.AsSpan(0, 12), encrypted.AsSpan(28), encrypted.AsSpan(12, 16), png, ticketId.ToByteArray());
        return png;
    }

    private byte[] ImageKey(string keyId) => HMACSHA256.HashData(ReadKey(keyId), Encoding.UTF8.GetBytes("tixflow:qr-image:v1"));

    private byte[] ReadKey(string keyId)
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
        return key;
    }
}
