using System.Text.Json;

namespace LyricClone.Core.Update;

public enum UpdateCheckStatus
{
    NoFeed,
    Invalid,
    UpToDate,
    Available,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? Latest,
    string? Link,
    string? Notes);

/// <summary>Parses a tiny JSON feed <c>{"version","url","notes"}</c>. There is no default published feed.</summary>
public static class UpdateFeed
{
    public static UpdateCheckResult Parse(string? json, Version current)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(UpdateCheckStatus.Invalid, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var versionEl)
                || versionEl.ValueKind != JsonValueKind.String
                || !Version.TryParse(versionEl.GetString(), out var latest))
            {
                return new(UpdateCheckStatus.Invalid, null, null, null);
            }

            string? link = root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
                ? urlEl.GetString()
                : null;
            string? notes = root.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.String
                ? notesEl.GetString()
                : null;
            var status = latest > current ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate;
            return new(status, latest, string.IsNullOrWhiteSpace(link) ? null : link, notes);
        }
        catch (JsonException)
        {
            return new(UpdateCheckStatus.Invalid, null, null, null);
        }
    }
}
