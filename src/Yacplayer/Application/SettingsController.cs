using Yacplayer.Configuration;
using Yacplayer.Library;

namespace Yacplayer.Application;

internal enum SettingsItem
{
    Volume,
    Muted,
    Fps,
    FftSize,
    VisualizerMode,
    ReactiveUiMode,
    RealtimeAnalysis,
    RepeatMode,
    Shuffle,
    Recursive,
    LastDirectory,
    StartupDirectory,
    SchemaVersion
}

internal readonly record struct SettingsViewState(
    bool Visible,
    int SelectedIndex,
    bool EditingPath,
    string EditBuffer,
    string Message);

internal readonly record struct SettingsInteraction(
    bool Changed,
    bool CloseRequested,
    bool BrowserDirectoryChanged,
    string Message);

internal sealed class SettingsController(PlayerSettings settings)
{
    private const int MaximumPathLength = 1024;
    private int _selectedIndex;
    private bool _visible;
    private bool _editingPath;
    private string _editBuffer = "";
    private string _message = "Changes apply and save immediately";

    public static IReadOnlyList<SettingsItem> Items { get; } = Enum.GetValues<SettingsItem>();

    public SettingsViewState View => new(
        _visible,
        _selectedIndex,
        _editingPath,
        _editBuffer,
        _message);

    public void Open()
    {
        _visible = true;
        _editingPath = false;
        _message = "Changes apply and save immediately";
    }

    public void Report(string message)
    {
        _message = message;
    }

    public SettingsInteraction Handle(ConsoleKeyInfo key)
    {
        if (!_visible)
        {
            return default;
        }

        return _editingPath
            ? HandlePathEdit(key)
            : HandleNavigation(key);
    }

    private SettingsInteraction HandleNavigation(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.O:
                _visible = false;
                return new(false, true, false, "Settings closed");
            case ConsoleKey.UpArrow:
                _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return default;
            case ConsoleKey.DownArrow:
                _selectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
                return default;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                return ChangeSelected(-1);
            case ConsoleKey.RightArrow:
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
            case ConsoleKey.Spacebar:
                return ChangeSelected(1);
            case ConsoleKey.Enter:
                if (SelectedItem is SettingsItem.LastDirectory or SettingsItem.StartupDirectory)
                {
                    BeginPathEdit();
                    return default;
                }

                return ChangeSelected(1);
            default:
                return default;
        }
    }

    private SettingsInteraction ChangeSelected(int direction)
    {
        switch (SelectedItem)
        {
            case SettingsItem.Volume:
                settings.Volume = Math.Clamp(settings.Volume + direction * 5, 0, 100);
                break;
            case SettingsItem.Muted:
                settings.Muted = !settings.Muted;
                break;
            case SettingsItem.Fps:
                settings.Fps = settings.Fps == 30 ? 60 : 30;
                break;
            case SettingsItem.FftSize:
                settings.FftSize = settings.FftSize == 2048 ? 4096 : 2048;
                break;
            case SettingsItem.VisualizerMode:
                settings.VisualizerMode = Cycle(settings.VisualizerMode, direction);
                break;
            case SettingsItem.ReactiveUiMode:
                settings.ReactiveUiMode = Cycle(settings.ReactiveUiMode, direction);
                break;
            case SettingsItem.RealtimeAnalysis:
                settings.RealtimeAnalysisEnabled = !settings.RealtimeAnalysisEnabled;
                break;
            case SettingsItem.RepeatMode:
                settings.RepeatMode = Cycle(settings.RepeatMode, direction);
                break;
            case SettingsItem.Shuffle:
                settings.Shuffle = !settings.Shuffle;
                break;
            case SettingsItem.Recursive:
                settings.Recursive = !settings.Recursive;
                break;
            case SettingsItem.LastDirectory:
            case SettingsItem.StartupDirectory:
                BeginPathEdit();
                return default;
            case SettingsItem.SchemaVersion:
                _message = "Settings schema version is read-only";
                return new(false, false, false, _message);
        }

        settings.Validate();
        _message = $"{DisplayName(SelectedItem)} updated";
        return new(true, false, false, _message);
    }

    private void BeginPathEdit()
    {
        _editingPath = true;
        _editBuffer = SelectedItem == SettingsItem.StartupDirectory
            ? settings.StartupDirectory
            : settings.LastDirectory;
        _message = SelectedItem == SettingsItem.StartupDirectory
            ? "Enter a folder path; empty means use the last browser folder"
            : "Enter a folder path";
    }

    private SettingsInteraction HandlePathEdit(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _editingPath = false;
                _editBuffer = "";
                _message = "Path edit cancelled";
                return default;
            case ConsoleKey.Backspace:
                if (_editBuffer.Length > 0)
                {
                    _editBuffer = _editBuffer[..^1];
                }

                return default;
            case ConsoleKey.Enter:
                return CommitPath();
        }

        if (!char.IsControl(key.KeyChar) && _editBuffer.Length < MaximumPathLength)
        {
            _editBuffer += key.KeyChar;
        }

        return default;
    }

    private SettingsInteraction CommitPath()
    {
        var value = _editBuffer.Trim().Trim('"');
        if (SelectedItem == SettingsItem.LastDirectory && value.Length == 0)
        {
            _message = "Last browser folder cannot be empty";
            return new(false, false, false, _message);
        }

        if (value.Length > 0)
        {
            try
            {
                value = Path.GetFullPath(value);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _message = "Invalid folder path";
                return new(false, false, false, _message);
            }

            if (!Directory.Exists(value))
            {
                _message = "Folder does not exist or is not accessible";
                return new(false, false, false, _message);
            }
        }

        var browserDirectoryChanged = SelectedItem == SettingsItem.LastDirectory;
        if (SelectedItem == SettingsItem.StartupDirectory)
        {
            settings.StartupDirectory = value;
        }
        else
        {
            settings.LastDirectory = value;
        }

        _editingPath = false;
        _editBuffer = "";
        _message = $"{DisplayName(SelectedItem)} updated";
        return new(true, false, browserDirectoryChanged, _message);
    }

    private SettingsItem SelectedItem => Items[_selectedIndex];

    internal static string DisplayName(SettingsItem item) => item switch
    {
        SettingsItem.Volume => "Volume",
        SettingsItem.Muted => "Muted",
        SettingsItem.Fps => "UI frame rate",
        SettingsItem.FftSize => "FFT size",
        SettingsItem.VisualizerMode => "Visualizer",
        SettingsItem.ReactiveUiMode => "Reactive UI",
        SettingsItem.RealtimeAnalysis => "Realtime analysis",
        SettingsItem.RepeatMode => "Repeat mode",
        SettingsItem.Shuffle => "Shuffle",
        SettingsItem.Recursive => "Recursive scan",
        SettingsItem.LastDirectory => "Last browser folder",
        SettingsItem.StartupDirectory => "Startup folder",
        SettingsItem.SchemaVersion => "Settings schema",
        _ => item.ToString()
    };

    private static T Cycle<T>(T value, int direction) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var current = Array.IndexOf(values, value);
        var next = (current + Math.Sign(direction) + values.Length) % values.Length;
        return values[next];
    }
}
