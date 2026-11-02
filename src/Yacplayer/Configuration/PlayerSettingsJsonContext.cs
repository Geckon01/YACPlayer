using System.Text.Json.Serialization;

namespace Yacplayer.Configuration;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PlayerSettings))]
internal partial class PlayerSettingsJsonContext : JsonSerializerContext;
