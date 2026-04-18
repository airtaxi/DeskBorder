using System.Text.Json.Serialization;

namespace DeskBorder.Models;

[JsonSerializable(typeof(DeskBorderSettings))]
[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
public partial class DeskBorderSettingsSerializationContext : JsonSerializerContext;
