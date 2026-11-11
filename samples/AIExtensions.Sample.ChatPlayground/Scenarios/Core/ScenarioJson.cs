using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>JSON settings and readers shared by the scenarios' tools, block handlers and state mappers.</summary>
/// <remarks>
/// Call arguments and results arrive in different forms: as <see cref="JsonElement"/> values from a provider or a
/// recording, and as .NET values from a function that just ran. The readers accept all of them.
/// </remarks>
internal static class ScenarioJson
{
    /// <summary>Gets the options the scenarios' tools use: the Microsoft.Extensions.AI defaults plus the scenario types.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Reads a value as JSON.</summary>
    public static JsonElement? ToElement(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement element:
                return element;
            case string text when text.TrimStart() is ['{' or '[', ..]:
                try
                {
                    using var document = JsonDocument.Parse(text);
                    return document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return JsonSerializer.SerializeToElement(text, Options.GetTypeInfo(typeof(string)));
                }
            default:
                return JsonSerializer.SerializeToElement(value, Options.GetTypeInfo(value.GetType()));
        }
    }

    /// <summary>Reads a value as an object of type <typeparamref name="T"/>, or <see langword="null"/> when it is not one.</summary>
    public static T? Read<T>(object? value)
        where T : class
    {
        if (value is T typed)
            return typed;
        if (ToElement(value) is not { ValueKind: JsonValueKind.Object or JsonValueKind.Array } element)
            return null;

        try
        {
            return element.Deserialize((JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a named call argument as text.</summary>
    public static string? ReadString(IDictionary<string, object?>? arguments, string name) =>
        arguments is not null && arguments.TryGetValue(name, out var value) ? AsString(value) : null;

    /// <summary>Reads a named call argument as a whole number.</summary>
    public static int? ReadInt(IDictionary<string, object?>? arguments, string name) =>
        arguments is not null && arguments.TryGetValue(name, out var value) ? AsInt(value) : null;

    /// <summary>Reads a value as text; JSON other than a string is returned as JSON.</summary>
    public static string? AsString(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Reads a value as a whole number.</summary>
    public static int? AsInt(object? value) => value switch
    {
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        double number when !double.IsNaN(number) => (int)Math.Round(number),
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var number) =>
            (int)Math.Round(number),
        JsonElement { ValueKind: JsonValueKind.String } element => ParseInt(element.GetString()),
        string text => ParseInt(text),
        _ => null,
    };

    /// <summary>Reads a value as a yes or no.</summary>
    public static bool? AsBool(object? value) => value switch
    {
        bool flag => flag,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString(), out var flag) => flag,
        string text when bool.TryParse(text, out var flag) => flag,
        _ => null,
    };

    /// <summary>Reads a value as a list of strings: a JSON array, or one string.</summary>
    public static IReadOnlyList<string> AsStrings(object? value)
    {
        if (value is IEnumerable<string> strings)
            return [.. strings];
        if (ToElement(value) is not { } element)
            return [];

        return element.ValueKind switch
        {
            JsonValueKind.Array => [.. element.EnumerateArray().Select(static item => AsString(item)).OfType<string>()],
            JsonValueKind.String when element.GetString() is { Length: > 0 } text => [text],
            _ => [],
        };
    }

    /// <summary>Formats a value as indented JSON for the model.</summary>
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));

    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Insert(0, ScenarioJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>Provides trim-safe JSON metadata for the scenarios' tool arguments, results and state.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WeatherReport))]
[JsonSerializable(typeof(LocalTimeReport))]
[JsonSerializable(typeof(PlanState))]
[JsonSerializable(typeof(PlanStep))]
[JsonSerializable(typeof(PlanStepUpdate))]
[JsonSerializable(typeof(RecipeState))]
[JsonSerializable(typeof(RecipeIngredient))]
[JsonSerializable(typeof(MeetingBooking))]
[JsonSerializable(typeof(DocumentState))]
[JsonSerializable(typeof(List<string>))]
internal partial class ScenarioJsonContext : JsonSerializerContext;
