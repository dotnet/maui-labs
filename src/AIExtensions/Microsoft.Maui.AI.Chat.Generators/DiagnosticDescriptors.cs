// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace Microsoft.Maui.AI.Chat.Generators;

internal static class DiagnosticDescriptors
{
    private const string Category = "MauiAIChat";

    internal static readonly DiagnosticDescriptor NotPartial = Create(
        "MAUIAI101",
        "ToolBlock class must be partial",
        "ToolBlock class '{0}' must be declared as partial");

    internal static readonly DiagnosticDescriptor WrongBaseClass = Create(
        "MAUIAI102",
        "ToolBlock class must extend FunctionInvocationContentBlock",
        "ToolBlock class '{0}' must extend FunctionInvocationContentBlock");

    internal static readonly DiagnosticDescriptor IsAbstract = Create(
        "MAUIAI103",
        "ToolBlock class must not be abstract",
        "ToolBlock class '{0}' must not be abstract");

    internal static readonly DiagnosticDescriptor IsGeneric = Create(
        "MAUIAI104",
        "ToolBlock class must not be generic",
        "ToolBlock class '{0}' must not be generic");

    internal static readonly DiagnosticDescriptor EmptyToolName = Create(
        "MAUIAI105",
        "ToolBlock has an empty tool name",
        "ToolBlock '{0}' has an empty tool name");

    internal static readonly DiagnosticDescriptor DuplicateArgumentKey = Create(
        "MAUIAI106",
        "Duplicate ToolParameter key",
        "ToolParameter key '{0}' is already mapped by another property");

    internal static readonly DiagnosticDescriptor PropertySetterUnavailable = Create(
        "MAUIAI107",
        "ToolBlock property must have an accessible setter",
        "Property '{0}' must have a public or internal setter so generated code can populate it");

    internal static readonly DiagnosticDescriptor DuplicateToolName = Create(
        "MAUIAI108",
        "Duplicate ToolBlock tool name",
        "Tool name '{0}' is used by both '{1}' and '{2}'");

    internal static readonly DiagnosticDescriptor NestedType = Create(
        "MAUIAI109",
        "ToolBlock class must be top-level",
        "ToolBlock class '{0}' must be a top-level type");

    internal static readonly DiagnosticDescriptor MissingConstructor = Create(
        "MAUIAI110",
        "ToolBlock class needs a public parameterless constructor",
        "ToolBlock class '{0}' must have a public parameterless constructor");

    internal static readonly DiagnosticDescriptor DuplicateResultKey = Create(
        "MAUIAI111",
        "Duplicate ToolResult key",
        "ToolResult key '{0}' is already mapped by another property");

    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> ByIdMap =
        new[]
        {
            NotPartial,
            WrongBaseClass,
            IsAbstract,
            IsGeneric,
            EmptyToolName,
            DuplicateArgumentKey,
            PropertySetterUnavailable,
            DuplicateToolName,
            NestedType,
            MissingConstructor,
            DuplicateResultKey,
        }.ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);

    internal static DiagnosticDescriptor ById(string id) => ByIdMap[id];

    private static DiagnosticDescriptor Create(string id, string title, string message) =>
        new(
            id,
            title,
            message,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
}
