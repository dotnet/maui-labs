using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

[JsonConverter(typeof(DevFlowThemeJsonConverter))]
public enum DevFlowTheme
{
    System,
    Light,
    Dark,
}

[JsonConverter(typeof(ThemeSetScopeJsonConverter))]
public enum ThemeSetScope
{
    Auto,
    App,
    System,
}

public sealed class ThemeResult
{
    private bool? _success;

    [JsonPropertyName("theme")]
    public DevFlowTheme Theme { get; init; }

    [JsonPropertyName("requestedTheme")]
    public DevFlowTheme? RequestedTheme { get; init; }

    [JsonPropertyName("userAppTheme")]
    public DevFlowTheme? UserAppTheme { get; init; }

    [JsonPropertyName("effectiveTheme")]
    public DevFlowTheme? EffectiveTheme { get; init; }

    [JsonPropertyName("supportedThemes")]
    public string[]? SupportedThemes { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "app";

    [JsonPropertyName("success")]
    public bool Success
    {
        get => _success ?? true;
        init => _success = value;
    }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class DevFlowThemeJsonConverter : JsonConverter<DevFlowTheme>
{
    public override DevFlowTheme Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            ThemeExtensions.TryParseTheme(reader.GetString(), out var theme))
            return theme;

        throw new JsonException("Expected theme value light, dark, or system.");
    }

    public override void Write(Utf8JsonWriter writer, DevFlowTheme value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToProtocolString());
}

public sealed class ThemeSetScopeJsonConverter : JsonConverter<ThemeSetScope>
{
    public override ThemeSetScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            ThemeExtensions.TryParseScope(reader.GetString(), out var scope))
            return scope;

        throw new JsonException("Expected theme scope value auto, app, or system.");
    }

    public override void Write(Utf8JsonWriter writer, ThemeSetScope value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToProtocolString());
}

public static class ThemeExtensions
{
    public static string ToProtocolString(this DevFlowTheme theme) => theme switch
    {
        DevFlowTheme.Light => "light",
        DevFlowTheme.Dark => "dark",
        _ => "system",
    };

    public static string ToProtocolString(this ThemeSetScope scope) => scope switch
    {
        ThemeSetScope.App => "app",
        ThemeSetScope.System => "system",
        _ => "auto",
    };

    public static bool TryParseTheme(string? value, out DevFlowTheme theme)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "light":
                theme = DevFlowTheme.Light;
                return true;
            case "dark":
                theme = DevFlowTheme.Dark;
                return true;
            case "system":
            case "default":
            case "unspecified":
            case "unset":
                theme = DevFlowTheme.System;
                return true;
            default:
                theme = DevFlowTheme.System;
                return false;
        }
    }

    public static bool TryParseScope(string? value, out ThemeSetScope scope)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "auto":
                scope = ThemeSetScope.Auto;
                return true;
            case "app":
            case "application":
                scope = ThemeSetScope.App;
                return true;
            case "system":
            case "host":
            case "device":
                scope = ThemeSetScope.System;
                return true;
            default:
                scope = ThemeSetScope.Auto;
                return false;
        }
    }
}
