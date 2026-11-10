namespace Yacplayer.Dsp;

/// <summary>
/// Offline loudness profile of a track: one RMS dBFS value per fixed block.
/// The UI derives arrangement signals (current loudness, lift against the
/// preceding section, and the approach of a louder upcoming section) from it.
/// Instances are immutable after construction and read on the UI thread only.
/// </summary>
internal sealed class TrackEnergyMap
{
    private readonly float[] _blockDbfs;
    private readonly double _secondsPerBlock;
    private readonly double _floorDb;
    private readonly double _ceilingDb;

    public TrackEnergyMap(float[] blockDbfs, double secondsPerBlock)
    {
        if (blockDbfs.Length == 0)
        {
            throw new ArgumentException("Energy map requires at least one block.", nameof(blockDbfs));
        }

        if (secondsPerBlock <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondsPerBlock));
        }

        _blockDbfs = blockDbfs;
        _secondsPerBlock = secondsPerBlock;

        var sorted = (float[])blockDbfs.Clone();
        Array.Sort(sorted);
        _floorDb = Percentile(sorted, 0.10);
        _ceilingDb = Percentile(sorted, 0.90);
    }

    public int BlockCount => _blockDbfs.Length;

    /// <summary>
    /// Samples the arrangement around <paramref name="seconds"/>: current loudness
    /// (t ± 1 block), the lift against the preceding window (t-10s..t-2s), the
    /// approach of the following window (t+3s..t+13s), and the track-relative
    /// loudness between the 10th and 90th percentiles.
    /// </summary>
    public (double Now, double Lift, double Approach, double Intensity) Sample(double seconds)
    {
        var center = BlockAt(seconds);
        var halfWindow = (int)Math.Round(1.0 / _secondsPerBlock);
        var now = Mean(center - halfWindow, center + halfWindow);

        var pastSpan = WindowSpan(10, 2);
        var past = Mean(center - pastSpan.end, center - pastSpan.start);

        var futureSpan = WindowSpan(3, 13);
        var future = Mean(center + futureSpan.start, center + futureSpan.end);

        var spread = Math.Max(1.0, _ceilingDb - _floorDb);
        var intensity = Math.Clamp((now - _floorDb) / spread, 0, 1);
        return (now, now - past, future - now, intensity);
    }

    /// <summary>
    /// Converts a block RMS value to a 0..1 absolute loudness where -30 dBFS is
    /// silent-calm and -6 dBFS is maximum.
    /// </summary>
    public static double NormalizeAbsoluteLoudness(double dbfs)
        => Math.Clamp((dbfs + 30) / 24d, 0, 1);

    private int BlockAt(double seconds)
    {
        if (!(seconds > 0))
        {
            return 0;
        }

        var index = (int)(seconds / _secondsPerBlock);
        return Math.Min(_blockDbfs.Length - 1, Math.Max(0, index));
    }

    private (int start, int end) WindowSpan(double fromSeconds, double toSeconds)
    {
        var start = Math.Max(1, (int)Math.Round(fromSeconds / _secondsPerBlock));
        var end = Math.Max(start, (int)Math.Round(toSeconds / _secondsPerBlock));
        return (start, end);
    }

    private double Mean(int fromBlock, int toBlock)
    {
        var from = Math.Clamp(fromBlock, 0, _blockDbfs.Length - 1);
        var to = Math.Clamp(toBlock, from, _blockDbfs.Length - 1);
        double sum = 0;
        for (var index = from; index <= to; index++)
        {
            sum += _blockDbfs[index];
        }

        return sum / (to - from + 1);
    }

    private static double Percentile(float[] sorted, double rank)
    {
        var position = Math.Clamp(rank * (sorted.Length - 1), 0, sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sorted.Length - 1, lower + 1);
        var fraction = position - lower;
        return sorted[lower] * (1 - fraction) + sorted[upper] * fraction;
    }
}
