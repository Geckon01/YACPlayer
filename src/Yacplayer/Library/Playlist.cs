namespace Yacplayer.Library;

public enum RepeatMode
{
    Off,
    All,
    One
}

public sealed class Playlist
{
    private readonly List<TrackEntry> _items = [];
    private readonly Random _random;
    private readonly List<int> _bag = [];
    private readonly Stack<int> _history = [];

    public Playlist(int? seed = null)
    {
        _random = seed.HasValue ? new(seed.Value) : new();
    }

    public IReadOnlyList<TrackEntry> Items => _items;
    public int? CurrentIndex { get; private set; }
    public int SelectedIndex { get; private set; }
    public bool Shuffle { get; private set; }
    public RepeatMode Repeat { get; private set; }

    public void Add(TrackEntry item)
    {
        _items.Add(item);
        RefillBag();
    }

    public void AddRange(IEnumerable<TrackEntry> items)
    {
        _items.AddRange(items);
        RefillBag();
    }

    public void Select(int index)
    {
        SelectedIndex = _items.Count == 0
            ? 0
            : Math.Clamp(index, 0, _items.Count - 1);
    }

    public TrackEntry? Play(int index)
    {
        if ((uint)index >= (uint)_items.Count)
        {
            return null;
        }

        if (CurrentIndex is int old && old != index)
        {
            _history.Push(old);
        }

        CurrentIndex = index;
        SelectedIndex = index;
        _bag.Remove(index);
        return _items[index];
    }

    public TrackEntry? Next(bool automatic = false)
    {
        if (_items.Count == 0)
        {
            return null;
        }

        if (automatic && Repeat == RepeatMode.One && CurrentIndex is int same)
        {
            return Play(same);
        }

        if (Shuffle)
        {
            if (_bag.Count == 0)
            {
                if (Repeat != RepeatMode.All && CurrentIndex is not null)
                {
                    return null;
                }

                RefillBag();
            }

            return Play(PopBag());
        }

        var next = (CurrentIndex ?? -1) + 1;
        if (next >= _items.Count)
        {
            if (Repeat != RepeatMode.All)
            {
                return null;
            }

            next = 0;
        }

        return Play(next);
    }

    public TrackEntry? Previous()
    {
        if (_history.Count > 0)
        {
            return PlayWithoutHistory(_history.Pop());
        }

        if (CurrentIndex is > 0)
        {
            return PlayWithoutHistory(CurrentIndex.Value - 1);
        }

        return null;
    }

    public TrackEntry? Remove(int index)
    {
        if ((uint)index >= (uint)_items.Count)
        {
            return CurrentIndex is int c ? _items[c] : null;
        }

        bool current = CurrentIndex == index;
        _items.RemoveAt(index);

        if (_items.Count == 0)
        {
            CurrentIndex = null;
        }
        else if (current)
        {
            CurrentIndex = Math.Min(index, _items.Count - 1);
        }
        else if (CurrentIndex > index)
        {
            CurrentIndex--;
        }

        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, _items.Count - 1));
        _history.Clear();
        RefillBag();
        return CurrentIndex is int c2 ? _items[c2] : null;
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        _history.Clear();
        RefillBag();
    }

    public void SetRepeat(RepeatMode value) => Repeat = value;

    private TrackEntry PlayWithoutHistory(int index)
    {
        CurrentIndex = index;
        SelectedIndex = index;
        return _items[index];
    }

    private int PopBag()
    {
        var index = _random.Next(_bag.Count);
        var result = _bag[index];
        _bag.RemoveAt(index);
        return result;
    }

    private void RefillBag()
    {
        _bag.Clear();

        for (var i = 0; i < _items.Count; i++)
        {
            if (i != CurrentIndex)
            {
                _bag.Add(i);
            }
        }
    }
}
