// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;

namespace Microsoft.Maui.AI;

/// <summary>
/// Tracks a single tool invocation (function call) and its eventual result.
/// </summary>
public sealed class InvocationContext
{
    /// <summary>The unique identifier for this function call.</summary>
    public string CallId { get; }

    /// <summary>The function name that was invoked.</summary>
    public string Name { get; }

    /// <summary>Arguments passed to the function call.</summary>
    public IDictionary<string, object?>? Arguments { get; }

    /// <summary>Whether a result has been provided for this invocation.</summary>
    public bool HasResult { get; private set; }

    /// <summary>The result value, if any.</summary>
    public object? Result { get; private set; }

    /// <summary>Fires when <see cref="SetResult"/> is called.</summary>
    public event Action? ResultArrived;

    public InvocationContext(string callId, string name, IDictionary<string, object?>? arguments)
    {
        CallId = callId;
        Name = name;
        Arguments = arguments;
    }

    /// <summary>Retrieves a typed argument by name, or default if missing.</summary>
    public T? GetArgument<T>(string name)
    {
        if (Arguments is null || !Arguments.TryGetValue(name, out object? value))
            return default;

        return CoerceValue<T>(value);
    }

    /// <summary>Returns the result cast to <typeparamref name="T"/>.</summary>
    public T? GetResult<T>()
    {
        if (!HasResult || Result is null)
            return default;

        return CoerceValue<T>(Result);
    }

    /// <summary>Sets the result and fires <see cref="ResultArrived"/>.</summary>
    public void SetResult(object? result)
    {
        Result = result;
        HasResult = true;
        ResultArrived?.Invoke();
    }

    private static T? CoerceValue<T>(object? value)
    {
        if (value is T typed)
            return typed;

        // JsonElement is the most common type from M.E.AI tool arguments
        if (value is JsonElement element)
        {
            try
            {
                return element.Deserialize<T>();
            }
            catch
            {
                return default;
            }
        }

        try
        {
            return (T?)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }
}
