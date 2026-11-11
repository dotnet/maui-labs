using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// One card for all the <c>get_weather</c> calls of a response: a row per city, which fills in when that city's result
/// arrives. <see cref="WeatherComparisonHandler"/> creates and updates it.
/// </summary>
public sealed class WeatherComparisonBlock : ContentBlock
{
    private readonly List<WeatherComparisonRow> _rows = [];

    /// <summary>Gets a row per call, in the order the calls arrived.</summary>
    public IReadOnlyList<WeatherComparisonRow> Rows => _rows;

    /// <summary>Gets how many rows have their result.</summary>
    public int CompletedCount => _rows.Count(static row => row.IsComplete);

    internal WeatherComparisonRow? Find(string callId) =>
        _rows.FirstOrDefault(row => string.Equals(row.CallId, callId, StringComparison.Ordinal));

    internal void Add(WeatherComparisonRow row) => _rows.Add(row);
}

/// <summary>One city of a <see cref="WeatherComparisonBlock"/>.</summary>
public sealed class WeatherComparisonRow(FunctionCallContent call)
{
    /// <summary>Gets the call this row shows.</summary>
    public FunctionCallContent Call { get; } = call;

    /// <summary>Gets the call's id, which its result carries.</summary>
    public string CallId => Call.CallId;

    /// <summary>Gets the city the call asked about.</summary>
    public string City { get; } = ScenarioJson.ReadString(call.Arguments, "city") ?? "Unknown city";

    /// <summary>Gets the weather, once the result arrived and could be read.</summary>
    public WeatherReport? Report { get; private set; }

    /// <summary>Gets the result, once it arrived.</summary>
    public FunctionResultContent? Result { get; private set; }

    /// <summary>Gets why the result could not be shown, such as an error from the tool.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets whether the row has its result.</summary>
    public bool IsComplete => Result is not null;

    internal void Apply(FunctionResultContent result)
    {
        Result = result;
        Report = result.Exception is null ? ScenarioJson.Read<WeatherReport>(result.Result) : null;
        Error = Report is null
            ? result.Exception?.Message ?? ScenarioJson.AsString(result.Result) ?? "No result."
            : null;
    }
}

/// <summary>
/// Combines tool calls into one view: folds every <c>get_weather</c> call of a response, and their results, into a
/// single <see cref="WeatherComparisonBlock"/> instead of a card per call.
/// </summary>
/// <remarks>
/// The first update with a call emits the block. The handler then stays active for the rest of the response, so the
/// engine offers it each later update first: further calls add rows and each result fills its row in. A custom block
/// is not one of the engine's function blocks, so the handler claims the results of its calls itself; nothing else
/// would show them.
/// </remarks>
public sealed class WeatherComparisonHandler : ContentBlockHandler<WeatherComparisonBlock>
{
    /// <summary>The name of the tool whose calls the handler combines.</summary>
    public const string ToolName = "get_weather";

    /// <inheritdoc />
    public override BlockMappingResult<WeatherComparisonBlock> Handle(
        BlockMappingContext context, WeatherComparisonBlock state)
    {
        var changed = false;
        foreach (var content in context.UnhandledContents)
        {
            switch (content)
            {
                case FunctionCallContent { Name: ToolName } call:
                    context.MarkHandled(call);
                    if (state.Find(call.CallId) is null)
                        state.Add(new WeatherComparisonRow(call));
                    changed = true;
                    break;

                case FunctionResultContent result when state.Find(result.CallId) is { } row:
                    context.MarkHandled(result);
                    row.Apply(result);
                    changed = true;
                    break;
            }
        }

        if (!changed)
            return BlockMappingResult<WeatherComparisonBlock>.Pass();

        if (string.IsNullOrEmpty(state.Id))
        {
            state.Id = "weather-comparison-" + (context.Update.MessageId ?? Guid.NewGuid().ToString("N"));
            return BlockMappingResult<WeatherComparisonBlock>.Emit(state, state);
        }

        return BlockMappingResult<WeatherComparisonBlock>.Update(state);
    }
}
