using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

/// <summary>
/// Maps the raw Microsoft.Extensions.AI content for the <c>GetCurrentWeather</c>
/// tool into a strongly-typed <see cref="WeatherToolBlock"/>.
/// <para>
/// This is a hand-written custom block handler. It plugs into the same pipeline
/// the built-in handlers use and is registered with
/// <c>options.AddBlockHandler(new WeatherToolBlockHandler())</c>.
/// </para>
/// <list type="number">
///   <item>Phase 1 — match the <see cref="FunctionCallContent"/> whose name is
///   <c>GetCurrentWeather</c>, deserialize the <c>city</c> argument, and emit the block.</item>
///   <item>Phase 2 — match the corresponding <see cref="FunctionResultContent"/> by
///   <c>CallId</c>, parse the JSON result, and complete the block.</item>
/// </list>
/// </summary>
public sealed class WeatherToolBlockHandler : ContentBlockHandler<WeatherToolBlock>
{
    private const string ToolName = "GetCurrentWeather";

    public override BlockMappingResult<WeatherToolBlock> Handle(
        BlockMappingContext context, WeatherToolBlock state)
    {
        // Phase 1: claim the matching function call and emit the typed block.
        if (state.Call is null)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionCallContent call && call.Name == ToolName)
                {
                    context.MarkHandled(call);
                    state.Call = call;
                    state.Id = call.CallId;
                    state.City = GetStringArgument(call, "city");
                    return BlockMappingResult<WeatherToolBlock>.Emit(state, state);
                }
            }
        }

        // Phase 2: claim the matching result (by CallId) and complete the block.
        foreach (var content in context.UnhandledContents)
        {
            if (content is FunctionResultContent result
                && state.Call is not null
                && result.CallId == state.Call.CallId)
            {
                context.MarkHandled(result);
                state.Result = result;
                PopulateFromResult(state, result);
                return BlockMappingResult<WeatherToolBlock>.Complete();
            }
        }

        return BlockMappingResult<WeatherToolBlock>.Pass();
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

    private static void PopulateFromResult(WeatherToolBlock state, FunctionResultContent result)
    {
        // The GetCurrentWeather tool returns a JSON object, which arrives either as a
        // JsonElement or as a JSON string depending on the client. Handle both.
        JsonElement root;
        switch (result.Result)
        {
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                root = je;
                break;
            case string s when !string.IsNullOrWhiteSpace(s):
                using (var doc = JsonDocument.Parse(s))
                {
                    Populate(state, doc.RootElement);
                }
                return;
            default:
                var raw = result.Result?.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                    return;
                using (var doc = JsonDocument.Parse(raw))
                {
                    Populate(state, doc.RootElement);
                }
                return;
        }

        Populate(state, root);
    }

    private static void Populate(WeatherToolBlock state, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("location", out var location))
            state.Location = location.GetString();
        if (root.TryGetProperty("temperature", out var temperature))
            state.Temperature = temperature.GetInt32();
        if (root.TryGetProperty("conditions", out var conditions))
            state.Conditions = conditions.GetString();
        if (root.TryGetProperty("conditionIcon", out var icon))
            state.ConditionIcon = icon.GetString();
        if (root.TryGetProperty("humidity", out var humidity))
            state.Humidity = humidity.GetInt32();
        if (root.TryGetProperty("windSpeed", out var windSpeed))
            state.WindSpeed = windSpeed.GetInt32();
        if (root.TryGetProperty("feelsLike", out var feelsLike))
            state.FeelsLike = feelsLike.GetInt32();
    }
}
