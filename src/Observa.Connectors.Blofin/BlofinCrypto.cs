using System.Security.Cryptography;
using System.Text;

namespace Observa.Connectors.Blofin;

/// <summary>
/// BloFin's signing scheme for REST auth:
///   prehash   = requestPath + method + timestamp(ms) + nonce + body
///   signature = base64( lowerhex( HMAC-SHA256(secretKey, prehash) ) )
/// </summary>
internal static class BlofinCrypto
{
    public static string CreateSignature(
        string requestPath,
        string method,
        string timestamp,
        string nonce,
        string body,
        string secretKey)
    {
        var prehash = $"{requestPath}{method}{timestamp}{nonce}{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(hex));
    }
}
