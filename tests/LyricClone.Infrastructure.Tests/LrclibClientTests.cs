using System.Net;
using System.Text;
using LyricClone.Core.Lyrics;
using LyricClone.Infrastructure.Lrclib;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricClone.Infrastructure.Tests;

// Fixtures are made-up sample records, not real LRCLIB data.
public class LrclibClientTests
{
    private const string Record = """
        {"id":7,"name":"Sample","trackName":"Sample","artistName":"Band","albumName":"Album","duration":200.4,
         "instrumental":false,"hasWordSync":false,"plainLyrics":"sample","syncedLyrics":"[00:01.00] sample","lyricsfile":"version: '1.0'"}
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (LrclibClient Client, FakeHandler Handler, List<TimeSpan> Delays) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? userAgent = null)
    {
        var handler = new FakeHandler(respond);
        var delays = new List<TimeSpan>();
        var http = new HttpClient(handler) { BaseAddress = new Uri(LrclibClient.BaseAddress) };
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        var client = new LrclibClient(http, NullLogger<LrclibClient>.Instance, (d, _) => { delays.Add(d); return Task.CompletedTask; });
        return (client, handler, delays);
    }

    [Fact]
    public async Task Get_builds_signature_query_and_maps_record()
    {
        var (client, handler, _) = Create(_ => Json(Record));

        var result = await client.GetAsync("Sample & Co", "Band", "Album", 200_400, CancellationToken.None);

        var url = handler.Requests.Single().RequestUri!.AbsoluteUri;
        Assert.Contains("/api/get?track_name=Sample%20%26%20Co&artist_name=Band&album_name=Album&duration=200", url);
        Assert.NotNull(result);
        Assert.Equal(("7", LyricsFormat.Lrc, 200_400L), (result!.SourceSongId, result.Format, result.DurationMs!.Value));
    }

    [Fact]
    public async Task Get_returns_null_on_404()
    {
        var (client, _, _) = Create(_ => Json("""{"statusCode":404,"name":"TrackNotFound","message":"x"}""", HttpStatusCode.NotFound));

        Assert.Null(await client.GetAsync("a", "b", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Waits_for_retry_after_on_429_then_succeeds()
    {
        int calls = 0;
        var (client, _, delays) = Create(_ =>
        {
            if (calls++ == 0)
            {
                var r = Json("""{"statusCode":429}""", HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return r;
            }

            return Json("[" + Record + "]");
        });

        var results = await client.SearchAsync("sample", CancellationToken.None);

        Assert.Single(results);
        var wait = delays.Max();
        Assert.InRange(wait, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.6));
    }

    [Fact]
    public async Task Non_json_error_bodies_throw_a_clean_exception()
    {
        var (client, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Failed to deserialize") });

        await Assert.ThrowsAsync<LrclibException>(() => client.SearchAsync("sample", CancellationToken.None));
    }

    [Fact]
    public async Task Instrumental_and_plain_only_records_map_to_plain()
    {
        var (client, _, _) = Create(_ => Json("""{"id":9,"trackName":"T","artistName":"A","albumName":"","duration":10,"instrumental":true,"hasWordSync":false,"plainLyrics":null,"syncedLyrics":null,"lyricsfile":""}"""));

        var r = await client.GetByIdAsync(9, CancellationToken.None);

        Assert.True(r!.IsInstrumental);
        Assert.Equal(LyricsFormat.Plain, r.Format);
        Assert.False(r.HasSyncedContent);
    }

    [Fact]
    public async Task Sends_the_http_client_user_agent_only_to_lrclib()
    {
        const string ua = "lyric-clone/0.1.0 (personal build; contact: set in settings)";
        var (client, handler, _) = Create(_ => Json(Record), ua);

        await client.GetAsync("Sample", "Band", "Album", 200_000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("lrclib.net", request.RequestUri!.Host);
        Assert.StartsWith("https://lrclib.net/", request.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("lyric-clone/0.1.0", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spaces_requests_at_least_300ms_apart()
    {
        var (client, handler, delays) = Create(_ => Json(Record));

        await client.GetAsync("Sample", "Band", null, null, CancellationToken.None);
        await client.GetAsync("Sample", "Band", null, null, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var gap = Assert.Single(delays);
        Assert.True(gap > TimeSpan.Zero, "second request must wait out the remainder of the 300ms gap");
        Assert.InRange(gap, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(300));
    }

    [Theory]
    [InlineData("123", 123)]
    [InlineData("https://lrclib.net/api/get/456", 456)]
    [InlineData("https://lrclib.net/lyrics/789/", 789)]
    public void TryParseId_accepts_ids_and_lrclib_links(string input, long expected)
    {
        Assert.True(LrclibClient.TryParseId(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("https://example.com/123")]
    public void TryParseId_rejects_other_input(string input) =>
        Assert.False(LrclibClient.TryParseId(input, out _));
}
