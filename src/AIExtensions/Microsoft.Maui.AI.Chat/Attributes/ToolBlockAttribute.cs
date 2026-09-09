// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Generates a block handler that maps one named function call and its result into the annotated
/// <see cref="FunctionInvocationContentBlock"/> subclass.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ToolBlockAttribute : Attribute
{
    public ToolBlockAttribute(string toolName) => ToolName = toolName;

    public string ToolName { get; }
}
