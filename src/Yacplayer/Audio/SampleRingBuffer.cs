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
    public void Reset() => Volatile.Write(ref _read, Volatile.Read(ref _write));
}
