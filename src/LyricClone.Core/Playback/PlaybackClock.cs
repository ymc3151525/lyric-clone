namespace LyricClone.Core.Playback;

/// <summary>
/// Turns sparse session reports into a position you can read every frame.
/// Effective position = reported + elapsed × rate − session delay − device delay.
/// Positive delays show lyrics later, matching the sign of a version offset. Players without a timeline use an internal timer started at track change.
/// </summary>
public sealed class PlaybackClock
{
    /// <summary>Corrections smaller than this are ignored as SMTC noise (browsers / Apple Music).</summary>
    public const long NoiseThresholdMs = 80;

    /// <summary>Backward jumps smaller than this are treated as reporting jitter, not a seek.</summary>
    public const long JitterThresholdMs = 1200;

    /// <summary>Forward corrections smaller than this are eased in over <see cref="EaseDurationMs"/>.</summary>
    public const long EaseThresholdMs = 1200;

    public const long EaseDurationMs = 700;

    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    private MediaSnapshot? _snapshot;
    private long _anchorPositionMs;
    private DateTimeOffset _anchorAt;
    private bool _running;
    private double _rate = 1;

    private long _easeFromMs;
    private DateTimeOffset _easeStartedAt;
    private bool _easing;
    private long _durationMs;

    public PlaybackClock(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public long SessionDelayMs { get; set; }

    public long DeviceDelayMs { get; set; }

    public bool SmoothingEnabled { get; set; } = true;

    public bool IsRunning
    {
        get { lock (_gate) { return _running; } }
    }

    public void Update(MediaSnapshot snapshot)
    {
        lock (_gate)
        {
            var now = _now();
            bool newTrack = !snapshot.IsSameTrack(_snapshot);
            long before = _snapshot is null ? 0 : RawPosition(now);

            _running = snapshot.Status == PlaybackStatus.Playing;
            _rate = snapshot.PlaybackRate > 0 ? snapshot.PlaybackRate : 1;

            if (!snapshot.HasTimeline)
            {
                if (newTrack)
                {
                    SetAnchor(0, now);
                }
                else
                {
                    SetAnchor(before, now);
                }

                _snapshot = snapshot;
                return;
            }

            long reported = snapshot.PositionMs;
            if (snapshot.Status == PlaybackStatus.Playing)
            {
                var age = now - snapshot.PositionUpdatedAt;
                if (age > TimeSpan.Zero && age < TimeSpan.FromHours(6))
                {
                    reported += (long)(age.TotalMilliseconds * _rate);
                }
            }

            if (!newTrack && SmoothingEnabled && _snapshot is not null && snapshot.Status == PlaybackStatus.Playing)
            {
                long delta = reported - before;
                if (delta != 0 && Math.Abs(delta) < NoiseThresholdMs)
                {
                    _snapshot = snapshot;
                    return;
                }

                if (delta < 0 && -delta < JitterThresholdMs)
                {
                    _snapshot = snapshot;
                    return;
                }

                if (delta > 0 && delta < EaseThresholdMs)
                {
                    _easeFromMs = before;
                    _easeStartedAt = now;
                    _easing = true;
                }
                else
                {
                    _easing = false;
                }
            }
            else
            {
                _easing = false;
            }

            SetAnchor(reported, now);
            _snapshot = snapshot;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _snapshot = null;
            _running = false;
            _easing = false;
            SetAnchor(0, _now());
        }
    }

    /// <summary>Position used for lyrics, with delays applied. Clamped at 0.</summary>
    public long PositionMs
    {
        get
        {
            lock (_gate)
            {
                long p = SmoothedPosition(_now()) - SessionDelayMs - DeviceDelayMs;
                return Math.Max(0, p);
            }
        }
    }

    private long SmoothedPosition(DateTimeOffset now)
    {
        long target = RawPosition(now);
        if (!_easing)
        {
            return target;
        }

        double t = (now - _easeStartedAt).TotalMilliseconds / EaseDurationMs;
        if (t >= 1)
        {
            _easing = false;
            return target;
        }

        long fromNow = _easeFromMs + (long)((now - _easeStartedAt).TotalMilliseconds * (_running ? _rate : 0));
        return fromNow + (long)((target - fromNow) * t);
    }

    private long RawPosition(DateTimeOffset now)
    {
        if (!_running)
        {
            return _anchorPositionMs;
        }

        return _anchorPositionMs + (long)((now - _anchorAt).TotalMilliseconds * _rate);
    }

    private void SetAnchor(long positionMs, DateTimeOffset at)
    {
        _anchorPositionMs = Math.Max(0, positionMs);
        _anchorAt = at;
    }
}
