using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Yacplayer.Audio;

public sealed class AudioEngine : IDisposable
{
    private readonly SampleRingBuffer _ring;
    private readonly AudioEventQueue _events;
    private WasapiPlayer? _output;
    private AudioFileReader? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private string? _path;
    private int _volume = 75;
    private bool _muted;
    private volatile bool _intentional;
    private long _generation;

    public AudioEngine(SampleRingBuffer ring, AudioEventQueue events)
    {
        _ring = ring;
        _events = events;
    }

    public long Generation => Volatile.Read(ref _generation);

    public PlaybackSnapshot Snapshot => new(
        _output?.PlaybackState switch
        {
            NAudio.Wave.PlaybackState.Playing => PlaybackState.Playing,
            NAudio.Wave.PlaybackState.Paused => PlaybackState.Paused,
            _ => PlaybackState.Stopped
        },
        _path,
        _reader?.CurrentTime ?? default,
        _reader?.TotalTime ?? default,
        _reader?.WaveFormat.SampleRate ?? 0,
        _reader?.WaveFormat.Channels ?? 0,
        _volume,
        _muted);

    public void Open(string path)
    {
        Close();
        var generation = Interlocked.Increment(ref _generation);

        try
        {
            _path = path;
            _reader = new(path);
            var tap = new PcmTapSampleProvider(_reader, _ring);
            _volumeProvider = new(tap);
            ApplyVolume();

            _output = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithLatency(100)
                .Build();

            _stoppedHandler = (_, e) => OnStopped(generation, e);
            _output.PlaybackStopped += _stoppedHandler;
            _output.Init(_volumeProvider.ToWaveProvider());
            _intentional = false;
            _output.Play();
        }
        catch (Exception ex)
        {
            Close();
            _events.Publish(new AudioEvent(
                AudioEventKind.Error,
                Generation,
                $"Cannot play {System.IO.Path.GetFileName(path)}: {ex.Message}"));
        }
    }

    public void TogglePause()
    {
        if (_output?.PlaybackState == NAudio.Wave.PlaybackState.Playing)
        {
            _output.Pause();
        }
        else if (_output?.PlaybackState == NAudio.Wave.PlaybackState.Paused)
        {
            _output.Play();
        }
    }

    public void Stop()
    {
        _intentional = true;
        Interlocked.Increment(ref _generation);
        _output?.Stop();

        if (_reader is not null)
        {
            _reader.Position = 0;
        }

        _ring.Reset();
    }

    public void Seek(double seconds)
    {
        if (_reader is null)
        {
            return;
        }

        _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(
            _reader.CurrentTime.TotalSeconds + seconds,
            0,
            _reader.TotalTime.TotalSeconds));
        _ring.Reset();
    }

    public void SetVolume(int value)
    {
        _volume = Math.Clamp(value, 0, 100);
        ApplyVolume();
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (_volumeProvider is not null)
        {
            _volumeProvider.Volume = _muted ? 0 : _volume / 100f;
        }
    }

    private void OnStopped(long generation, StoppedEventArgs e)
    {
        if (generation != Generation)
        {
            return;
        }

        if (e.Exception is not null)
        {
            _events.Publish(new AudioEvent(AudioEventKind.Error, generation, e.Exception.Message));
        }
        else if (!_intentional)
        {
            _events.Publish(new AudioEvent(AudioEventKind.Ended, generation));
        }
    }

    private void Close()
    {
        _intentional = true;
        Interlocked.Increment(ref _generation);

        if (_output is not null)
        {
            if (_stoppedHandler is not null)
            {
                _output.PlaybackStopped -= _stoppedHandler;
            }

            _output.Stop();
            _output.Dispose();
        }

        _reader?.Dispose();
        _output = null;
        _volumeProvider = null;
        _stoppedHandler = null;
        _reader = null;
        _path = null;
        _ring.Reset();
    }

    public void Dispose() => Close();
}
