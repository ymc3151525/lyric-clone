using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Playback;
using LyricClone.Infrastructure.Data;
using LyricClone.Infrastructure.Lrclib;
using LyricClone.Infrastructure.Lyrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricClone.Infrastructure.Tests;

public sealed class LyricsResolverCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-clone-resolve-" + Guid.NewGuid().ToString("N"));
    private readonly string _cacheDir;
    private readonly LibraryRepository _repo;

    public LyricsResolverCacheTests()
    {
        Directory.CreateDirectory(_dir);
        _cacheDir = Path.Combine(_dir, "qq");
        Directory.CreateDirectory(_cacheDir);
        _repo = new LibraryRepository(new LibraryDatabase(Path.Combine(_dir, "library.db")));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Player_cache_miss_is_not_remembered_so_a_later_file_is_picked_up()
    {
        var resolver = Create(cacheRetryDelays: []);
        var snap = Snap();
        var order = LyricsSources.DefaultOrder;

        var first = await resolver.ResolveAsync(snap, order, null, CancellationToken.None);
        Assert.Equal(ResolveState.NotFound, first.State);
        Assert.DoesNotContain(LyricsSources.QqCache, await _repo.RecentMissesAsync(first.Track!.Id, TimeSpan.FromHours(12)));

        File.WriteAllText(Path.Combine(_cacheDir, "虚构乐队 - 示例曲目 - 200 - 专辑_qm.qrc"), HelperLyricsParserTests.Qrc);
        var second = await resolver.ResolveAsync(snap, order, null, CancellationToken.None);
        Assert.Equal(ResolveState.Found, second.State);
        Assert.Equal(LyricsSources.QqCache, second.Version?.Source);
    }

    [Fact]
    public async Task Player_cache_is_retried_after_the_player_writes_the_file()
    {
        var resolver = Create(cacheRetryDelays: [TimeSpan.FromMilliseconds(60)]);
        var snap = Snap();
        var order = LyricsSources.DefaultOrder;

        var pending = resolver.ResolveAsync(snap, order, null, CancellationToken.None);
        await Task.Delay(30);
        File.WriteAllText(Path.Combine(_cacheDir, "虚构乐队 - 示例曲目 - 200 - 专辑_qm.qrc"), HelperLyricsParserTests.Qrc);

        var result = await pending;
        Assert.Equal(ResolveState.Found, result.State);
        Assert.Equal(LyricsSources.QqCache, result.Version?.Source);
    }

    private LyricsResolver Create(TimeSpan[] cacheRetryDelays) =>
        new(_repo, new EmptyLrclib(), new LocalFileProvider(), new HelperLyricsParser(),
            NullLogger<LyricsResolver>.Instance, playerCache: new PlayerCacheProvider(_ => [_cacheDir]),
            cacheRetryDelays: cacheRetryDelays);

    private static MediaSnapshot Snap() => new(
        "qqmusic.exe", "示例曲目", "虚构乐队", "专辑", 200_000, 1_200,
        DateTimeOffset.UtcNow, PlaybackStatus.Playing, 1, true);

    private sealed class EmptyLrclib : ILrclibClient
    {
        public Task<LyricsCandidate?> GetAsync(string title, string artist, string? album, long? durationMs, CancellationToken ct) =>
            Task.FromResult<LyricsCandidate?>(null);

        public Task<LyricsCandidate?> GetByIdAsync(long id, CancellationToken ct) =>
            Task.FromResult<LyricsCandidate?>(null);

        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);

        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string title, string? artist, string? album, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
    }
}
