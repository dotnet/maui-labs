// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Represents the plugin.json manifest for a single plugin in the marketplace.
/// </summary>
internal sealed class PluginManifest
{
	/// <summary>
	/// Name of the plugin.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Version of the plugin (e.g. "0.1.0").
	/// </summary>
	public string? Version { get; set; }

	/// <summary>
	/// Optional description of the plugin.
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Relative paths to skill directories or skill definition globs.
	/// Accepts both a single string and an array in plugin.json.
	/// </summary>
	[JsonConverter(typeof(StringOrArrayConverter))]
	public string[] Skills { get; set; } = [];

	/// <summary>
	/// Optional relative paths to agent definition directories.
	/// Accepts both a single string and an array in plugin.json.
	/// </summary>
	[JsonConverter(typeof(StringOrArrayConverter))]
	public string[]? Agents { get; set; }

	/// <summary>
	/// Optional relative path to an LSP server configuration file.
	/// </summary>
	public string? LspServers { get; set; }
}
