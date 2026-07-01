using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

/// <summary>
/// Strongly-typed block for the <c>GetCurrentWeather</c> tool.
/// <para>
/// This is the manual equivalent of what a source generator could produce:
/// a typed projection over the raw Microsoft.Extensions.AI
/// <see cref="Microsoft.Extensions.AI.FunctionCallContent"/> /
/// <see cref="Microsoft.Extensions.AI.FunctionResultContent"/> pair.
/// </para>
/// <para>
/// The <see cref="WeatherToolBlockHandler"/> populates these properties from
/// the base M.E.AI content so the view can bind to strongly-typed data instead
/// of parsing JSON inline.
/// </para>
/// </summary>
public class WeatherToolBlock : FunctionInvocationContentBlock
{
    // Parameter (from FunctionCallContent.Arguments)
    public string? City { get; set; }

    // Result properties (from FunctionResultContent.Result JSON)
    public string? Location { get; set; }
    public int Temperature { get; set; }
    public string? Conditions { get; set; }
    public string? ConditionIcon { get; set; }
    public int Humidity { get; set; }
    public int WindSpeed { get; set; }
    public int FeelsLike { get; set; }
}
