using System.Numerics;
using FftFlat;
using Yacplayer.Audio;
namespace Yacplayer.Dsp;
public sealed class SpectrumAnalyzer
{
    private readonly SampleRingBuffer _ring; private readonly int _size; private readonly double[] _window; private readonly Complex[] _fft; private readonly float[] _samples; private readonly FastFourierTransform _transform;
    public SpectrumAnalyzer(SampleRingBuffer ring, int size = 2048) { if ((size & (size - 1)) != 0) throw new ArgumentException("FFT size must be power of two"); _ring = ring; _size = size; _samples = new float[size]; _fft = new Complex[size]; _window = Enumerable.Range(0, size).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (size - 1))).ToArray(); _transform = new(size); }
    public bool Analyze(int sampleRate, Span<float> bands)
    {
        if (_ring.Read(_samples) < _size) { bands.Clear(); return false; }
        for (var i = 0; i < _size; i++) _fft[i] = new(_samples[i] * _window[i], 0); _transform.Forward(_fft); bands.Clear();
        var min = 40d; var max = Math.Min(18000d, sampleRate / 2d); for (var b = 0; b < bands.Length; b++) { var low = min * Math.Pow(max / min, b / (double)bands.Length); var high = min * Math.Pow(max / min, (b + 1d) / bands.Length); var from = Math.Max(1, (int)(low * _size / sampleRate)); var to = Math.Min(_size / 2, Math.Max(from + 1, (int)(high * _size / sampleRate))); double peak = 0; for (var k = from; k < to; k++) peak = Math.Max(peak, _fft[k].Magnitude); bands[b] = (float)Math.Clamp(Math.Log10(1 + peak) / 3, 0, 1); }
        return true;
    }
}
