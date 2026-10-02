namespace RingInfo.App.Services;

/// <summary>
/// 画面に表示するファイルの場所を、利用者に依存しない書き方（%APPDATA% など）にする。
/// 実際のパスをそのまま出すと、ユーザー名（C:\Users\ユーザー名\…）が画面やスクリーンショットに写るため。
/// </summary>
internal static class PathDisplay
{
    /// <summary>置き換える場所（長いものから順に判定する）</summary>
    private static (string Folder, string Name)[] Folders =>
    [
        (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
        (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
        (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
    ];

    public static string ToDisplay(string? path) => ToDisplay(path, Folders);

    internal static string ToDisplay(string? path, IEnumerable<(string Folder, string Name)> folders)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        foreach (var (folder, name) in folders.Where(f => !string.IsNullOrEmpty(f.Folder)).OrderByDescending(f => f.Folder.Length))
        {
            var prefix = folder.TrimEnd('\\', '/');
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            if (path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return name + path[prefix.Length..];
            }
        }

        return path;
    }
}
