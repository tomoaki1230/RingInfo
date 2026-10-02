namespace RingInfo.Core.Settings;

/// <summary>設定ファイルに保存する機密情報（Client Secret・トークン）の暗号化</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    /// <exception cref="System.Security.Cryptography.CryptographicException">復号できない場合</exception>
    string Unprotect(string protectedText);
}
