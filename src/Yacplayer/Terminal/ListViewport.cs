namespace Yacplayer.Terminal;

public sealed class ListViewport
{
    public int Offset { get; private set; }

    public void Reset() => Offset = 0;

    public void EnsureVisible(int index, int itemCount, int visibleRows, int margin = 1)
    {
        if (itemCount <= 0 || visibleRows <= 0)
        {
            Offset = 0;
            return;
        }

        visibleRows = Math.Min(visibleRows, itemCount);
        index = Math.Clamp(index, 0, itemCount - 1);
        var maxOffset = Math.Max(0, itemCount - visibleRows);
        Offset = Math.Clamp(Offset, 0, maxOffset);

        var safeMargin = Math.Clamp(margin, 0, Math.Max(0, (visibleRows - 1) / 2));
        if (index < Offset + safeMargin)
            Offset = index - safeMargin;
        else if (index >= Offset + visibleRows - safeMargin)
            Offset = index - visibleRows + safeMargin + 1;

        Offset = Math.Clamp(Offset, 0, maxOffset);
    }
}
