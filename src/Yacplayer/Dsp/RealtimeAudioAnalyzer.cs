using System.Numerics;
using FftFlat;
using Yacplayer.Audio;

namespace Yacplayer.Dsp;

public readonly record struct RealtimeAudioSnapshot(
    bool Available,
    float RmsDbfs,
    float PeakDbfs,
    float CrestDb,
    float ZeroCrossingRate,
    float SpectralCentroidHz,
    float DominantFrequencyHz,
    float Bpm,
    float BeatConfidence,
    bool BeatDetected)
{
    public static RealtimeAudioSnapshot Empty => new(
        false,
        -90,
        -90,
        0,
        0,
        0,
        0,
        0,
        0,
        false);
}

public sealed class RealtimeAudioAnalyzer
{
    private const int MetricsFftSize = 2048;
    private const int OnsetFftSize = 1024;
    private const int OnsetHopSize = 256;

    private readonly SampleRingBuffer _ring;
    private readonly float[] _readBuffer = new float[8192];
    private readonly float[] _sampleWindow = new float[MetricsFftSize];
    private readonly float[] _orderedSamples = new float[MetricsFftSize];
    private readonly Complex[] _metricsFft = new Complex[MetricsFftSize];
    private readonly double[] _metricsHann = CreateHannWindow(MetricsFftSize);
    private readonly FastFourierTransform _metricsTransform = new(MetricsFftSize);
    private readonly float[] _onsetWindow = new float[OnsetFftSize];
    private readonly Complex[] _onsetFft = new Complex[OnsetFftSize];
    private readonly float[] _previousMagnitudes = new float[OnsetFftSize / 2];
    private readonly double[] _onsetHann = CreateHannWindow(OnsetFftSize);
    private readonly FastFourierTransform _onsetTransform = new(OnsetFftSize);
    private readonly BeatTracker _beatTracker = new();

    private int _sampleRate;
    private int _windowWrite;
    private int _windowCount;
    private int _onsetWindowWrite;
    private int _onsetWindowCount;
    private int _samplesSinceOnsetFrame;
    private long _processedSamples;
    private bool _hasPreviousSpectrum;
    private bool _fluxBaselineInitialized;
    private double _fluxMean;
    private double _fluxDeviation;
    private RealtimeAudioSnapshot _last = RealtimeAudioSnapshot.Empty;

    public RealtimeAudioAnalyzer(SampleRingBuffer ring)
    {
        _ring = ring;
    }

    public RealtimeAudioSnapshot Analyze(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return RealtimeAudioSnapshot.Empty;
        }

        if (_sampleRate != sampleRate)
        {
            Reset();
            _sampleRate = sampleRate;
        }

        var receivedSamples = false;
        int read;
        while ((read = _ring.Read(_readBuffer)) > 0)
        {
            receivedSamples = true;
            ProcessSamples(_readBuffer.AsSpan(0, read));
        }

        if (!receivedSamples || _windowCount < MetricsFftSize)
        {
            return receivedSamples
                ? RealtimeAudioSnapshot.Empty
                : _last with { BeatDetected = false };
        }

        _last = CalculateSnapshot();
        return _last;
    }

    public void Reset()
    {
        _ring.Reset();
        Array.Clear(_sampleWindow);
        Array.Clear(_orderedSamples);
        Array.Clear(_metricsFft);
        Array.Clear(_onsetWindow);
        Array.Clear(_onsetFft);
        Array.Clear(_previousMagnitudes);
        _beatTracker.Reset();
        _sampleRate = 0;
        _windowWrite = 0;
        _windowCount = 0;
        _onsetWindowWrite = 0;
        _onsetWindowCount = 0;
        _samplesSinceOnsetFrame = 0;
        _processedSamples = 0;
        _hasPreviousSpectrum = false;
        _fluxBaselineInitialized = false;
        _fluxMean = 0;
        _fluxDeviation = 0;
        _last = RealtimeAudioSnapshot.Empty;
    }

    private void ProcessSamples(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _sampleWindow[_windowWrite] = sample;
            _windowWrite = (_windowWrite + 1) % MetricsFftSize;
            _windowCount = Math.Min(MetricsFftSize, _windowCount + 1);

            _onsetWindow[_onsetWindowWrite] = sample;
            _onsetWindowWrite = (_onsetWindowWrite + 1) % OnsetFftSize;
            _onsetWindowCount = Math.Min(OnsetFftSize, _onsetWindowCount + 1);
            _samplesSinceOnsetFrame++;
            _processedSamples++;

            if (_onsetWindowCount == OnsetFftSize &&
                _samplesSinceOnsetFrame >= OnsetHopSize)
            {
                _samplesSinceOnsetFrame = 0;
                ProcessOnsetFrame();
            }
        }
    }

    private void ProcessOnsetFrame()
    {
        for (var index = 0; index < OnsetFftSize; index++)
        {
            var sample = _onsetWindow[(_onsetWindowWrite + index) % OnsetFftSize];
            _onsetFft[index] = new Complex(sample * _onsetHann[index], 0);
        }

        _onsetTransform.Forward(_onsetFft);

        double lowFlux = 0;
        double midFlux = 0;
        double highFlux = 0;
        var lowBins = 0;
        var midBins = 0;
        var highBins = 0;

        for (var bin = 1; bin < OnsetFftSize / 2; bin++)
        {
            var frequency = bin * (double)_sampleRate / OnsetFftSize;
            if (frequency > 10000)
            {
                break;
            }

            var magnitude = (float)Math.Log(1 + _onsetFft[bin].Magnitude);
            var difference = _hasPreviousSpectrum
                ? Math.Max(0, magnitude - _previousMagnitudes[bin])
                : 0;
            _previousMagnitudes[bin] = magnitude;

            if (frequency < 180)
            {
                lowFlux += difference;
                lowBins++;
            }
            else if (frequency < 2500)
            {
                midFlux += difference;
                midBins++;
            }
            else
            {
                highFlux += difference;
                highBins++;
            }
        }

        _hasPreviousSpectrum = true;
        var rawFlux = 0.45 * Average(lowFlux, lowBins) +
                      0.4 * Average(midFlux, midBins) +
                      0.15 * Average(highFlux, highBins);
        var onset = NormalizeFlux(rawFlux);
        var frameCenterSample = _processedSamples - OnsetFftSize / 2L;
        _beatTracker.AddFrame(onset, frameCenterSample, _sampleRate, OnsetHopSize);
    }

    private float NormalizeFlux(double rawFlux)
    {
        if (!_fluxBaselineInitialized)
        {
            _fluxBaselineInitialized = true;
            _fluxMean = rawFlux;
            _fluxDeviation = Math.Max(rawFlux * 0.5, 1e-5);
            return 0;
        }

        var deviation = Math.Max(_fluxDeviation, 1e-5);
        var normalized = Math.Clamp((rawFlux - _fluxMean) / deviation - 0.5, 0, 8);
        var meanRate = rawFlux > _fluxMean ? 0.012 : 0.045;
        _fluxMean += (rawFlux - _fluxMean) * meanRate;
        _fluxDeviation += (Math.Abs(rawFlux - _fluxMean) - _fluxDeviation) * 0.035;
        return (float)normalized;
    }

    private RealtimeAudioSnapshot CalculateSnapshot()
    {
        for (var index = 0; index < MetricsFftSize; index++)
        {
            _orderedSamples[index] = _sampleWindow[(_windowWrite + index) % MetricsFftSize];
        }

        double squareSum = 0;
        float peak = 0;
        var crossings = 0;
        for (var index = 0; index < MetricsFftSize; index++)
        {
            var sample = _orderedSamples[index];
            squareSum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
            if (index > 0 && (sample >= 0) != (_orderedSamples[index - 1] >= 0))
            {
                crossings++;
            }

            _metricsFft[index] = new Complex(sample * _metricsHann[index], 0);
        }

        _metricsTransform.Forward(_metricsFft);

        double magnitudeSum = 0;
        double weightedFrequency = 0;
        double dominantMagnitude = 0;
        var dominantFrequency = 0d;
        var minimumBin = Math.Max(1, (int)Math.Ceiling(40d * MetricsFftSize / _sampleRate));
        for (var bin = minimumBin; bin < MetricsFftSize / 2; bin++)
        {
            var magnitude = _metricsFft[bin].Magnitude;
            var frequency = bin * (double)_sampleRate / MetricsFftSize;
            magnitudeSum += magnitude;
            weightedFrequency += magnitude * frequency;
            if (magnitude > dominantMagnitude)
            {
                dominantMagnitude = magnitude;
                dominantFrequency = frequency;
            }
        }

        var rms = Math.Sqrt(squareSum / MetricsFftSize);
        var crest = rms <= 1e-12 ? 0 : 20 * Math.Log10(peak / rms);
        return new RealtimeAudioSnapshot(
            true,
            ToDbfs(rms),
            ToDbfs(peak),
            (float)Math.Clamp(crest, 0, 60),
            crossings / (float)(MetricsFftSize - 1),
            magnitudeSum <= 1e-12 ? 0 : (float)(weightedFrequency / magnitudeSum),
            (float)dominantFrequency,
            _beatTracker.Bpm,
            _beatTracker.Confidence,
            _beatTracker.BeatDetected);
    }

    private static double Average(double value, int count) => count == 0 ? 0 : value / count;

    private static double[] CreateHannWindow(int size) => Enumerable.Range(0, size)
        .Select(index => 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (size - 1)))
        .ToArray();

    private static float ToDbfs(double amplitude) =>
        (float)Math.Max(-90, 20 * Math.Log10(Math.Max(amplitude, 1e-12)));
}
