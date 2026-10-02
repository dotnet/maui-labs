// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Represents a repository-hosted AI development asset, such as a Copilot agent definition.
/// </summary>
internal sealed class RepositoryAssetInfo
{
	/// <summary>
	/// Display name of the asset.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Asset category, for example "agent".
	/// </summary>
	public string Category { get; set; } = string.Empty;

	/// <summary>
	/// Optional description parsed from frontmatter.
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Repository-root-relative source path.
	/// </summary>
	public string RemotePath { get; set; } = string.Empty;

	/// <summary>
	/// Project-root-relative destination directory.
	/// </summary>
	public string DestinationRoot { get; set; } = string.Empty;

	/// <summary>
	/// Repository-root-relative files that belong to this asset.
	/// </summary>
	public List<string> Files { get; set; } = [];
}
