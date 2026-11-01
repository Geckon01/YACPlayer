using NAudio.Wave;
namespace Yacplayer.Audio;
public sealed class AudioEngine : IDisposable
{
    private readonly SampleRingBuffer _ring; private WasapiPlayer? _output; private AudioFileReader? _reader; private string? _path; private int _volume = 75; private bool _muted; private bool _intentional;
    public AudioEngine(SampleRingBuffer ring) => _ring = ring;
    public event Action? Ended; public event Action<string>? Error;
    public PlaybackSnapshot Snapshot => new(_output?.PlaybackState switch { NAudio.Wave.PlaybackState.Playing => PlaybackState.Playing, NAudio.Wave.PlaybackState.Paused => PlaybackState.Paused, _ => PlaybackState.Stopped }, _path, _reader?.CurrentTime ?? default, _reader?.TotalTime ?? default, _reader?.WaveFormat.SampleRate ?? 0, _reader?.WaveFormat.Channels ?? 0, _volume, _muted);
    public void Open(string path)
    {
        Close(); try { _path = path; _reader = new(path); ApplyVolume(); var tap = new PcmTapSampleProvider(_reader, _ring); _output = new WasapiPlayerBuilder().WithSharedMode().WithLatency(100).Build(); _output.PlaybackStopped += OnStopped; _output.Init(tap.ToWaveProvider()); _intentional = false; _output.Play(); }
        catch (Exception ex) { Close(); Error?.Invoke($"Cannot play {System.IO.Path.GetFileName(path)}: {ex.Message}"); }
    }
    public void TogglePause() { if (_output?.PlaybackState == NAudio.Wave.PlaybackState.Playing) _output.Pause(); else if (_output?.PlaybackState == NAudio.Wave.PlaybackState.Paused) _output.Play(); }
    public void Stop() { _intentional = true; _output?.Stop(); if (_reader is not null) _reader.Position = 0; _ring.Reset(); }
    public void Seek(double seconds) { if (_reader is null) return; _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(_reader.CurrentTime.TotalSeconds + seconds, 0, _reader.TotalTime.TotalSeconds)); _ring.Reset(); }
    public void SetVolume(int value) { _volume = Math.Clamp(value, 0, 100); ApplyVolume(); }
    public void ToggleMute() { _muted = !_muted; ApplyVolume(); }
    private void ApplyVolume() { if (_reader is not null) _reader.Volume = _muted ? 0 : _volume / 100f; }
    private void OnStopped(object? sender, StoppedEventArgs e) { if (e.Exception is not null) Error?.Invoke(e.Exception.Message); else if (!_intentional) Ended?.Invoke(); }
    private void Close() { if (_output is not null) { _intentional = true; _output.PlaybackStopped -= OnStopped; _output.Stop(); _output.Dispose(); } _reader?.Dispose(); _output = null; _reader = null; _path = null; _ring.Reset(); }
    public void Dispose() => Close();
}
