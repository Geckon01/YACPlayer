using NAudio.Wave;
using System.Runtime.InteropServices;
using Yacplayer.Dsp;

namespace Yacplayer.Audio;

/// <summary>
/// Decodes a track offline on a background thread and builds its energy map.
/// Runs in parallel with playback through an independent decoder, so it never
/// touches the playback pipeline or the audio callback.
/// </summary>
internal static class TrackEnergyScanner
{
    public const double SecondsPerBlock = 1.0;

    public static async Task<TrackEnergyMap?> ScanAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => Scan(path, ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TrackEnergyMap? Scan(string path, CancellationToken ct)
    {
        using var reader = new AudioFileReader(path);
        var sampleRate = reader.WaveFormat.SampleRate;
        if (sampleRate <= 0)
        {
            return null;
        }

        var blockSamples = (int)(sampleRate * SecondsPerBlock);
        var raw = new byte[8192 * 4];
        var energies = new List<float>();
        double sumOfSquares = 0;
        var taken = 0;

        while (true)
        {
            var read = reader.Read(raw, 0, raw.Length);
            if (read <= 0)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            var samples = MemoryMarshal.Cast<byte, float>(raw.AsSpan(0, read));
            for (var index = 0; index < samples.Length; index++)
            {
                sumOfSquares += (double)samples[index] * samples[index];
                taken++;
                if (taken == blockSamples)
                {
                    var rms = Math.Sqrt(sumOfSquares / blockSamples);
                    energies.Add((float)(20 * Math.Log10(Math.Max(rms, 1e-9))));
                    sumOfSquares = 0;
                    taken = 0;
                }
            }
        }

        if (taken > 0)
        {
            var rms = Math.Sqrt(sumOfSquares / taken);
            energies.Add((float)(20 * Math.Log10(Math.Max(rms, 1e-9))));
        }

        return energies.Count > 0
            ? new TrackEnergyMap(energies.ToArray(), SecondsPerBlock)
            : null;
    }
}
