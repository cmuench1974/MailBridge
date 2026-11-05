using System.Security.Cryptography;
using System.Text;

namespace MailBridge.Core.Security.OAuth;

/// <summary>Proof Key for Code Exchange (RFC 7636, S256) and random state values.</summary>
public static class Pkce
{
    /// <summary>43-character code verifier from 32 random bytes.</summary>
    public static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CreateCodeChallenge(string codeVerifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    /// <summary>Unguessable value that ties the browser redirect to this sign-in attempt.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
