// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Detects agent environments by scanning from the working directory up to the
/// Git repository root for known configuration directories.
/// </summary>
internal static class AgentEnvironmentDetector
{
	/// <summary>
	/// Scans from <paramref name="workingDir"/> up to the Git root for known
	/// agent environments (.claude/, .vscode/, .opencode/) and checks for
	/// Copilot CLI at the user level (~/.copilot/).
	/// </summary>
	/// <param name="workingDir">Directory to start scanning from.</param>
	/// <returns>List of detected environments (may be empty).</returns>
	public static List<DetectedEnvironment> Detect(string workingDir)
	{
		var environments = new List<DetectedEnvironment>();
		var gitRoot = FindGitRoot(workingDir);
		var searchRoot = gitRoot ?? Path.GetFullPath(workingDir);
		var foundKinds = new HashSet<AgentEnvironmentKind>();

		var current = new DirectoryInfo(workingDir);
		var rootFullPath = gitRoot is not null ? Path.GetFullPath(gitRoot) : null;

		while (current is not null)
		{
			var dir = current.FullName;

			if (!foundKinds.Contains(AgentEnvironmentKind.Claude) &&
				Directory.Exists(Path.Combine(dir, ".claude")))
			{
				foundKinds.Add(AgentEnvironmentKind.Claude);
				environments.Add(new DetectedEnvironment
				{
					Kind = AgentEnvironmentKind.Claude,
					SkillsDirectory = Path.Combine(dir, ".claude", "skills"),
					McpConfigPath = Path.Combine(dir, ".claude", "mcp.json"),
					McpConfigExists = File.Exists(Path.Combine(dir, ".claude", "mcp.json"))
				});
			}

			if (!foundKinds.Contains(AgentEnvironmentKind.VsCode) &&
				Directory.Exists(Path.Combine(dir, ".vscode")))
			{
				foundKinds.Add(AgentEnvironmentKind.VsCode);
				environments.Add(new DetectedEnvironment
				{
					Kind = AgentEnvironmentKind.VsCode,
					SkillsDirectory = Path.Combine(dir, ".github", "skills"),
					McpConfigPath = Path.Combine(dir, ".vscode", "mcp.json"),
					McpConfigExists = File.Exists(Path.Combine(dir, ".vscode", "mcp.json"))
				});
			}

			if (!foundKinds.Contains(AgentEnvironmentKind.OpenCode) &&
				Directory.Exists(Path.Combine(dir, ".opencode")))
			{
				foundKinds.Add(AgentEnvironmentKind.OpenCode);
				environments.Add(new DetectedEnvironment
				{
					Kind = AgentEnvironmentKind.OpenCode,
					SkillsDirectory = Path.Combine(dir, ".opencode", "skills"),
					McpConfigPath = Path.Combine(dir, ".opencode", "config.json"),
					McpConfigExists = File.Exists(Path.Combine(dir, ".opencode", "config.json"))
				});
			}

			// Stop at the Git root.
			if (rootFullPath is null)
				break;

			if (string.Equals(current.FullName, rootFullPath, PathComparison))
				break;

			current = current.Parent;
		}

		// Copilot CLI is detected at the user level.
		var copilotCliEnvironment = GetCopilotCliEnvironment(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			searchRoot);
		if (copilotCliEnvironment is not null)
			environments.Add(copilotCliEnvironment);

		return environments;
	}

	internal static string ResolveProjectRoot(string workingDir)
		=> FindGitRoot(workingDir) ?? Path.GetFullPath(workingDir);

	/// <summary>
	/// Walks up from <paramref name="startDir"/> looking for a <c>.git</c> directory.
	/// </summary>
	internal static string? FindGitRoot(string startDir)
	{
		var current = new DirectoryInfo(startDir);
		while (current is not null)
		{
			if (IsGitRoot(current.FullName))
				return current.FullName;

			current = current.Parent;
		}

		return null;
	}

	private static bool IsGitRoot(string directory)
	{
		var gitPath = Path.Combine(directory, ".git");
		return Directory.Exists(gitPath) || File.Exists(gitPath);
	}

	internal static DetectedEnvironment? GetCopilotCliEnvironment(string userHome, string searchRoot)
	{
		if (string.IsNullOrEmpty(userHome))
			return null;

		var copilotDir = Path.Combine(userHome, ".copilot");
		if (!Directory.Exists(copilotDir))
			return null;

		var mcpPath = Path.Combine(copilotDir, "mcp.json");
		return new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.CopilotCli,
			SkillsDirectory = Path.Combine(searchRoot, ".github", "skills"),
			McpConfigPath = mcpPath,
			McpConfigExists = File.Exists(mcpPath)
		};
	}

	static StringComparison PathComparison =>
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
