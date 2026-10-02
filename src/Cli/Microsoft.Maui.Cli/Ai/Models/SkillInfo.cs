// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Represents a discovered skill from the marketplace, including metadata
/// parsed from SKILL.md frontmatter and the list of associated files.
/// </summary>
internal sealed class SkillInfo
{
	/// <summary>
	/// Name of the skill (from SKILL.md frontmatter or directory name).
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Optional description of the skill parsed from SKILL.md frontmatter.
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Name of the parent plugin that contains this skill.
	/// </summary>
	public string PluginName { get; set; } = string.Empty;

	/// <summary>
	/// Repository-root-relative path of the skill directory in the marketplace.
	/// </summary>
	public string RemotePath { get; set; } = string.Empty;

	/// <summary>
	/// List of repository-root-relative file paths that belong to this skill.
	/// </summary>
	public List<string> Files { get; set; } = [];
}
