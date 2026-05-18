using Microsoft.AspNetCore.Components.AI;

namespace AiControlsSample;

/// <summary>
/// Strongly-typed tool block for the GetCurrentWeather function.
/// The source generator creates a handler that:
/// 1. Matches FunctionCallContent with Name == "GetCurrentWeather"
/// 2. Deserializes the "city" argument into the City property
/// 3. Matches the corresponding FunctionResultContent by CallId
/// 4. Deserializes result properties (location, temperature, etc.)
/// </summary>
[ToolBlock("GetCurrentWeather")]
public partial class WeatherToolBlock : FunctionInvocationContentBlock
{
    // Parameters (deserialized from FunctionCallContent.Arguments)
    [ToolParameter(Name = "city")]
    public string? City { get; set; }

    // Result properties (deserialized from FunctionResultContent.Result JSON)
    [ToolResult(Name = "location")]
    public string? Location { get; set; }

    [ToolResult(Name = "temperature")]
    public int Temperature { get; set; }

    [ToolResult(Name = "conditions")]
    public string? Conditions { get; set; }

    [ToolResult(Name = "conditionIcon")]
    public string? ConditionIcon { get; set; }

    [ToolResult(Name = "humidity")]
    public int Humidity { get; set; }

    [ToolResult(Name = "windSpeed")]
    public int WindSpeed { get; set; }

    [ToolResult(Name = "feelsLike")]
    public int FeelsLike { get; set; }
}
