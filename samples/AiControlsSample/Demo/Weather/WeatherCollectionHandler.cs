using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

/// <summary>
/// Maps every <c>GetCurrentWeather</c> call/result in a turn into a single
/// <see cref="WeatherCollectionBlock"/>. Registered with
/// <c>options.AddBlockHandler(new WeatherCollectionHandler())</c>.
/// </summary>
/// <remarks>
/// The pipeline gives active blocks first claim on new content, and a handler that never returns
/// <c>Complete</c> stays active for the whole turn. So this handler:
/// <list type="number">
///   <item>emits the collection on the first weather call (<c>Emit</c>);</item>
///   <item>folds each additional weather call and result into the same block (<c>Update</c>);</item>
///   <item>passes on everything else so other content (text, other tools) flows to other handlers.</item>
/// </list>
/// A single city simply produces a one-item collection.
/// </remarks>
public sealed class WeatherCollectionHandler : ContentBlockHandler<WeatherCollectionBlock>
{
    private const string ToolName = "GetCurrentWeather";

    public override BlockMappingResult<WeatherCollectionBlock> Handle(
        BlockMappingContext context, WeatherCollectionBlock state)
    {
        // Snapshot the unhandled content before marking, so claiming items mid-iteration is safe.
        var unhandled = new List<AIContent>();
        foreach (var content in context.UnhandledContents)
            unhandled.Add(content);

        var claimed = false;

        foreach (var content in unhandled)
        {
            switch (content)
            {
                // A weather call for a (possibly new) city — add a pending item.
                case FunctionCallContent call when call.Name == ToolName:
                    context.MarkHandled(call);
                    var item = state.GetOrAdd(call.CallId);
                    item.City = GetStringArgument(call, "city");
                    claimed = true;
                    break;

                // A result for a call we already added — fill it in.
                case FunctionResultContent result when state.Find(result.CallId) is { } existing:
                    context.MarkHandled(result);
                    PopulateFromResult(existing, result);
                    existing.HasResult = true;
                    claimed = true;
                    break;
            }
        }

        if (!claimed)
            return BlockMappingResult<WeatherCollectionBlock>.Pass();

        if (string.IsNullOrEmpty(state.Id))
        {
            state.Id = Guid.NewGuid().ToString("N");
            return BlockMappingResult<WeatherCollectionBlock>.Emit(state, state);
        }

        return BlockMappingResult<WeatherCollectionBlock>.Update(state);
    }

    private static string? GetStringArgument(FunctionCallContent call, string key)
    {
        if (call.Arguments is { } args && args.TryGetValue(key, out var value) && value is not null)
        {
            return value switch
            {
                JsonElement je => je.GetString(),
                string s => s,
                _ => value.ToString(),
            };
        }
        return null;
    }

    private static void PopulateFromResult(WeatherItem item, FunctionResultContent result)
    {
        // GetCurrentWeather returns a JSON object, arriving as a JsonElement or a JSON string.
        switch (result.Result)
        {
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                Populate(item, je);
                return;
            case string s when !string.IsNullOrWhiteSpace(s):
                using (var doc = JsonDocument.Parse(s))
                    Populate(item, doc.RootElement);
                return;
            default:
                var raw = result.Result?.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                    return;
                using (var doc = JsonDocument.Parse(raw))
                    Populate(item, doc.RootElement);
                return;
        }
    }

    private static void Populate(WeatherItem item, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("location", out var location))
            item.Location = location.GetString();
        if (root.TryGetProperty("temperature", out var temperature))
            item.Temperature = temperature.GetInt32();
        if (root.TryGetProperty("conditions", out var conditions))
            item.Conditions = conditions.GetString();
        if (root.TryGetProperty("conditionIcon", out var icon))
            item.ConditionIcon = icon.GetString();
        if (root.TryGetProperty("humidity", out var humidity))
            item.Humidity = humidity.GetInt32();
        if (root.TryGetProperty("windSpeed", out var windSpeed))
            item.WindSpeed = windSpeed.GetInt32();
        if (root.TryGetProperty("feelsLike", out var feelsLike))
            item.FeelsLike = feelsLike.GetInt32();
    }
}
