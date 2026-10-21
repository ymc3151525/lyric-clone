using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LyricClone.App.Services;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Playback;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lrclib;
using LyricClone.Infrastructure.Lyrics;
using LyricClone.Infrastructure.Settings;
using LyricClone.App.Interop;
using LyricClone.Core.Views;
using LyricClone.Infrastructure.Smtc;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricClone.Flows.Tests;

/// <summary>One STA thread with a running dispatcher and the app's resource dictionaries, shared by all UI tests.</summary>
public static class Sta
{
    private static readonly Lazy<Dispatcher> Host = new(Start);

    public static void Run(Func<Task> test)
    {
        var dispatcher = Host.Value;
        var task = dispatcher.InvokeAsync(test).Task.Unwrap();
        if (!task.Wait(TimeSpan.FromSeconds(30)))
        {
            // A dialog left open would keep the shared dispatcher in its nested loop and hang every later test.
            dispatcher.Invoke(() =>
            {
                foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
                {
                    w.Close();
                }
            });
            throw new TimeoutException("UI test did not finish in 30 s");
        }

        task.GetAwaiter().GetResult();
    }

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new LyricClone.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "flow-tests-ui",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(20);
        }
    }

    public static void Click(Window window, string buttonName)
    {
        var button = (Button)window.FindName(buttonName)!;
        Assert.True(button.IsVisible && button.IsEnabled, $"{buttonName} should be visible and enabled");
        UiDriver.Press(button);
    }

    public static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name)!;
}

public sealed class FakeSessions : ISessionWatcher
{
    private readonly List<string> _apps = [];

    public SmtcAvailability Availability { get; set; } = SmtcAvailability.Available;

    public MediaSnapshot? Current { get; private set; }

    public IReadOnlyList<string> SessionAppIds => _apps;

    public Func<string, bool> IsAppAllowed { get; set; } = _ => true;

    public event EventHandler<MediaSnapshot?>? SnapshotChanged;

    public int PlayPauseCalls { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Simulates a player reporting a track; respects the app filter like the real watcher.</summary>
    public void Play(string appId, string title, string artist, string album = "", long durationMs = 200_000, long positionMs = 0)
    {
        if (!_apps.Contains(appId))
        {
            _apps.Add(appId);
        }

        Current = IsAppAllowed(appId)
            ? new MediaSnapshot(appId, title, artist, album, durationMs, positionMs, DateTimeOffset.UtcNow, PlaybackStatus.Playing, 1.0, true)
            : null;
        SnapshotChanged?.Invoke(this, Current);
    }

    public void Stop()
    {
        Current = null;
        SnapshotChanged?.Invoke(this, null);
    }

    public Task<bool> TryPlayPauseAsync()
    {
        PlayPauseCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> TryNextAsync() => Task.FromResult(true);

    public Task<bool> TryPreviousAsync() => Task.FromResult(true);

    public byte[]? Thumbnail { get; set; }

    public Task<byte[]?> TryGetThumbnailAsync() => Task.FromResult(Thumbnail);

    /// <summary>A session that exists but is filtered out, e.g. a browser.</summary>
    public void AddIgnoredSession(string appId)
    {
        if (!_apps.Contains(appId))
        {
            _apps.Add(appId);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeTaskbar : ITaskbarQuery
{
    public TaskbarSnapshot Current { get; set; } =
        new(TaskbarEdge.Bottom, 0, 1040, 1920, 40, AutoHide: false, FullscreenApp: false, QueryFailed: false);

    public TaskbarSnapshot Read() => Current;
}

/// <summary>In-memory LRCLIB. Records every call so tests can assert no extra requests were made.</summary>
public sealed class FakeLrclib : ILrclibClient
{
    public ConcurrentDictionary<long, LyricsCandidate> Records { get; } = new();

    public ConcurrentQueue<string> Calls { get; } = new();

    public Exception? FailWith { get; set; }

    public Exception? FailGetWith { get; set; }

    public LyricsCandidate Add(long id, string title, string artist, string lrc, long durationMs = 200_000, bool instrumental = false)
    {
        var c = new LyricsCandidate(LyricsSources.Lrclib, id.ToString(), title, artist, "", durationMs,
            instrumental ? LyricsFormat.Plain : LyricsFormat.Lrc, instrumental ? null : lrc, null, instrumental, false);
        Records[id] = c;
        return c;
    }

    public Task<LyricsCandidate?> GetAsync(string title, string artist, string? album, long? durationMs, CancellationToken ct)
    {
        Calls.Enqueue($"get {title}|{artist}");
        ThrowIfFailing();
        if (FailGetWith is not null)
        {
            throw FailGetWith;
        }

        return Task.FromResult(Records.Values.FirstOrDefault(r =>
            r.Title == title && r.Artist == artist && (durationMs is null || r.DurationMs is null || Math.Abs(r.DurationMs.Value - durationMs.Value) <= 2000)));
    }

    public Task<LyricsCandidate?> GetByIdAsync(long id, CancellationToken ct)
    {
        Calls.Enqueue($"id {id}");
        ThrowIfFailing();
        return Task.FromResult(Records.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        Calls.Enqueue($"search {query}");
        ThrowIfFailing();
        IReadOnlyList<LyricsCandidate> found = Records.Values
            .Where(r => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => r.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.SourceSongId)
            .ToList();
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string title, string? artist, string? album, CancellationToken ct) =>
        SearchAsync(title, ct);

    private void ThrowIfFailing()
    {
        if (FailWith is { } ex)
        {
            throw ex;
        }
    }
}

/// <summary>Real repository, resolver, parser and settings store over a temp folder; fake SMTC and LRCLIB.</summary>
public sealed class FlowEnv : IDisposable
{
    public FlowEnv()
    {
        Dir = Path.Combine(Path.GetTempPath(), "lyric-clone-flows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Repository = new LibraryRepository(new LibraryDatabase(Path.Combine(Dir, "library.db")));
        Settings = new JsonSettingsStore(Path.Combine(Dir, "settings.json"), NullLogger<JsonSettingsStore>.Instance);
        MusicDir = Path.Combine(Dir, "music");
        Directory.CreateDirectory(MusicDir);
        Embedded = new EmbeddedLyricsProvider(() => Settings.Current.MusicFolder ?? MusicDir);
        Resolver = new LyricsResolver(Repository, Lrclib, new LocalFileProvider(), Parser, NullLogger<LyricsResolver>.Instance, Embedded, new PlayerCacheProvider(_ => []));
    }

    public string Dir { get; }

    /// <summary>Stands in for the user's Music folder so tests never scan the real one.</summary>
    public string MusicDir { get; }

    public EmbeddedLyricsProvider Embedded { get; }

    public FakeLrclib Lrclib { get; } = new();

    public FakeSessions Sessions { get; } = new();

    public ILyricsParser Parser { get; } = new HelperLyricsParser();

    public LibraryRepository Repository { get; }

    public JsonSettingsStore Settings { get; }

    public LyricsResolver Resolver { get; }

    public NowPlayingService? NowPlaying { get; private set; }

    /// <summary>Must be called on the UI thread: the service captures the current dispatcher.</summary>
    public async Task<NowPlayingService> StartAsync()
    {
        NowPlaying = new NowPlayingService(Sessions, Resolver, Settings, NullLogger<NowPlayingService>.Instance);
        await NowPlaying.StartAsync(CancellationToken.None);
        return NowPlaying;
    }

    public ManagerContext Context() =>
        new(Repository, Resolver, Parser, Lrclib, Settings, NowPlaying ?? throw new InvalidOperationException("StartAsync first"));

    public string LocalFolder(params (string FileName, string Content)[] files)
    {
        var folder = Path.Combine(Dir, "local-lyrics");
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(folder, name), content);
        }

        return folder;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Made-up lyrics only. Never real song text.</summary>
public static class Samples
{
    public const string LrcA = """
        [00:01.00]示例 A 的第一行
        [00:05.00]示例 A 的第二行
        [00:09.00]示例 A 的第三行
        """;

    public const string LrcB = """
        [00:02.00]Made-up B line one
        [00:06.00]Made-up B line two
        """;

    public const string LrcMine = """
        [00:00.50]我自己写的 第一行
        [00:04.50]我自己写的 第二行
        """;

    public const string LrcTranslated = """
        [00:01.00]Hello there friends
        [00:01.00]你好朋友们
        [00:05.00]Another English line
        [00:05.00]另一行中文
        """;
}
