using NAudio.Wave;

namespace Yacplayer.Audio;

public sealed class PcmTapSampleProvider(
    ISampleProvider source,
    SampleRingBuffer visualizationRing,
    SampleRingBuffer? analysisRing = null) : ISampleProvider
{
    private volatile bool _analysisEnabled;

    public WaveFormat WaveFormat => source.WaveFormat;

    public bool AnalysisEnabled
    {
        get => _analysisEnabled;
        set => _analysisEnabled = value;
    }

    public int Read(Span<float> buffer)
    {
        var read = source.Read(buffer);
        var channels = Math.Max(1, WaveFormat.Channels);
        if (channels == 1)
        {
            var samples = buffer[..read];
            visualizationRing.Write(samples);
            if (_analysisEnabled)
            {
                analysisRing?.Write(samples);
            }
        }
        else
        {
            Span<float> one = stackalloc float[1];
            for (var i = 0; i + channels <= read; i += channels)
            {
                float mono = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    mono += buffer[i + channel];
                }

                one[0] = mono / channels;
                visualizationRing.Write(one);
                if (_analysisEnabled)
                {
                    analysisRing?.Write(one);
                }
            }
        }

        return read;
    }
}
