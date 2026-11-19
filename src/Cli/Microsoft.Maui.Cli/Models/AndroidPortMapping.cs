// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Models;

/// <summary>
/// A single adb port mapping rule. <see cref="Local"/> and <see cref="Remote"/> mirror
/// adb's own <c>LOCAL</c>/<c>REMOTE</c> socket specs:
/// for a forward rule <see cref="Local"/> is the host port and <see cref="Remote"/> is the device port;
/// for a reverse rule <see cref="Local"/> is the device port and <see cref="Remote"/> is the host port.
/// </summary>
public sealed record AndroidPortMapping
{
	[JsonPropertyName("local")]
	public int Local { get; init; }

	[JsonPropertyName("remote")]
	public int Remote { get; init; }

	[JsonPropertyName("protocol")]
	public string Protocol { get; init; } = "tcp";
}

/// <summary>
/// Result of <c>maui android port list</c>.
/// </summary>
public sealed record AndroidPortListResult
{
	[JsonPropertyName("serial")]
	public string Serial { get; init; } = string.Empty;

	[JsonPropertyName("forward")]
	public List<AndroidPortMapping> Forward { get; init; } = [];

	[JsonPropertyName("reverse")]
	public List<AndroidPortMapping> Reverse { get; init; } = [];
}

/// <summary>
/// Result of a mutating <c>maui android port</c> action (forward, reverse, or clear).
/// </summary>
public sealed record AndroidPortActionResult
{
	[JsonPropertyName("success")]
	public bool Success { get; init; } = true;

	[JsonPropertyName("serial")]
	public string Serial { get; init; } = string.Empty;

	/// <summary>The action performed: <c>forward</c>, <c>reverse</c>, or <c>clear</c>.</summary>
	[JsonPropertyName("action")]
	public required string Action { get; init; }

	[JsonPropertyName("local")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Local { get; init; }

	[JsonPropertyName("remote")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Remote { get; init; }

	[JsonPropertyName("protocol")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Protocol { get; init; }

	[JsonPropertyName("cleared_forward")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? ClearedForward { get; init; }

	[JsonPropertyName("cleared_reverse")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? ClearedReverse { get; init; }

	[JsonPropertyName("message")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Message { get; init; }
}
