using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Services;

/// <summary>Supplies deterministic in-app functions that a real model may invoke.</summary>
public sealed class PlaygroundTools
{
    /// <summary>Gets the current local date and time function.</summary>
    public AITool CurrentLocalDateTime { get; } = AIFunctionFactory.Create(
        () => DateTimeOffset.Now.ToString("O"),
        "get_current_local_datetime",
        "Gets the current local date and time as an ISO 8601 timestamp.");

    /// <summary>Gets the calculator function.</summary>
    public AITool Calculator { get; } = AIFunctionFactory.Create(
        Calculate,
        "calculate",
        "Performs one deterministic arithmetic operation: add, subtract, multiply, or divide.");

    private static double Calculate(double left, string operation, double right) =>
        operation.ToLowerInvariant() switch
        {
            "add" or "+" => left + right,
            "subtract" or "-" => left - right,
            "multiply" or "*" => left * right,
            "divide" or "/" when right != 0 => left / right,
            "divide" or "/" => throw new ArgumentException("Cannot divide by zero.", nameof(right)),
            _ => throw new ArgumentException("Operation must be add, subtract, multiply, or divide.", nameof(operation)),
        };
}
