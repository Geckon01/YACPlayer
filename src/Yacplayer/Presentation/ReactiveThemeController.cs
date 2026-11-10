using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Terminal;

namespace Yacplayer.Presentation;

/// <summary>
/// Colors derived from music characteristics. When <see cref="Active"/> is false the
/// renderer must use its static theme. All values are produced on the UI thread.
/// </summary>
public readonly record struct ReactiveTheme(
    bool Active,
    CellColor Border,
    CellBackground TitleBackground,
    CellBackground RowBackground,
    CellColor AccentText,
    double Hue = ReactiveThemeController.BaseHue,
    double Saturation = 0.82,
    double Pulse = 0,
    double VersionGlow = 0,
    double VersionHue = 0);

/// <summary>
/// Converts smoothed music features (bass energy, spectral centroid, overall energy)
/// into bounded HSL color parameters. Updates are rate limited to roughly 12.5 FPS and
/// no-op entirely while the mode is Off. When playback is inactive or audio data is
/// missing, parameters glide back to the static green theme through the release path.
/// </summary>
internal sealed class ReactiveThemeController
{
    public const double BaseHue = 118;
    public const double MinimumHue = 78;
    public const double MaximumHue = 192;
    private const long UpdateIntervalMs = 80;

    private const double GoldenAngle = 137.508;

    // Arrangement-driven amplitude scaling: quiet sections respond at 40% of
    // the base amplitude, climaxes at 160%, smoothed over seconds so section
    // boundaries glide rather than switch.
    private const double QuietScale = 0.4;
    private const double LoudScale = 1.6;

    private readonly double _timeScale;
    private long _lastUpdateMs = long.MinValue;
    private long _lastCallMs = long.MinValue;
    private bool _previousBeat;
    private double _versionGlow;
    private double _versionHue;
    private double _bass;
    private double _hue = BaseHue;
    private double _saturation = 0.82;
    private double _drama = 1;
    private bool _arrangementActive;
    private ReactiveTheme _current;

    public ReactiveThemeController(double timeScale = 1.0)
    {
        _timeScale = timeScale;
    }

    public ReactiveTheme Update(
        RealtimeAudioSnapshot features,
        bool reacting,
        ReactiveUiMode mode,
        long nowMs)
        => Update(features, reacting, mode, nowMs, null, 0);

    /// <summary>
    /// When an <paramref name="arrangement"/> map is available for the current
    /// track, a smoothed drama factor derived from it (loudness lift against
    /// the preceding section plus the approach of a louder upcoming section)
    /// scales the pulse amplitude, saturation, and hue speed: quiet sections
    /// stay calm, choruses push the interface to full amplitude.
    /// </summary>
    public ReactiveTheme Update(
        RealtimeAudioSnapshot features,
        bool reacting,
        ReactiveUiMode mode,
        long nowMs,
        TrackEnergyMap? arrangement,
        double positionSeconds)
    {
        if (mode == ReactiveUiMode.Off)
        {
            _current = default;
            _lastUpdateMs = long.MinValue;
            _lastCallMs = long.MinValue;
            _previousBeat = false;
            _versionGlow = 0;
            _arrangementActive = false;
            _drama = 1;
            return _current;
        }

        var callDt = _lastCallMs == long.MinValue
            ? 0
            : Math.Min(0.25, (nowMs - _lastCallMs) / 1000d) * _timeScale;
        _lastCallMs = nowMs;

        // The predicted beat edge is tracked every call so the short flash is never
        // dropped by the 80 ms color-update gate below.
        var beat = reacting && features.Available && features.BeatDetected;
        if (beat && !_previousBeat)
        {
            _versionGlow = 1;
            _versionHue = (_versionHue + GoldenAngle) % 360;
        }
        else if (_versionGlow > 0)
        {
            _versionGlow = _versionGlow <= 0.02 ? 0 : _versionGlow * Math.Exp(-callDt / 0.22);
        }

        _previousBeat = beat;

        if (_lastUpdateMs == long.MinValue)
        {
            _lastUpdateMs = nowMs;
            return Build(mode, reacting);
        }

        var elapsedMs = nowMs - _lastUpdateMs;
        if (elapsedMs < UpdateIntervalMs)
        {
            // Colors are gated, but the version-flash decay still advances each frame.
            return _current with { VersionGlow = _versionGlow, VersionHue = _versionHue };
        }

        _lastUpdateMs = nowMs;
        var dt = Math.Min(0.25, elapsedMs / 1000d) * _timeScale;
        var visualizer = mode == ReactiveUiMode.Visualizer;

        var bassTarget = reacting && features.Available
            ? Math.Clamp(features.BassEnergy, 0, 1)
            : 0;
        var alpha = bassTarget > _bass
            ? 1 - Math.Exp(-dt / 0.05)
            : 1 - Math.Exp(-dt / (visualizer ? 0.28 : 0.38));
        _bass += (bassTarget - _bass) * alpha;

        _arrangementActive = reacting && arrangement is not null;
        if (_arrangementActive)
        {
            var (_, lift, approach, intensity) = arrangement!.Sample(Math.Max(0, positionSeconds));
            var eventLevel = Math.Clamp(Math.Max(lift, approach) / 5d, 0, 1);
            var dramaTarget = Math.Clamp(0.45 * intensity + 0.55 * eventLevel, 0, 1);
            var dramaAlpha = dramaTarget > _drama
                ? 1 - Math.Exp(-dt / 1.5)
                : 1 - Math.Exp(-dt / 4.0);
            _drama += (dramaTarget - _drama) * dramaAlpha;
        }

        var hueSpeed = _arrangementActive ? 0.55 + 0.75 * _drama : 1;
        var hueTarget = reacting && features.Available && features.SpectralCentroidHz > 0
            ? MinimumHue + (MaximumHue - MinimumHue) * NormalizeCentroid(features.SpectralCentroidHz)
            : BaseHue;
        _hue += (ShortestHueDelta(_hue, hueTarget)) * (1 - Math.Exp(-dt / ((visualizer ? 1.6 : 2.2) / hueSpeed)));

        var saturationTarget = reacting && features.Available
            ? (visualizer ? 0.45 : 0.55) + (visualizer ? 0.50 : 0.35) * NormalizeEnergy(features.RmsDbfs)
            : 0.82;
        if (_arrangementActive)
        {
            saturationTarget *= 0.6 + 0.85 * _drama;
        }

        saturationTarget = Math.Clamp(saturationTarget, 0.45, 0.98);
        _saturation += (saturationTarget - _saturation) * (1 - Math.Exp(-dt / 2.0));

        return Build(mode, reacting);
    }

    private ReactiveTheme Build(ReactiveUiMode mode, bool reacting)
    {
        var visualizer = mode == ReactiveUiMode.Visualizer;
        // Pulse amplitudes stay bounded (18%/28% of the lightness axis) with
        // exponential envelopes so the motion reads as a glow, never a strobe.
        // The arrangement drama factor (1.0 when no map is available) scales the
        // base amplitude so quiet sections stay calm and choruses push to full.
        var amplitude = _arrangementActive ? QuietScale + (LoudScale - QuietScale) * _drama : 1;
        var pulse = _bass * (visualizer ? 0.28 : 0.18) * amplitude;
        var saturation = Math.Clamp(_saturation, 0.40, 0.98);
        var border = FromHsl(_hue, saturation * 0.85, Math.Clamp(0.40 + pulse * 1.1, 0.3, 0.82));
        var title = BackgroundFromHsl(_hue, saturation, Math.Clamp(0.42 + pulse * 1.5, 0.3, 0.80));
        var row = BackgroundFromHsl(_hue, saturation * 0.85, Math.Clamp(0.30 + pulse * 1.9, 0.22, 0.62));
        var accent = FromHsl(_hue, saturation * 0.75, Math.Clamp(0.62 + pulse * 0.8, 0.5, 0.92));
        _current = new ReactiveTheme(
            true, border, title, row, accent, _hue, saturation, pulse, _versionGlow, _versionHue);
        return _current;
    }

    private static double NormalizeCentroid(float hertz)
    {
        const double minimum = 100d;
        const double maximum = 6000d;
        var clamped = Math.Clamp(hertz, minimum, maximum);
        return Math.Log(clamped / minimum) / Math.Log(maximum / minimum);
    }

    private static double NormalizeEnergy(float rmsDbfs)
        => Math.Clamp((Math.Max(rmsDbfs, -90) + 50) / 40d, 0, 1);

    private static double ShortestHueDelta(double from, double to)
    {
        var delta = (to - from) % 360;
        if (delta > 180)
        {
            delta -= 360;
        }
        else if (delta < -180)
        {
            delta += 360;
        }

        return delta;
    }

    /// <summary>
    /// Easter egg: the header version label flashes in a fresh color on every
    /// predicted beat and fades back to white between beats.
    /// </summary>
    internal static CellColor VersionColor(double hue, double glow)
        => FromHsl(hue, 0.95 * glow, Math.Clamp(0.60 + 0.40 * (1 - glow), 0, 1));

    internal static CellColor FromHsl(double hue, double saturation, double lightness)
    {
        var (r, g, b) = HslToChannels(hue, saturation, lightness);
        return CellColor.Rgb(
            (int)Math.Round(r * 255),
            (int)Math.Round(g * 255),
            (int)Math.Round(b * 255));
    }

    internal static CellBackground BackgroundFromHsl(double hue, double saturation, double lightness)
    {
        var (r, g, b) = HslToChannels(hue, saturation, lightness);
        return CellBackground.Rgb(
            (int)Math.Round(r * 255),
            (int)Math.Round(g * 255),
            (int)Math.Round(b * 255));
    }

    private static (double R, double G, double B) HslToChannels(double hue, double saturation, double lightness)
    {
        var h = ((hue % 360) + 360) % 360 / 60d;
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = lightness - c / 2;
        return h switch
        {
            < 1 => (c + m, x + m, m),
            < 2 => (x + m, c + m, m),
            < 3 => (m, c + m, x + m),
            < 4 => (m, x + m, c + m),
            < 5 => (x + m, m, c + m),
            _ => (c + m, m, x + m),
        };
    }
}
