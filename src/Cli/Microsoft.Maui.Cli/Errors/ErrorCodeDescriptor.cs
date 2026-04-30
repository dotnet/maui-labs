// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Errors;

public sealed record ErrorCodeDescriptor
{
[JsonPropertyName("code")]
public required string Code { get; init; }

[JsonPropertyName("name")]
public required string Name { get; init; }

[JsonPropertyName("category")]
public required string Category { get; init; }

[JsonPropertyName("subcategory")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? Subcategory { get; init; }

[JsonPropertyName("description")]
public required string Description { get; init; }

[JsonPropertyName("default_remediation_type")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? DefaultRemediationType { get; init; }
}

public sealed record ErrorCodeCatalogueResult
{
[JsonPropertyName("codes")]
public required IReadOnlyList<ErrorCodeDescriptor> Codes { get; init; }

[JsonPropertyName("count")]
public int Count => Codes.Count;
}