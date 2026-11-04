using Yacplayer.Audio;
using Yacplayer.Configuration;
using Yacplayer.Dsp;

namespace Yacplayer.Application;

internal readonly record struct VisualizationFrame(
    float[] SpectrumBands,
    float[] Waveform,
    int WaveformCount,
    RealtimeAudioSnapshot RealtimeAudio);

internal sealed class VisualizationPipeline
{
    private readonly SampleRingBuffer _visualizationRing = new(32768);
    private readonly SampleRingBuffer _analysisRing = new(65536);
    private readonly SpectrumAnalyzer _spectrumAnalyzer;
    private readonly RealtimeAudioAnalyzer _realtimeAnalyzer;
    private readonly float[] _bands = new float[64];
    private readonly float[] _rawBands = new float[64];
    private readonly float[] _waveform = new float[4096];
    private int _waveformCount;
    private VisualizerMode _activeMode;
    private long _analysisGeneration;
    private RealtimeAudioSnapshot _realtimeAudio = RealtimeAudioSnapshot.Empty;

    public VisualizationPipeline(PlayerSettings settings)
    {
        _spectrumAnalyzer = new(_visualizationRing, settings.FftSize);
        _realtimeAnalyzer = new(_analysisRing);
        _activeMode = settings.VisualizerMode;
    }

    public SampleRingBuffer VisualizationRing => _visualizationRing;
    public SampleRingBuffer AnalysisRing => _analysisRing;

    public void InitializeAnalysisGeneration(long generation)
    {
        _analysisGeneration = generation;
    }

    public VisualizationFrame Update(
        PlaybackSnapshot playback,
        PlayerSettings settings,
        long analysisGeneration)
    {
        UpdateRealtimeAudio(playback, settings, analysisGeneration);
        UpdateVisualizer(playback, settings.VisualizerMode);
        return new(_bands, _waveform, _waveformCount, _realtimeAudio);
    }

    private void UpdateRealtimeAudio(
        PlaybackSnapshot playback,
        PlayerSettings settings,
        long analysisGeneration)
    {
        if (_analysisGeneration != analysisGeneration)
        {
            _analysisGeneration = analysisGeneration;
            _realtimeAnalyzer.Reset();
            _realtimeAudio = RealtimeAudioSnapshot.Empty;
        }

        if (settings.RealtimeAnalysisEnabled && playback.State == PlaybackState.Playing)
        {
            _realtimeAudio = _realtimeAnalyzer.Analyze(playback.SampleRate);
        }
        else if (!settings.RealtimeAnalysisEnabled)
        {
            _realtimeAudio = RealtimeAudioSnapshot.Empty;
        }
        else
        {
            _realtimeAudio = _realtimeAudio with { BeatDetected = false };
        }
    }

    private void UpdateVisualizer(PlaybackSnapshot playback, VisualizerMode mode)
    {
        if (_activeMode != mode)
        {
            _activeMode = mode;
            _visualizationRing.Reset();
            Array.Clear(_rawBands);
            Array.Clear(_bands);
            _waveformCount = 0;
        }

        if (mode == VisualizerMode.Spectrum)
        {
            _spectrumAnalyzer.Analyze(playback.SampleRate <= 0 ? 48000 : playback.SampleRate, _rawBands);
            for (var index = 0; index < _bands.Length; index++)
            {
                var smoothing = _rawBands[index] > _bands[index] ? 0.55f : 0.16f;
                _bands[index] += (_rawBands[index] - _bands[index]) * smoothing;
                if (_bands[index] < 0.002f)
                {
                    _bands[index] = 0;
                }
            }
        }
        else if (mode == VisualizerMode.Waveform)
        {
            _waveformCount = _visualizationRing.ReadLatest(_waveform);
        }
        else
        {
            _waveformCount = 0;
        }
    }
}
