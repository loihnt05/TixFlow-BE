using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Isolated load-test identity provider. No authentication bypass in the application.
// No host port, private key export, or token endpoint. Tokens go to a private mount.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
using var rsa = RSA.Create(2048);
const string issuer = "http://issuer:8080";
string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
string Json64(object value) => B64(JsonSerializer.SerializeToUtf8Bytes(value));
string Token(int i, string role = "Customer", string audience = "tixflow-api")
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string body = Json64(new { alg = "RS256", typ = "JWT", kid = "load-only" }) + "." + Json64(new {
        iss = issuer, aud = audience, sub = $"baseline-v1-{i}", email = $"baseline-v1-{i}@example.test",
        preferred_username = $"baseline-v1-{i}@example.test", name = "Load Test", roles = new[] { role },
        azp = "tixflow-load", scope = "payment:callback", iat = now, nbf = now - 30, exp = now + 14400
    });
    return body + "." + B64(rsa.SignData(Encoding.ASCII.GetBytes(body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
Directory.CreateDirectory("/state");
File.WriteAllText("/state/tokens.tmp", JsonSerializer.Serialize(new {
    customers = Enumerable.Range(1, 10000).Select(i => Token(i)).ToArray(),
    callback = Token(10001, "Customer", "tixflow-mock-callback")
}));
File.Move("/state/tokens.tmp", "/state/tokens.json", true);
var key = rsa.ExportParameters(false);
app.MapGet("/.well-known/openid-configuration", () => new { issuer, jwks_uri = issuer + "/keys" });
app.MapGet("/keys", () => new { keys = new[] { new { kty = "RSA", use = "sig", kid = "load-only", alg = "RS256", n = B64(key.Modulus!), e = B64(key.Exponent!) } } });
app.MapGet("/health", () => "ready");
app.Run();
