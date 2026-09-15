// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Supported agent environment kinds that can be detected on the local machine.
/// </summary>
internal enum AgentEnvironmentKind
{
	/// <summary>Claude Code agent.</summary>
	Claude,

	/// <summary>VS Code with GitHub Copilot extension.</summary>
	VsCode,

	/// <summary>GitHub Copilot CLI agent.</summary>
	CopilotCli,

	/// <summary>OpenCode agent.</summary>
	OpenCode
}

/// <summary>
/// Represents a detected agent environment and its configuration paths for
/// skill installation and MCP server registration.
/// </summary>
internal sealed class DetectedEnvironment
{
	public string ReasonCode { get; set; } = "detected-marker";
	public string? MarkerPath { get; set; }
	public string Scope { get; set; } = "project";

	internal System.Text.Json.Nodes.JsonObject ToJson() => new()
	{
		["name"] = Kind.ToString(), ["reasonCode"] = ReasonCode, ["markerPath"] = MarkerPath,
		["scope"] = Scope, ["skillsPath"] = SkillsDirectory, ["mcpPath"] = McpConfigPath,
		["mcpScope"] = Kind == AgentEnvironmentKind.CopilotCli ? "user" : "project"
	};

	/// <summary>
	/// Kind of agent environment detected.
	/// </summary>
	public AgentEnvironmentKind Kind { get; set; }

	/// <summary>
	/// Absolute path to the directory where skills should be installed.
	/// </summary>
	public string SkillsDirectory { get; set; } = string.Empty;

	/// <summary>
	/// Absolute path to the MCP configuration file.
	/// </summary>
	public string McpConfigPath { get; set; } = string.Empty;

	/// <summary>
	/// Whether the MCP configuration file already exists on disk.
	/// </summary>
	public bool McpConfigExists { get; set; }
}
