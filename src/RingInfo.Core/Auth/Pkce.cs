using System.Security.Cryptography;
using System.Text;

namespace RingInfo.Core.Auth;

/// <summary>PKCE（RFC 7636）と state 用の乱数文字列を生成する</summary>
public static class Pkce
{
    /// <summary>code_verifier を生成する（64 文字の base64url 文字列）</summary>
    public static string CreateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(48));

    /// <summary>code_verifier から S256 の code_challenge を求める</summary>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>CSRF 対策の state を生成する</summary>
    public static string CreateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    internal static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
