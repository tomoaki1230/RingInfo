using System.Diagnostics;

namespace RingInfo.App.Services;

/// <summary>既定のブラウザやエクスプローラーで開く</summary>
internal static class ShellLauncher
{
    public static void Open(string target)
        => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public static void Open(Uri uri) => Open(uri.AbsoluteUri);
}
