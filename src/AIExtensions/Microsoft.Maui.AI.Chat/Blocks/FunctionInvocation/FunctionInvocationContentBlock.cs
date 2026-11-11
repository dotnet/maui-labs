// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>A tool call together with its (eventual) result, matched by <c>CallId</c>.</summary>
/// <remarks>
/// Produced by <see cref="FunctionInvocationHandler"/> from M.E.AI <see cref="FunctionCallContent"/> and
/// <see cref="FunctionResultContent"/>. Subclass it (with a custom <see cref="ContentBlockHandler{TState}"/>)
/// to project a specific tool into a strongly-typed block. Server tools are invoked by
/// <see cref="FunctionInvokingChatClient"/>; the engine only renders the call and its result. Calls
/// marked <see cref="FunctionCallContent.InformationalOnly"/> (handled elsewhere, replayed, or
/// restored) are rendered too, and a turn shows each call id once even when the call reappears in
/// a later round. Setting <see cref="Call"/> raises <see cref="ContentBlock.PropertyChanged"/> for it and
/// for <see cref="ToolName"/> and <see cref="Arguments"/> when they change; setting <see cref="Result"/>
/// raises it for the result and for <see cref="HasResult"/> when that changes.
/// </remarks>
public class FunctionInvocationContentBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_callChanged = new(nameof(Call));
    private static readonly PropertyChangedEventArgs s_toolNameChanged = new(nameof(ToolName));
    private static readonly PropertyChangedEventArgs s_argumentsChanged = new(nameof(Arguments));
    private static readonly PropertyChangedEventArgs s_resultChanged = new(nameof(Result));
    private static readonly PropertyChangedEventArgs s_hasResultChanged = new(nameof(HasResult));

    private FunctionCallContent? _call;
    private FunctionResultContent? _result;

    public FunctionCallContent? Call
    {
        get => _call;
        set
        {
            var previous = _call;
            _call = value;
            if (value is not null && string.IsNullOrEmpty(Id))
            {
                Id = string.IsNullOrEmpty(value.CallId)
                    ? Guid.NewGuid().ToString("N")
                    : value.CallId;
            }

            if (ReferenceEquals(previous, value))
                return;

            RaisePropertyChanged(s_callChanged);
            if (!string.Equals(previous?.Name, value?.Name, StringComparison.Ordinal))
                RaisePropertyChanged(s_toolNameChanged);
            if (!ReferenceEquals(previous?.Arguments, value?.Arguments))
                RaisePropertyChanged(s_argumentsChanged);
        }
    }

    public FunctionResultContent? Result
    {
        get => _result;
        set
        {
            var hadResult = HasResult;
            if (SetResultSilently(value))
                ReportResultChanged(hadResult);
        }
    }

    public string? ToolName => Call?.Name;

    public IDictionary<string, object?>? Arguments => Call?.Arguments;

    public bool HasResult => Result is not null;

    /// <summary>Sets <see cref="Result"/> without raising its change, which the caller reports later.</summary>
    /// <returns>Whether the result changed.</returns>
    internal bool SetResultSilently(FunctionResultContent? value)
    {
        if (ReferenceEquals(_result, value))
            return false;

        _result = value;
        return true;
    }

    /// <summary>Raises the change of <see cref="Result"/>, and of <see cref="HasResult"/> when it changed.</summary>
    internal void ReportResultChanged(bool hadResult)
    {
        RaisePropertyChanged(s_resultChanged);
        if (HasResult != hadResult)
            RaisePropertyChanged(s_hasResultChanged);
    }
}
