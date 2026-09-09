// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>A tool call together with its (eventual) result, matched by <c>CallId</c>.</summary>
/// <remarks>
/// Produced by <see cref="FunctionInvocationHandler"/> from M.E.AI <see cref="FunctionCallContent"/> and
/// <see cref="FunctionResultContent"/>. Subclass it (with a custom <see cref="ContentBlockHandler{TState}"/>)
/// to project a specific tool into a strongly-typed block. If the tool was not auto-invoked by the
/// chat client, <see cref="AgentContext"/> runs it and feeds the result back to the model.
/// </remarks>
public class FunctionInvocationContentBlock : ContentBlock
{
    private FunctionCallContent? _call;

    public FunctionCallContent? Call
    {
        get => _call;
        set
        {
            _call = value;
            if (value is not null && string.IsNullOrEmpty(Id))
            {
                Id = string.IsNullOrEmpty(value.CallId)
                    ? Guid.NewGuid().ToString("N")
                    : value.CallId;
            }
        }
    }

    public FunctionResultContent? Result { get; set; }

    public string? ToolName => Call?.Name;

    public IDictionary<string, object?>? Arguments => Call?.Arguments;

    public bool HasResult => Result is not null;
}
