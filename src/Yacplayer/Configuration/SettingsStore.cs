using System.Text.Json;

namespace Yacplayer.Configuration;

public sealed class SettingsStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yacplayer",
        "settings.json");

    public async Task<PlayerSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = await File.ReadAllTextAsync(Path, ct);
                var value = JsonSerializer.Deserialize(
                    json,
                    PlayerSettingsJsonContext.Default.PlayerSettings) ?? new();

                value.Validate();
                return value;
            }
        }
        catch (JsonException)
        {
            try
            {
                File.Move(Path, Path + ".bad", true);
            }
            catch (IOException)
            {
            }
        }
        catch (IOException)
        {
        }

        return new();
    }

    public async Task SaveAsync(PlayerSettings value, CancellationToken ct = default)
    {
        value.Validate();

        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);

        var temporary = Path + ".tmp";
        var json = JsonSerializer.Serialize(
            value,
            PlayerSettingsJsonContext.Default.PlayerSettings);

        await File.WriteAllTextAsync(temporary, json, ct);
        File.Move(temporary, Path, true);
    }
}
