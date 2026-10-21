namespace LyricClone.Infrastructure;

public static class DataPaths
{
    /// <summary>%AppData%\lyric-clone, or LYRIC_CLONE_DATA_DIR when set (tests, screenshots, portable use).</summary>
    public static string Root =>
        Environment.GetEnvironmentVariable("LYRIC_CLONE_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lyric-clone");
}
