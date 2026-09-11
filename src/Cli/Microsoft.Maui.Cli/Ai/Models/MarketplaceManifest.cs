// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Represents the top-level marketplace.json manifest that lists available plugins.
/// </summary>
internal sealed class MarketplaceManifest
{
	/// <summary>
	/// Name of the marketplace repository.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Owner information for the marketplace.
	/// </summary>
	public MarketplaceOwner? Owner { get; set; }

	/// <summary>
	/// List of plugin entries available in the marketplace.
	/// </summary>
	public PluginEntry[] Plugins { get; set; } = [];
}

/// <summary>
/// Represents the marketplace owner.
/// </summary>
internal sealed class MarketplaceOwner
{
	/// <summary>
	/// Name of the owning organization or team.
	/// </summary>
	public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Represents a single plugin entry in the marketplace manifest.
/// </summary>
internal sealed class PluginEntry
{
	/// <summary>
	/// Name of the plugin.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Relative path to the plugin source directory within the marketplace repository.
	/// </summary>
	public string Source { get; set; } = string.Empty;

	/// <summary>
	/// Optional description of the plugin.
	/// </summary>
	public string? Description { get; set; }
}
