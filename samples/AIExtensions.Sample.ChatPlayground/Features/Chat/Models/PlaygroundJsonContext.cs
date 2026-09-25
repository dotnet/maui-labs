using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Models;

/// <summary>Provides trim-safe JSON metadata for the playground's structured response.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(PlaygroundResponse))]
internal partial class PlaygroundJsonContext : JsonSerializerContext;
