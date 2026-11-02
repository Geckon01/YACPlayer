namespace Yacplayer.Audio;
public sealed class SampleRingBuffer
{
    private readonly float[] _data; private long _write; private long _read;
    public SampleRingBuffer(int capacity) { if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity)); _data = new float[capacity]; }
    public int Capacity => _data.Length; public int Count => (int)Math.Min(Capacity, Math.Max(0, Volatile.Read(ref _write) - Volatile.Read(ref _read)));
    public void Write(ReadOnlySpan<float> values)
    {
        var write = _write; foreach (var value in values) _data[(int)(write++ % Capacity)] = value; Volatile.Write(ref _write, write);
        var minimumRead = write - Capacity; if (Volatile.Read(ref _read) < minimumRead) Volatile.Write(ref _read, minimumRead);
    }
    public int Read(Span<float> destination)
    {
        var read = Volatile.Read(ref _read); var available = (int)Math.Min(destination.Length, Math.Max(0, Volatile.Read(ref _write) - read));
        for (var i = 0; i < available; i++) destination[i] = _data[(int)((read + i) % Capacity)]; Volatile.Write(ref _read, read + available); return available;
    }
    public int ReadLatest(Span<float> destination)
    {
        var write = Volatile.Read(ref _write); var read = Volatile.Read(ref _read);
        var available = (int)Math.Min(Capacity, Math.Max(0, write - read)); var count = Math.Min(destination.Length, available); var start = write - count;
        for (var i = 0; i < count; i++) destination[i] = _data[(int)((start + i) % Capacity)]; Volatile.Write(ref _read, write); return count;
    }
    public void Reset() => Volatile.Write(ref _read, Volatile.Read(ref _write));
}
