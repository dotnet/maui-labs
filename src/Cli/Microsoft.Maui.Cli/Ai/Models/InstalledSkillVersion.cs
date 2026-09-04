// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Represents the content of a .skill-version JSON file that tracks
/// installed skill metadata. Backward compatible with the legacy format
/// that only contains commit, updatedAt, and branch fields.
/// </summary>
internal sealed class InstalledSkillVersion
{
	/// <summary>
	/// Name of the installed skill.
	/// </summary>
	public string? Name { get; set; }

	/// <summary>
	/// Git commit SHA at the time of installation.
	/// </summary>
	public string? Commit { get; set; }

	/// <summary>
	/// Branch from which the skill was installed.
	/// </summary>
	public string? Branch { get; set; }

	/// <summary>
	/// ISO 8601 timestamp of when the skill was last updated.
	/// </summary>
	public string? UpdatedAt { get; set; }

	/// <summary>
	/// Source repository identifier (e.g. "owner/repo").
	/// </summary>
	public string? Source { get; set; }

	/// <summary>
	/// Relative path to the plugin within the marketplace repository.
	/// </summary>
	public string? PluginPath { get; set; }
}
