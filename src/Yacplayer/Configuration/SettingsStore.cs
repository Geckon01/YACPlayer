using System.Text.Json;
using System.Text.Json.Serialization;
namespace Yacplayer.Configuration;
public sealed class SettingsStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yacplayer", "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public async Task<PlayerSettings> LoadAsync(CancellationToken ct = default)
    {
        try { if (File.Exists(Path)) { var value = JsonSerializer.Deserialize<PlayerSettings>(await File.ReadAllTextAsync(Path, ct), Options) ?? new(); value.Validate(); return value; } }
        catch (JsonException) { try { File.Move(Path, Path + ".bad", true); } catch (IOException) { } }
        catch (IOException) { }
        return new();
    }
    public async Task SaveAsync(PlayerSettings value, CancellationToken ct = default)
    {
        value.Validate(); var directory = System.IO.Path.GetDirectoryName(Path)!; Directory.CreateDirectory(directory);
        var temporary = Path + ".tmp"; await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options), ct); File.Move(temporary, Path, true);
    }
}
