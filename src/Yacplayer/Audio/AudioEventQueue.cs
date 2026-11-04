using System.Threading.Channels;

namespace Yacplayer.Audio;

public enum AudioEventKind
{
    Ended,
    Error
}

public readonly record struct AudioEvent(
    AudioEventKind Kind,
    long Generation,
    string? Message = null);

public sealed class AudioEventQueue
{
    private readonly Channel<AudioEvent> _channel;

    public AudioEventQueue(int capacity = 16)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _channel = Channel.CreateBounded<AudioEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    }

    public bool Publish(AudioEvent value) => _channel.Writer.TryWrite(value);

    public bool TryRead(out AudioEvent value) => _channel.Reader.TryRead(out value);
}
