using NAudio.Wave;
namespace Yacplayer.Audio;
public sealed class PcmTapSampleProvider(ISampleProvider source, SampleRingBuffer ring) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(Span<float> buffer)
    {
        var read = source.Read(buffer); var channels = Math.Max(1, WaveFormat.Channels);
        if (channels == 1) ring.Write(buffer[..read]);
        else { Span<float> one = stackalloc float[1]; for (var i = 0; i + channels <= read; i += channels) { float mono = 0; for (var c = 0; c < channels; c++) mono += buffer[i + c]; one[0] = mono / channels; ring.Write(one); } }
        return read;
    }
}
