// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.AI.Chat.Generators;

internal sealed class ToolPropertyModel : IEquatable<ToolPropertyModel>
{
    internal ToolPropertyModel(string propertyName, string key, string typeName)
    {
        PropertyName = propertyName;
        Key = key;
        TypeName = typeName;
    }

    internal string PropertyName { get; }
    internal string Key { get; }
    internal string TypeName { get; }

    public bool Equals(ToolPropertyModel? other) =>
        other is not null
        && PropertyName == other.PropertyName
        && Key == other.Key
        && TypeName == other.TypeName;

    public override bool Equals(object? obj) => Equals(obj as ToolPropertyModel);

    public override int GetHashCode() =>
        Hash.Combine(Hash.Combine(PropertyName, Key), TypeName.GetHashCode());
}

internal sealed class ToolBlockModel : IEquatable<ToolBlockModel>
{
    internal ToolBlockModel(
        string @namespace,
        string className,
        string fullyQualifiedType,
        string toolName,
        ImmutableArray<ToolPropertyModel> parameters,
        ImmutableArray<ToolPropertyModel> results,
        SourceLocation location)
    {
        Namespace = @namespace;
        ClassName = className;
        FullyQualifiedType = fullyQualifiedType;
        ToolName = toolName;
        Parameters = parameters;
        Results = results;
        Location = location;
    }

    internal string Namespace { get; }
    internal string ClassName { get; }
    internal string FullyQualifiedType { get; }
    internal string ToolName { get; }
    internal ImmutableArray<ToolPropertyModel> Parameters { get; }
    internal ImmutableArray<ToolPropertyModel> Results { get; }
    internal SourceLocation Location { get; }

    public bool Equals(ToolBlockModel? other) =>
        other is not null
        && Namespace == other.Namespace
        && ClassName == other.ClassName
        && FullyQualifiedType == other.FullyQualifiedType
        && ToolName == other.ToolName
        && Parameters.SequenceEqual(other.Parameters)
        && Results.SequenceEqual(other.Results)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => Equals(obj as ToolBlockModel);

    public override int GetHashCode()
    {
        var hash = Hash.Combine(Namespace, ClassName);
        hash = Hash.Combine(hash, FullyQualifiedType.GetHashCode());
        hash = Hash.Combine(hash, ToolName.GetHashCode());
        foreach (var parameter in Parameters)
            hash = Hash.Combine(hash, parameter.GetHashCode());
        foreach (var result in Results)
            hash = Hash.Combine(hash, result.GetHashCode());
        return Hash.Combine(hash, Location.GetHashCode());
    }
}

internal sealed class ParseResult : IEquatable<ParseResult>
{
    internal ParseResult(
        ToolBlockModel? model,
        ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics;
    }

    internal ToolBlockModel? Model { get; }
    internal ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    public bool Equals(ParseResult? other) =>
        other is not null
        && Equals(Model, other.Model)
        && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as ParseResult);

    public override int GetHashCode()
    {
        var hash = Model?.GetHashCode() ?? 0;
        foreach (var diagnostic in Diagnostics)
            hash = Hash.Combine(hash, diagnostic.GetHashCode());
        return hash;
    }
}

internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    internal DiagnosticInfo(
        string descriptorId,
        SourceLocation location,
        ImmutableArray<string> arguments)
    {
        DescriptorId = descriptorId;
        Location = location;
        Arguments = arguments;
    }

    internal string DescriptorId { get; }
    internal SourceLocation Location { get; }
    internal ImmutableArray<string> Arguments { get; }

    public bool Equals(DiagnosticInfo? other) =>
        other is not null
        && DescriptorId == other.DescriptorId
        && Location.Equals(other.Location)
        && Arguments.SequenceEqual(other.Arguments);

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
    {
        var hash = Hash.Combine(DescriptorId.GetHashCode(), Location.GetHashCode());
        foreach (var argument in Arguments)
            hash = Hash.Combine(hash, argument.GetHashCode());
        return hash;
    }
}

internal sealed class SourceLocation : IEquatable<SourceLocation>
{
    private SourceLocation(
        string filePath,
        TextSpan span,
        LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        Span = span;
        LineSpan = lineSpan;
    }

    private string FilePath { get; }
    private TextSpan Span { get; }
    private LinePositionSpan LineSpan { get; }

    internal static SourceLocation From(Location location) =>
        new(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span);

    internal Location ToLocation() =>
        string.IsNullOrEmpty(FilePath)
            ? Location.None
            : Location.Create(FilePath, Span, LineSpan);

    public bool Equals(SourceLocation? other) =>
        other is not null
        && FilePath == other.FilePath
        && Span.Equals(other.Span)
        && LineSpan.Equals(other.LineSpan);

    public override bool Equals(object? obj) => Equals(obj as SourceLocation);

    public override int GetHashCode() =>
        Hash.Combine(FilePath.GetHashCode(), Span.GetHashCode());
}

internal static class Hash
{
    internal static int Combine(int first, int second)
    {
        unchecked
        {
            var rotated = ((uint)first << 5) | ((uint)first >> 27);
            return ((int)rotated + first) ^ second;
        }
    }

    internal static int Combine(string? first, string? second) =>
        Combine(first?.GetHashCode() ?? 0, second?.GetHashCode() ?? 0);
}
