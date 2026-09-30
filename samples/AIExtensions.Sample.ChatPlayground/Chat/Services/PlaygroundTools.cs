using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Supplies local functions that a real model may invoke.</summary>
public sealed class PlaygroundTools
{
    public PlaygroundTools(Func<ConnectionStatusResult> getConnectionStatus)
    {
        ArgumentNullException.ThrowIfNull(getConnectionStatus);
        ConnectionStatus = AIFunctionFactory.Create(
            getConnectionStatus,
            "get_connection_status",
            "Reports this device's network access and active connection types. Call only when the user asks whether this device is online or what connection type it is using; this does not test a specific website.");
    }

    /// <summary>Gets the current local date and time function.</summary>
    public AITool CurrentLocalDateTime { get; } = AIFunctionFactory.Create(
        () => DateTimeOffset.Now.ToString("O"),
        "get_current_local_datetime",
        "Always use when the actual user request asks for the current local date or time; " +
            "the model cannot know the current value without this tool. Do not use for quoted, " +
            "hypothetical, negated, or analyzed date/time text.");

    /// <summary>Gets the calculator function.</summary>
    public AITool Calculator { get; } = AIFunctionFactory.Create(
        Calculate,
        "calculate",
        "Use only when the actual user request asks to calculate a value. Performs one deterministic " +
            "arithmetic operation: add, subtract, multiply, or divide. Do not use for quoted, " +
            "hypothetical, negated, or analyzed arithmetic text.");

    /// <summary>Gets the device's current network access and connection types.</summary>
    public AITool ConnectionStatus { get; }

    private static double Calculate(
        [Description("The first number in the requested calculation.")] double left,
        [Description("The requested operation: add, subtract, multiply, or divide.")] string operation,
        [Description("The second number in the requested calculation.")] double right) =>
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
