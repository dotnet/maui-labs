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
	internal static string? UserHomeOverrideForTests { get; set; }
	internal static string UserHome => UserHomeOverrideForTests ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	internal sealed record Descriptor(AgentEnvironmentKind Kind, string SkillsPath, string McpPath, string McpContainer, bool SupportsAgents);
	internal static readonly Descriptor[] Descriptors =
	[
		new(AgentEnvironmentKind.Claude, ".claude/skills", ".mcp.json", "mcpServers", false),
		new(AgentEnvironmentKind.VsCode, ".github/skills", ".vscode/mcp.json", "servers", true),
		new(AgentEnvironmentKind.CopilotCli, ".github/skills", ".copilot/mcp-config.json", "mcpServers", true),
		new(AgentEnvironmentKind.OpenCode, ".opencode/skills", "opencode.json", "mcp", false)
	];
	internal static Descriptor Describe(AgentEnvironmentKind kind) => Descriptors.Single(d => d.Kind == kind);
	internal static bool Supports(AgentEnvironmentKind environment, AiAssetKind kind) => kind != AiAssetKind.Agent || Describe(environment).SupportsAgents;
	internal static DetectedEnvironment Canonical(AgentEnvironmentKind kind, string projectRoot)
	{
		var descriptor = Describe(kind);
		var config = Path.Combine(kind == AgentEnvironmentKind.CopilotCli ? UserHome : projectRoot, descriptor.McpPath);
		return new() { Kind = kind, SkillsDirectory = Path.Combine(projectRoot, descriptor.SkillsPath), McpConfigPath = config, McpConfigExists = File.Exists(config) };
	}
	/// <summary>
	/// Scans from <paramref name="workingDir"/> up to the Git root for known
	/// agent configuration directories and project configuration files, and checks for
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
				(Directory.Exists(Path.Combine(dir, ".claude")) ||
				 File.Exists(Path.Combine(dir, ".mcp.json"))))
			{
				foundKinds.Add(AgentEnvironmentKind.Claude);
				environments.Add(new DetectedEnvironment
				{
					Kind = AgentEnvironmentKind.Claude,
					SkillsDirectory = Path.Combine(dir, ".claude", "skills"),
					McpConfigPath = Path.Combine(dir, ".mcp.json"),
					McpConfigExists = File.Exists(Path.Combine(dir, ".mcp.json"))
				});
			}

			if (!foundKinds.Contains(AgentEnvironmentKind.VsCode) &&
				(Directory.Exists(Path.Combine(dir, ".vscode")) ||
				 Directory.Exists(Path.Combine(dir, ".github", "skills")) ||
				 Directory.Exists(Path.Combine(dir, ".github", "agents"))))
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
				(Directory.Exists(Path.Combine(dir, ".opencode")) ||
				 File.Exists(Path.Combine(dir, "opencode.json")) ||
				 File.Exists(Path.Combine(dir, "opencode.jsonc"))))
			{
				var configPath = File.Exists(Path.Combine(dir, "opencode.jsonc"))
					? Path.Combine(dir, "opencode.jsonc")
					: Path.Combine(dir, "opencode.json");
				foundKinds.Add(AgentEnvironmentKind.OpenCode);
				environments.Add(new DetectedEnvironment
				{
					Kind = AgentEnvironmentKind.OpenCode,
					SkillsDirectory = Path.Combine(dir, ".opencode", "skills"),
					McpConfigPath = configPath,
					McpConfigExists = File.Exists(configPath)
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
			UserHome,
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

		var mcpPath = Path.Combine(copilotDir, "mcp-config.json");
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
