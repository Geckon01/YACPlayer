using Yacplayer.Library;

namespace Yacplayer.Tests;

public class PlaylistEdgeCaseTests
{
    [Fact]
    public void SingleTrackShuffleRepeatAllStartsANewCycleWithoutAnEmptyBagPop()
    {
        var playlist = new Playlist(2);
        playlist.Add(new TrackEntry("only"));
        playlist.SetShuffle(true);
        playlist.SetRepeat(RepeatMode.All);

        Assert.Equal("only", playlist.Next()!.Path);
        Assert.Equal("only", playlist.Next()!.Path);
        Assert.Equal("only", playlist.Next(automatic: true)!.Path);

        playlist.SetRepeat(RepeatMode.Off);
        Assert.Null(playlist.Next());
    }
}
