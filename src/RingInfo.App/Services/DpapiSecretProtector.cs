using System.Security.Cryptography;
using System.Text;
using RingInfo.Core.Settings;

namespace RingInfo.App.Services;

/// <summary>Windows の DPAPI（現在のユーザーのみ復号可能）で機密情報を暗号化する</summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RingInfo.Settings.v1");

    public string Protect(string plainText)
        => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedText)
        => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser));
}
