using System.IO;
using System.Reflection;
using RingInfo.Core.Settings;

namespace RingInfo.App.Services;

/// <summary>ビルド時に組み込まれた Oura API アプリの情報（oura-client.json）を読み込む</summary>
internal static class BuiltInClient
{
    private const string ResourceName = "RingInfo.OuraClient.json";

    /// <summary>組み込まれていなければ null（利用者が自分でアプリ登録する方式になる）</summary>
    public static OuraClientInfo? Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return OuraClientInfo.TryParse(reader.ReadToEnd());
    }
}
