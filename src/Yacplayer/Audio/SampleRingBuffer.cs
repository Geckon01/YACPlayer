namespace Yacplayer.Audio;

/// <summary>
/// Lock-free single-producer/single-buffer-reader ring. The audio callback is the only
/// writer and owns <c>_write</c>; the UI loop is the only reader and owns <c>_read</c>.
/// Overflow drops oldest samples lazily: the consumer catches its own read position up
/// before copying, so the writer never touches <c>_read</c> and the reverse stays true.
/// </summary>
public sealed class SampleRingBuffer
{
    private readonly float[] _data;
    private readonly int _mask;

    // Written by the producer, published with release semantics in Write.
    private long _write;

    // Written by the consumer only.
    private long _read;

    public SampleRingBuffer(int capacity)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        var size = 2;
        while (size < capacity)
        {
            size <<= 1;
        }

        _data = new float[size];
        _mask = size - 1;
    }

    public int Capacity => _data.Length;

    public int Count => (int)Math.Min(_data.Length, Math.Max(0, Volatile.Read(ref _write) - Volatile.Read(ref _read)));

    /// <summary>Producer-side only (audio callback). Never mutates the consumer's read position.</summary>
    public void Write(ReadOnlySpan<float> values)
    {
        var data = _data;
        var write = _write;
        foreach (var value in values)
        {
            data[(int)(write++ & _mask)] = value;
        }

        Volatile.Write(ref _write, write);
    }

    /// <summary>Consumer-side only (UI loop).</summary>
    public int Read(Span<float> destination)
    {
        var write = Volatile.Read(ref _write);
        var read = CatchUp(Volatile.Read(ref _read), write);
        var available = (int)Math.Min(destination.Length, Math.Min(_data.Length, write - read));
        CopyTo(read, destination, available);
        Volatile.Write(ref _read, read + available);
        return available;
    }

    /// <summary>Consumer-side only (UI loop). Leaves partial data queued until it fits.</summary>
    public bool TryReadExact(Span<float> destination)
    {
        var write = Volatile.Read(ref _write);
        var read = CatchUp(Volatile.Read(ref _read), write);
        if (write - read < destination.Length)
        {
            return false;
        }

        CopyTo(read, destination, destination.Length);
        Volatile.Write(ref _read, read + destination.Length);
        return true;
    }

    /// <summary>Consumer-side only (UI loop). Returns the newest samples and discards the backlog.</summary>
    public int ReadLatest(Span<float> destination)
    {
        var write = Volatile.Read(ref _write);
        var available = (int)Math.Min(_data.Length, Math.Max(0, write - Volatile.Read(ref _read)));
        var count = Math.Min(destination.Length, available);
        CopyTo(write - count, destination, count);
        Volatile.Write(ref _read, write);
        return count;
    }

    /// <summary>Consumer-side only (UI loop).</summary>
    public void Reset() => Volatile.Write(ref _read, Volatile.Read(ref _write));

    private long CatchUp(long read, long write)
    {
        var minimumRead = write - _data.Length;
        return read < minimumRead ? minimumRead : read;
    }

    private void CopyTo(long start, Span<float> destination, int count)
    {
        var data = _data;
        for (var i = 0; i < count; i++)
        {
            destination[i] = data[(int)((start + i) & _mask)];
        }
    }
}
