using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using Microsoft.Extensions.Logging;

namespace LyricClone.Infrastructure.Lrclib;

public interface ILrclibClient
{
    /// <summary>Best match for a track signature, or null when LRCLIB has no record (404).</summary>
    Task<LyricsCandidate?> GetAsync(string title, string artist, string? album, long? durationMs, CancellationToken ct);

    Task<LyricsCandidate?> GetByIdAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string title, string? artist, string? album, CancellationToken ct);
}

public sealed class LrclibException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Client for the public LRCLIB API (https://lrclib.net/docs). Requests go out one at a time with a
/// short gap, and 429 / 503 responses wait for Retry-After plus jitter, as the API asks.
/// </summary>
public sealed class LrclibClient : ILrclibClient
{
    public const string BaseAddress = "https://lrclib.net";
    private static readonly TimeSpan GapBetweenRequests = TimeSpan.FromMilliseconds(300);
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly ILogger<LrclibClient> _logger;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public LrclibClient(HttpClient http, ILogger<LrclibClient> logger, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _logger = logger;
        _delay = delay ?? Task.Delay;
        _http.BaseAddress ??= new Uri(BaseAddress);
    }

    public async Task<LyricsCandidate?> GetAsync(string title, string artist, string? album, long? durationMs, CancellationToken ct)
    {
        var q = new List<string>
        {
            "track_name=" + Uri.EscapeDataString(title),
            "artist_name=" + Uri.EscapeDataString(artist),
        };
        if (!string.IsNullOrWhiteSpace(album))
        {
            q.Add("album_name=" + Uri.EscapeDataString(album));
        }

        if (durationMs is > 0)
        {
            long seconds = Math.Clamp((long)Math.Round(durationMs.Value / 1000.0), 1, 3600);
            q.Add("duration=" + seconds.ToString(CultureInfo.InvariantCulture));
        }

        var record = await SendAsync<LrclibRecord>("/api/get?" + string.Join('&', q), allowNotFound: true, ct);
        return record is null ? null : ToCandidate(record);
    }

    public async Task<LyricsCandidate?> GetByIdAsync(long id, CancellationToken ct)
    {
        var record = await SendAsync<LrclibRecord>("/api/get/" + id.ToString(CultureInfo.InvariantCulture), allowNotFound: true, ct);
        return record is null ? null : ToCandidate(record);
    }

    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var records = await SendAsync<List<LrclibRecord>>("/api/search?q=" + Uri.EscapeDataString(query.Trim()), allowNotFound: false, ct);
        return records?.Select(ToCandidate).ToArray() ?? [];
    }

    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string title, string? artist, string? album, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var q = new List<string> { "track_name=" + Uri.EscapeDataString(title.Trim()) };
        if (!string.IsNullOrWhiteSpace(artist))
        {
            q.Add("artist_name=" + Uri.EscapeDataString(artist.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(album))
        {
            q.Add("album_name=" + Uri.EscapeDataString(album.Trim()));
        }

        var records = await SendAsync<List<LrclibRecord>>("/api/search?" + string.Join('&', q), allowNotFound: false, ct);
        return records?.Select(ToCandidate).ToArray() ?? [];
    }

    /// <summary>Accepts a bare id or an lrclib.net URL that ends in an id.</summary>
    public static bool TryParseId(string input, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        if (long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return id > 0;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)
            || !uri.Host.EndsWith("lrclib.net", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var last = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault();
        return long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    internal static LyricsCandidate ToCandidate(LrclibRecord r)
    {
        bool synced = !string.IsNullOrWhiteSpace(r.SyncedLyrics);
        return new LyricsCandidate(
            LyricsSources.Lrclib,
            r.Id.ToString(CultureInfo.InvariantCulture),
            r.TrackName ?? r.Name ?? "",
            r.ArtistName ?? "",
            r.AlbumName ?? "",
            r.Duration is > 0 ? (long)Math.Round(r.Duration.Value * 1000) : null,
            synced ? LyricsFormat.Lrc : LyricsFormat.Plain,
            synced ? r.SyncedLyrics : r.PlainLyrics,
            null,
            r.Instrumental,
            r.HasWordSync);
    }

    private async Task<T?> SendAsync<T>(string path, bool allowNotFound, CancellationToken ct) where T : class
    {
        await _queue.WaitAsync(ct);
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                var wait = _lastRequestAt + GapBetweenRequests - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await _delay(wait, ct);
                }

                using var response = await _http.GetAsync(path, ct);
                _lastRequestAt = DateTimeOffset.UtcNow;

                if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
                {
                    return null;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt >= MaxAttempts)
                    {
                        throw new LrclibException("LRCLIB is busy, try again later.", response.StatusCode);
                    }

                    var retryAfter = RetryAfter(response) ?? TimeSpan.FromSeconds(attempt * 2);
                    var jitter = TimeSpan.FromMilliseconds(retryAfter.TotalMilliseconds * Random.Shared.NextDouble() * 0.3);
                    _logger.LogInformation("LRCLIB returned {Status}; waiting {Delay}", (int)response.StatusCode, retryAfter + jitter);
                    await _delay(retryAfter + jitter, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new LrclibException($"LRCLIB request failed with status {(int)response.StatusCode}.", response.StatusCode);
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new LrclibException("LRCLIB returned an unexpected response.", response.StatusCode);
                }

                return await response.Content.ReadFromJsonAsync<T>(ct);
            }
        }
        finally
        {
            _queue.Release();
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var ra = response.Headers.RetryAfter;
        if (ra?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (ra?.Date is DateTimeOffset date)
        {
            var d = date - DateTimeOffset.UtcNow;
            return d > TimeSpan.Zero ? d : TimeSpan.Zero;
        }

        return null;
    }
}

internal sealed class LrclibRecord
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("trackName")] public string? TrackName { get; set; }
    [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
    [JsonPropertyName("albumName")] public string? AlbumName { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
    [JsonPropertyName("hasWordSync")] public bool HasWordSync { get; set; }
    [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
    [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    [JsonPropertyName("lyricsfile")] public string? Lyricsfile { get; set; }
}
