using LyricClone.App.Views;
using LyricClone.Core.Playback;
using LyricClone.Infrastructure.Settings;
using LyricClone.Infrastructure.Smtc;

namespace LyricClone.App.Services;

/// <summary>Decides whether to show S16 and shows it once per run. The delay gives players time to register their sessions.</summary>
public sealed class PlayerHintService
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(10);

    private readonly NowPlayingService _nowPlaying;
    private readonly ISettingsStore _settings;
    private readonly Action _openAppsSettings;
    private bool _shown;

    public PlayerHintService(NowPlayingService nowPlaying, ISettingsStore settings, Action openAppsSettings)
    {
        _nowPlaying = nowPlaying;
        _settings = settings;
        _openAppsSettings = openAppsSettings;
    }

    public PlayerHintKind Evaluate() => PlayerHint.Decide(
        _nowPlaying.Availability == SmtcAvailability.Unavailable,
        _nowPlaying.SessionAppIds.Count,
        _nowPlaying.Snapshot is not null,
        _settings.Current.ShowNoPlayerHint);

    public async Task CheckAfterAsync(TimeSpan delay)
    {
        if (Evaluate() == PlayerHintKind.SmtcUnavailable)
        {
            ShowIfNeeded();
            return;
        }

        await Task.Delay(delay);
        ShowIfNeeded();
    }

    /// <summary>Shows the dialog when needed and not shown yet this run. Returns the window, or null.</summary>
    public PlayerHintDialog? ShowIfNeeded()
    {
        var kind = Evaluate();
        if (_shown || kind == PlayerHintKind.None)
        {
            return null;
        }

        _shown = true;
        var ignored = _nowPlaying.SessionAppIds.Select(SettingsWindow.FriendlyName).Distinct().ToList();
        var dialog = new PlayerHintDialog(kind, ignored);
        dialog.Closed += async (_, _) =>
        {
            if (dialog.DontShowAgain)
            {
                await _settings.UpdateAsync(s => s.ShowNoPlayerHint = false);
            }

            if (dialog.OpenAppsRequested)
            {
                _openAppsSettings();
            }
        };
        dialog.Show();
        return dialog;
    }
}
