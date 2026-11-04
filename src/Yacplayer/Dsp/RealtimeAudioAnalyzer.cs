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
    private const int FftSize = 2048;
    private const int EnergyFrameSize = 1024;
    private const int MaxOnsetFrames = 1024;
    private readonly SampleRingBuffer _ring;
    private readonly float[] _readBuffer = new float[8192];
    private readonly float[] _sampleWindow = new float[FftSize];
    private readonly float[] _orderedSamples = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _hann = Enumerable.Range(0, FftSize)
        .Select(index => 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FftSize - 1)))
        .ToArray();
    private readonly FastFourierTransform _transform = new(FftSize);
    private readonly List<float> _onsets = new(MaxOnsetFrames);
    private int _sampleRate;
    private int _windowWrite;
    private int _windowCount;
    private double _energySum;
    private int _energyCount;
    private float _previousEnergy;
    private int _beatHoldFrames;
    private int _framesSinceBpmEstimate;
    private float _bpm;
    private float _beatConfidence;
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

        if (!receivedSamples || _windowCount < FftSize)
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
        Array.Clear(_fft);
        _onsets.Clear();
        _sampleRate = 0;
        _windowWrite = 0;
        _windowCount = 0;
        _energySum = 0;
        _energyCount = 0;
        _previousEnergy = 0;
        _beatHoldFrames = 0;
        _framesSinceBpmEstimate = 0;
        _bpm = 0;
        _beatConfidence = 0;
        _last = RealtimeAudioSnapshot.Empty;
    }

    private void ProcessSamples(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _sampleWindow[_windowWrite] = sample;
            _windowWrite = (_windowWrite + 1) % FftSize;
            _windowCount = Math.Min(FftSize, _windowCount + 1);

            _energySum += sample * sample;
            _energyCount++;
            if (_energyCount == EnergyFrameSize)
            {
                AddEnergyFrame((float)Math.Sqrt(_energySum / EnergyFrameSize));
                _energySum = 0;
                _energyCount = 0;
            }
        }
    }

    private void AddEnergyFrame(float energy)
    {
        var onset = Math.Max(0, energy - _previousEnergy);
        _previousEnergy = energy;

        var averageCount = Math.Min(32, _onsets.Count);
        double recentAverage = 0;
        for (var index = _onsets.Count - averageCount; index < _onsets.Count; index++)
        {
            recentAverage += _onsets[index];
        }

        if (averageCount > 0)
        {
            recentAverage /= averageCount;
        }

        if (onset > Math.Max(0.004, recentAverage * 2.5))
        {
            _beatHoldFrames = 5;
        }
        else if (_beatHoldFrames > 0)
        {
            _beatHoldFrames--;
        }

        _onsets.Add(onset);
        if (_onsets.Count > MaxOnsetFrames)
        {
            _onsets.RemoveRange(0, _onsets.Count - MaxOnsetFrames);
        }

        _framesSinceBpmEstimate++;
        var estimateInterval = Math.Max(1, (int)Math.Round(_sampleRate / (double)EnergyFrameSize / 2));
        if (_framesSinceBpmEstimate >= estimateInterval)
        {
            _framesSinceBpmEstimate = 0;
            EstimateTempo();
        }
    }

    private void EstimateTempo()
    {
        var frameRate = _sampleRate / (double)EnergyFrameSize;
        var historyCount = Math.Min(_onsets.Count, (int)Math.Ceiling(frameRate * 12));
        if (historyCount < frameRate * 4)
        {
            _bpm = 0;
            _beatConfidence = 0;
            return;
        }

        var start = _onsets.Count - historyCount;
        double mean = 0;
        for (var index = start; index < _onsets.Count; index++)
        {
            mean += _onsets[index];
        }

        mean /= historyCount;
        var minimumLag = Math.Max(1, (int)Math.Floor(frameRate * 60 / 200));
        var maximumLag = Math.Min(historyCount / 2, (int)Math.Ceiling(frameRate * 60 / 60));
        var bestLag = 0;
        double bestCorrelation = 0;

        for (var lag = minimumLag; lag <= maximumLag; lag++)
        {
            double correlation = 0;
            double leftEnergy = 0;
            double rightEnergy = 0;
            for (var index = lag; index < historyCount; index++)
            {
                var left = _onsets[start + index] - mean;
                var right = _onsets[start + index - lag] - mean;
                correlation += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }

            var denominator = Math.Sqrt(leftEnergy * rightEnergy);
            var normalized = denominator <= 1e-12 ? 0 : correlation / denominator;
            if (normalized > bestCorrelation)
            {
                bestCorrelation = normalized;
                bestLag = lag;
            }
        }

        _bpm = bestLag == 0 ? 0 : (float)(60 * frameRate / bestLag);
        _beatConfidence = (float)Math.Clamp(bestCorrelation, 0, 1);
    }

    private RealtimeAudioSnapshot CalculateSnapshot()
    {
        for (var index = 0; index < FftSize; index++)
        {
            _orderedSamples[index] = _sampleWindow[(_windowWrite + index) % FftSize];
        }

        double squareSum = 0;
        float peak = 0;
        var crossings = 0;
        for (var index = 0; index < FftSize; index++)
        {
            var sample = _orderedSamples[index];
            squareSum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
            if (index > 0 && (sample >= 0) != (_orderedSamples[index - 1] >= 0))
            {
                crossings++;
            }

            _fft[index] = new Complex(sample * _hann[index], 0);
        }

        _transform.Forward(_fft);

        double magnitudeSum = 0;
        double weightedFrequency = 0;
        double dominantMagnitude = 0;
        var dominantFrequency = 0d;
        var minimumBin = Math.Max(1, (int)Math.Ceiling(40d * FftSize / _sampleRate));
        for (var bin = minimumBin; bin < FftSize / 2; bin++)
        {
            var magnitude = _fft[bin].Magnitude;
            var frequency = bin * (double)_sampleRate / FftSize;
            magnitudeSum += magnitude;
            weightedFrequency += magnitude * frequency;
            if (magnitude > dominantMagnitude)
            {
                dominantMagnitude = magnitude;
                dominantFrequency = frequency;
            }
        }

        var rms = Math.Sqrt(squareSum / FftSize);
        var crest = rms <= 1e-12 ? 0 : 20 * Math.Log10(peak / rms);
        return new RealtimeAudioSnapshot(
            true,
            ToDbfs(rms),
            ToDbfs(peak),
            (float)Math.Clamp(crest, 0, 60),
            crossings / (float)(FftSize - 1),
            magnitudeSum <= 1e-12 ? 0 : (float)(weightedFrequency / magnitudeSum),
            (float)dominantFrequency,
            _bpm,
            _beatConfidence,
            _beatHoldFrames > 0);
    }

    private static float ToDbfs(double amplitude) =>
        (float)Math.Max(-90, 20 * Math.Log10(Math.Max(amplitude, 1e-12)));
}
