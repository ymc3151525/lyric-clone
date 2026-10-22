using LyricClone.Core.Settings;
using LyricClone.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricClone.Infrastructure.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-clone-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Update_writes_only_under_the_given_path_and_raises_changed()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        AppSettings? seen = null;
        store.Changed += (_, s) => seen = s;

        await store.UpdateAsync(s => s.DeviceDelayMs = 250);

        Assert.Equal(250, store.Current.DeviceDelayMs);
        Assert.Equal(250, seen?.DeviceDelayMs);
        Assert.True(File.Exists(path));
        Assert.Contains("250", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.StartsWith(Path.GetFullPath(_dir), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unreadable_file_is_backed_up_and_defaults_are_used()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{not-json");

        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        Assert.Equal(0, store.Current.DeviceDelayMs);
        Assert.True(File.Exists(path + ".broken"));
        Assert.Contains("{not-json", File.ReadAllText(path + ".broken"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_and_import_round_trip()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        await store.UpdateAsync(s =>
        {
            s.DeviceDelayMs = 80;
            s.ShowChineseTranslation = false;
        });

        var exported = Path.Combine(_dir, "export.json");
        await store.ExportAsync(exported);
        await store.UpdateAsync(s => s.DeviceDelayMs = 0);

        var other = new JsonSettingsStore(Path.Combine(_dir, "other.json"), NullLogger<JsonSettingsStore>.Instance);
        await other.ImportAsync(exported);

        Assert.Equal(80, other.Current.DeviceDelayMs);
        Assert.False(other.Current.ShowChineseTranslation);
    }

    [Fact]
    public void Data_dir_override_points_settings_at_the_temp_folder()
    {
        var prev = Environment.GetEnvironmentVariable("LYRIC_CLONE_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("LYRIC_CLONE_DATA_DIR", _dir);
            Assert.Equal(Path.Combine(_dir, "settings.json"), JsonSettingsStore.DefaultPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYRIC_CLONE_DATA_DIR", prev);
        }
    }
}
