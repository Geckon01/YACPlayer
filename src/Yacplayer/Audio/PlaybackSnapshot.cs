namespace Yacplayer.Audio;
public enum PlaybackState { Stopped, Playing, Paused }
public sealed record PlaybackSnapshot(PlaybackState State, string? Path, TimeSpan Position, TimeSpan Duration, int SampleRate, int Channels, int Volume, bool Muted);
