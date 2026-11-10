using Yacplayer.Library;

namespace Yacplayer.Configuration;

public enum VisualizerMode
{
    Spectrum,
    Waveform,
    Off
}

public enum ReactiveUiMode
{
    Off,
    Subtle,
    Visualizer
}

public sealed class PlayerSettings
{
    public int Version { get; set; } = 1;
    public int Volume { get; set; } = 75;
    public bool Muted { get; set; }
    public int Fps { get; set; } = 30;
    public int FftSize { get; set; } = 2048;
    public VisualizerMode VisualizerMode { get; set; } = VisualizerMode.Spectrum;
    public bool RealtimeAnalysisEnabled { get; set; } = true;
    public ReactiveUiMode ReactiveUiMode { get; set; } = ReactiveUiMode.Off;
    public RepeatMode RepeatMode { get; set; }
    public bool Shuffle { get; set; }
    public bool Recursive { get; set; }
    public string LastDirectory { get; set; } = "";
    public string StartupDirectory { get; set; } = "";

    public void Validate()
    {
        Version = 1;
        Volume = Math.Clamp(Volume, 0, 100);

        if (Fps is not (30 or 60))
        {
            Fps = 30;
        }

        if (FftSize is not (2048 or 4096))
        {
            FftSize = 2048;
        }

        if (!Enum.IsDefined(VisualizerMode))
        {
            VisualizerMode = VisualizerMode.Spectrum;
        }

        if (!Enum.IsDefined(ReactiveUiMode))
        {
            ReactiveUiMode = ReactiveUiMode.Off;
        }

        if (!Enum.IsDefined(RepeatMode))
        {
            RepeatMode = RepeatMode.Off;
        }

        LastDirectory ??= "";
        StartupDirectory ??= "";

        if (LastDirectory.Length > 0 && !Directory.Exists(LastDirectory))
        {
            LastDirectory = "";
        }

        if (StartupDirectory.Length > 0 && !Directory.Exists(StartupDirectory))
        {
            StartupDirectory = "";
        }
    }
}
