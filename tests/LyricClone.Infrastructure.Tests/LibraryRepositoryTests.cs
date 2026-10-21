using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Data;

namespace LyricClone.Infrastructure.Tests;

// Fixtures are made-up sample text, not real song lyrics.
public sealed class LibraryRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-clone-tests", Guid.NewGuid().ToString("N"));
    private readonly LibraryRepository _repo;

    public LibraryRepositoryTests()
    {
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

    private static LyricsCandidate Online(string id = "42") =>
        new(LyricsSources.Lrclib, id, "Sample Song", "Sample Band", "Sample Album", 200_000, LyricsFormat.Lrc, "[00:01.00]sample", null, false, false);

    [Fact]
    public async Task Same_track_is_found_again_by_normalised_key_and_duration()
    {
        var a = await _repo.GetOrCreateTrackAsync("Sample Song (feat. X)", "Sample Band", "Album", 200_000, "app");
        var b = await _repo.GetOrCreateTrackAsync("sample song", "SAMPLE BAND", "album", 201_500, "app");
        var c = await _repo.GetOrCreateTrackAsync("sample song", "SAMPLE BAND", "album", 260_000, "app");

        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, c.Id);
    }

    [Fact]
    public async Task Same_key_keeps_one_row_when_player_duration_jumps_by_seconds()
    {
        var a = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "Album", 233_692, "app");
        var b = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "Album", 252_493, "app");

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(252_493, b.DurationMs);
    }

    [Fact]
    public async Task Saving_same_online_source_replaces_and_keeps_status_and_offset()
    {
        var t = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "", 200_000, "app");
        var v1 = await _repo.SaveCandidateAsync(t.Id, Online("1"), new LyricsFlags(true, false, false, false));
        await _repo.ConfirmAsync(v1.Id);
        await _repo.SetOffsetAsync(v1.Id, 250);

        var v2 = await _repo.SaveCandidateAsync(t.Id, Online("2"), new LyricsFlags(true, true, false, false));

        var versions = await _repo.GetVersionsAsync(t.Id);
        var only = Assert.Single(versions);
        Assert.Equal(v1.Id, v2.Id);
        Assert.Equal("2", only.SourceSongId);
        Assert.Equal(VersionStatus.Confirmed, only.Status);
        Assert.Equal(250, only.OffsetMs);
        Assert.True(only.HasWordSync);
    }

    [Fact]
    public async Task Saving_qq_cache_is_allowed_and_replaces_the_same_source()
    {
        var t = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "", 200_000, "app");
        var first = new LyricsCandidate(LyricsSources.QqCache, "a.qrc", "Sample Song", "Sample Band", "", 200_000, LyricsFormat.Qrc, "[0,100]one(0,100)", null, false, true);
        var v1 = await _repo.SaveCandidateAsync(t.Id, first, new LyricsFlags(true, true, false, false));
        var second = first with { SourceSongId = "b.qrc", Content = "[0,100]two(0,100)" };
        var v2 = await _repo.SaveCandidateAsync(t.Id, second, new LyricsFlags(true, true, false, false));

        var versions = (await _repo.GetVersionsAsync(t.Id)).Where(v => v.Source == LyricsSources.QqCache).ToList();
        var only = Assert.Single(versions);
        Assert.Equal(v1.Id, v2.Id);
        Assert.Equal("b.qrc", only.SourceSongId);
        Assert.Contains("two", only.Content);
    }

    [Fact]
    public async Task Pin_is_exclusive_and_unpin_restores_by_kind()
    {
        var t = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "", null, "app");
        var online = await _repo.SaveCandidateAsync(t.Id, Online(), default);
        var local = await _repo.AddUserVersionAsync(t.Id, "mine", LyricsFormat.Lrc, "[00:01.00]sample", null, new LyricsFlags(true, false, false, false));

        await _repo.PinAsync(online.Id);
        await _repo.PinAsync(local.Id);

        var after = (await _repo.GetVersionsAsync(t.Id)).ToDictionary(v => v.Id);
        Assert.Equal(VersionStatus.Confirmed, after[online.Id].Status);
        Assert.Equal(VersionStatus.Pinned, after[local.Id].Status);

        await _repo.UnpinAsync(local.Id);
        Assert.Equal(VersionStatus.Cached, (await _repo.GetVersionAsync(local.Id))!.Status);
    }

    [Fact]
    public async Task Local_versions_cannot_be_confirmed()
    {
        var t = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "", null, "app");
        var local = await _repo.AddUserVersionAsync(t.Id, "mine", LyricsFormat.Lrc, "[00:01.00]sample", null, default);

        await _repo.ConfirmAsync(local.Id);

        Assert.Equal(VersionStatus.Cached, (await _repo.GetVersionAsync(local.Id))!.Status);
    }

    [Fact]
    public async Task Query_searches_filters_and_sorts()
    {
        var a = await _repo.GetOrCreateTrackAsync("Alpha", "Band One", "", null, "app");
        var b = await _repo.GetOrCreateTrackAsync("Beta", "Band Two", "", null, "app");
        await _repo.AddUserVersionAsync(b.Id, "mine", LyricsFormat.Lrc, "[00:01.00]sample", null, default);

        Assert.Equal([a.Id], (await _repo.QueryTracksAsync(new TrackQuery(Search: "one"))).Select(t => t.Id));
        Assert.Equal([b.Id], (await _repo.QueryTracksAsync(new TrackQuery(HasLocal: true))).Select(t => t.Id));
        Assert.Equal([a.Id, b.Id], (await _repo.QueryTracksAsync(new TrackQuery(Sort: TrackSort.Title))).Select(t => t.Id));
        Assert.Empty(await _repo.QueryTracksAsync(new TrackQuery(Search: "100%")));
    }

    [Fact]
    public async Task Misses_are_remembered_and_cleared_by_a_save()
    {
        var t = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "", null, "app");
        await _repo.RecordMissAsync(t.Id, LyricsSources.Lrclib);
        Assert.Contains(LyricsSources.Lrclib, await _repo.RecentMissesAsync(t.Id, TimeSpan.FromHours(1)));

        await _repo.SaveCandidateAsync(t.Id, Online(), default);
        Assert.Empty(await _repo.RecentMissesAsync(t.Id, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task Deleting_a_track_removes_its_versions()
    {
        var t = await _repo.GetOrCreateTrackAsync("Sample Song", "Sample Band", "", null, "app");
        var v = await _repo.SaveCandidateAsync(t.Id, Online(), default);

        await _repo.DeleteTrackAsync(t.Id);

        Assert.Null(await _repo.GetVersionAsync(v.Id));
    }
}
