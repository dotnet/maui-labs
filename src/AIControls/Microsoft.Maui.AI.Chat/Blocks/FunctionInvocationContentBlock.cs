// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>A tool call together with its (eventual) result, matched by <c>CallId</c>.</summary>
/// <remarks>
/// Produced by <see cref="FunctionInvocationHandler"/> from M.E.AI <c>FunctionCallContent</c> and
/// <c>FunctionResultContent</c>. Subclass it (with a custom <see cref="ContentBlockHandler{TState}"/>)
/// to project a specific tool into a strongly-typed block. If the tool was not auto-invoked by the
/// chat client, <see cref="AgentContext"/> runs it and feeds the result back to the model.
/// </remarks>
public class FunctionInvocationContentBlock : ContentBlock
{
    public FunctionCallContent? Call { get; set; }

    public FunctionResultContent? Result { get; set; }

    public string? ToolName => Call?.Name;

    public IDictionary<string, object?>? Arguments => Call?.Arguments;

    public bool HasResult => Result is not null;
}
