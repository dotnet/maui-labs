// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Output;

internal sealed record StatusMessageResult
{
	[JsonPropertyName("status")]
	public required string Status { get; init; }

	[JsonPropertyName("message")]
	public required string Message { get; init; }

	[JsonPropertyName("percentage")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Percentage { get; init; }
}

internal sealed record VersionResult
{
	[JsonPropertyName("version")]
	public required string Version { get; init; }

	[JsonPropertyName("runtime")]
	public required string Runtime { get; init; }

	[JsonPropertyName("os")]
	public required string Os { get; init; }
}

internal sealed record CliCommandResult
{
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("status")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Status { get; init; }

	[JsonPropertyName("message")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Message { get; init; }

	[JsonPropertyName("name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Name { get; init; }

	[JsonPropertyName("package")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Package { get; init; }

	[JsonPropertyName("device")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Device { get; init; }

	[JsonPropertyName("serial")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Serial { get; init; }

	[JsonPropertyName("path")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Path { get; init; }

	[JsonPropertyName("command")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Command { get; init; }

	[JsonPropertyName("arguments")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Arguments { get; init; }

	[JsonPropertyName("full_command")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? FullCommand { get; init; }

	[JsonPropertyName("version")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Version { get; init; }

	[JsonPropertyName("elevated")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Elevated { get; init; }

	[JsonPropertyName("installed")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? Installed { get; init; }

	[JsonPropertyName("uninstalled")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? Uninstalled { get; init; }

	[JsonPropertyName("versions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<int>? Versions { get; init; }
}

internal sealed record SimulatorCreateResult
{
	[JsonPropertyName("udid")]
	public required string Udid { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("device_type")]
	public required string DeviceType { get; init; }

	[JsonPropertyName("runtime")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Runtime { get; init; }
}

/// <summary>
/// Result of a successful simulator erase. The <see cref="Erased"/> field is always <c>true</c>;
/// failure is reported via a MauiToolException before this model is emitted.
/// </summary>
internal sealed record SimulatorEraseResult
{
	[JsonPropertyName("target")]
	public required string Target { get; init; }

	[JsonPropertyName("erased")]
	public bool Erased { get; init; }
}

internal sealed record SimulatorAppResult
{
	[JsonPropertyName("udid")]
	public required string Udid { get; init; }

	[JsonPropertyName("bundle_identifier")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? BundleIdentifier { get; init; }

	[JsonPropertyName("app_path")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? AppPath { get; init; }

	[JsonPropertyName("action")]
	public required string Action { get; init; }

	[JsonPropertyName("success")]
	public bool Success { get; init; }
}

internal sealed record SimulatorAppContainerResult
{
	[JsonPropertyName("udid")]
	public required string Udid { get; init; }

	[JsonPropertyName("bundle_identifier")]
	public required string BundleIdentifier { get; init; }

	[JsonPropertyName("container_type")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ContainerType { get; init; }

	[JsonPropertyName("path")]
	public required string Path { get; init; }
}
