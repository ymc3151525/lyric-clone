using System.Text.Json;
using System.Text.Json.Serialization;
using LyricClone.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LyricClone.Infrastructure.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    /// <summary>Applies <paramref name="edit"/> to the current settings, saves, and raises <see cref="Changed"/>.</summary>
    Task UpdateAsync(Action<AppSettings> edit, CancellationToken ct = default);

    Task ExportAsync(string path, CancellationToken ct = default);

    Task ImportAsync(string path, CancellationToken ct = default);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string path, ILogger<JsonSettingsStore> logger)
    {
        _path = path;
        _logger = logger;
        Current = Load();
    }

    public static string DefaultPath => Path.Combine(DataPaths.Root, "settings.json");

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public async Task UpdateAsync(Action<AppSettings> edit, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var copy = Clone(Current);
            edit(copy);
            copy.Normalize();
            await SaveAsync(copy, _path, ct);
            Current = copy;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, Current);
    }

    public Task ExportAsync(string path, CancellationToken ct = default) => SaveAsync(Current, path, ct);

    public async Task ImportAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var imported = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, ct)
            ?? throw new InvalidDataException("The file does not contain settings.");
        await UpdateAsync(s => CopyInto(imported, s), ct);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                loaded.Normalize();
                return loaded;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings file is unreadable; starting with defaults");
            TryBackupBroken();
        }

        return new AppSettings();
    }

    private void TryBackupBroken()
    {
        try
        {
            File.Copy(_path, _path + ".broken", overwrite: true);
        }
        catch
        {
        }
    }

    private static async Task SaveAsync(AppSettings settings, string path, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, ct);
        }

        File.Move(tmp, path, overwrite: true);
    }

    private static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Options), Options)!;

    private static void CopyInto(AppSettings from, AppSettings to)
    {
        var json = JsonSerializer.Serialize(from, Options);
        var fresh = JsonSerializer.Deserialize<AppSettings>(json, Options)!;
        foreach (var prop in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
        {
            prop.SetValue(to, prop.GetValue(fresh));
        }
    }
}
