using System.Numerics;
using FftFlat;
using Yacplayer.Audio;

namespace Yacplayer.Dsp;

public sealed class SpectrumAnalyzer
{
    private readonly SampleRingBuffer _ring;
    private readonly int _size;
    private readonly double[] _window;
    private readonly Complex[] _fft;
    private readonly float[] _samples;
    private readonly FastFourierTransform _transform;

    public SpectrumAnalyzer(SampleRingBuffer ring, int size = 2048)
    {
        if ((size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be power of two");
        }

        _ring = ring;
        _size = size;
        _samples = new float[size];
        _fft = new Complex[size];
        _window = Enumerable.Range(0, size)
            .Select(index => .5 - .5 * Math.Cos(2 * Math.PI * index / (size - 1)))
            .ToArray();
        _transform = new(size);
    }

    public bool Analyze(int sampleRate, Span<float> bands)
    {
        if (!_ring.TryReadExact(_samples))
        {
            return false;
        }

        for (var index = 0; index < _size; index++)
        {
            _fft[index] = new(_samples[index] * _window[index], 0);
        }

        _transform.Forward(_fft);
        bands.Clear();
        var minimumFrequency = 40d;
        var maximumFrequency = Math.Min(18000d, sampleRate / 2d);
        for (var band = 0; band < bands.Length; band++)
        {
            var low = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, band / (double)bands.Length);
            var high = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, (band + 1d) / bands.Length);
            var from = Math.Max(1, (int)(low * _size / sampleRate));
            var to = Math.Min(
                _size / 2,
                Math.Max(from + 1, (int)(high * _size / sampleRate)));
            double peak = 0;
            for (var bin = from; bin < to; bin++)
            {
                peak = Math.Max(peak, _fft[bin].Magnitude);
            }

            bands[band] = (float)Math.Clamp(Math.Log10(1 + peak) / 3, 0, 1);
        }

        return true;
    }
}
