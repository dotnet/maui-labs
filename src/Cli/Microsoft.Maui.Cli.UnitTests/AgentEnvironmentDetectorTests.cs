// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AgentEnvironmentDetectorTests : IDisposable
{
	private readonly string _tempDir;

	public AgentEnvironmentDetectorTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	[Fact]
	public void Detect_ClaudeDirectoryExists_DetectsClaudeEnvironment()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".claude"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);

		Assert.Contains(environments, e => e.Kind == AgentEnvironmentKind.Claude);
	}

	[Fact]
	public void Detect_VsCodeDirectoryExists_DetectsVsCodeEnvironment()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".vscode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);

		Assert.Contains(environments, e => e.Kind == AgentEnvironmentKind.VsCode);
	}

	[Fact]
	public void Detect_OpenCodeDirectoryExists_DetectsOpenCodeEnvironment()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".opencode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);

		Assert.Contains(environments, e => e.Kind == AgentEnvironmentKind.OpenCode);
	}

	[Fact]
	public void Detect_NoAgentDirectories_ReturnsEmptyOrOnlyCopilotCli()
	{
		// No .claude, .vscode, or .opencode directories
		var environments = AgentEnvironmentDetector.Detect(_tempDir);

		// Only CopilotCli might be detected (if ~/.copilot exists on the machine)
		Assert.DoesNotContain(environments, e => e.Kind == AgentEnvironmentKind.Claude);
		Assert.DoesNotContain(environments, e => e.Kind == AgentEnvironmentKind.VsCode);
		Assert.DoesNotContain(environments, e => e.Kind == AgentEnvironmentKind.OpenCode);
	}

	[Fact]
	public void Detect_MultipleEnvironments_DetectsAll()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".claude"));
		Directory.CreateDirectory(Path.Combine(_tempDir, ".vscode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);

		Assert.Contains(environments, e => e.Kind == AgentEnvironmentKind.Claude);
		Assert.Contains(environments, e => e.Kind == AgentEnvironmentKind.VsCode);
	}

	[Fact]
	public void Detect_Claude_SkillsDirectoryIsCorrect()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".claude"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);
		var claude = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.Claude);

		var expected = Path.Combine(_tempDir, ".claude", "skills");
		Assert.Equal(expected, claude.SkillsDirectory);
	}

	[Fact]
	public void Detect_Claude_McpConfigPathIsCorrect()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".claude"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);
		var claude = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.Claude);

		var expected = Path.Combine(_tempDir, ".claude", "mcp.json");
		Assert.Equal(expected, claude.McpConfigPath);
	}

	[Fact]
	public void Detect_VsCode_SkillsDirectoryUsesGitHubPath()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".vscode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);
		var vscode = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.VsCode);

		var expected = Path.Combine(_tempDir, ".github", "skills");
		Assert.Equal(expected, vscode.SkillsDirectory);
	}

	[Fact]
	public void Detect_VsCode_McpConfigPathIsCorrect()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".vscode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);
		var vscode = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.VsCode);

		var expected = Path.Combine(_tempDir, ".vscode", "mcp.json");
		Assert.Equal(expected, vscode.McpConfigPath);
	}

	[Fact]
	public void Detect_OpenCode_SkillsDirectoryIsCorrect()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".opencode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);
		var opencode = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.OpenCode);

		var expected = Path.Combine(_tempDir, ".opencode", "skills");
		Assert.Equal(expected, opencode.SkillsDirectory);
	}

	[Fact]
	public void Detect_OpenCode_McpConfigPathIsCorrect()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".opencode"));

		var environments = AgentEnvironmentDetector.Detect(_tempDir);
		var opencode = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.OpenCode);

		var expected = Path.Combine(_tempDir, ".opencode", "config.json");
		Assert.Equal(expected, opencode.McpConfigPath);
	}

	[Fact]
	public void Detect_McpConfigExists_ReflectsRealFilePresence()
	{
		var claudeDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(claudeDir);

		// Before creating mcp.json
		var envBefore = AgentEnvironmentDetector.Detect(_tempDir);
		var claudeBefore = Assert.Single(envBefore, e => e.Kind == AgentEnvironmentKind.Claude);
		Assert.False(claudeBefore.McpConfigExists);

		// After creating mcp.json
		File.WriteAllText(Path.Combine(claudeDir, "mcp.json"), "{}");
		var envAfter = AgentEnvironmentDetector.Detect(_tempDir);
		var claudeAfter = Assert.Single(envAfter, e => e.Kind == AgentEnvironmentKind.Claude);
		Assert.True(claudeAfter.McpConfigExists);
	}

	[Fact]
	public void Detect_StopsAtGitRoot()
	{
		// Create a git root with .claude at the root level
		Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
		Directory.CreateDirectory(Path.Combine(_tempDir, ".claude"));

		// Create a subdirectory to scan from
		var subDir = Path.Combine(_tempDir, "src", "project");
		Directory.CreateDirectory(subDir);

		var environments = AgentEnvironmentDetector.Detect(subDir);

		// Should still find .claude at the git root
		Assert.Contains(environments, e => e.Kind == AgentEnvironmentKind.Claude);
	}

	[Fact]
	public void Detect_StopsAtGitRootFileForWorktree()
	{
		File.WriteAllText(Path.Combine(_tempDir, ".git"), "gitdir: ../repo/.git/worktrees/project");
		Directory.CreateDirectory(Path.Combine(_tempDir, ".vscode"));

		var subDir = Path.Combine(_tempDir, "src", "project");
		Directory.CreateDirectory(subDir);

		var environments = AgentEnvironmentDetector.Detect(subDir);
		var vscode = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.VsCode);

		Assert.Equal(Path.Combine(_tempDir, ".github", "skills"), vscode.SkillsDirectory);
	}

	[Fact]
	public void ResolveProjectRoot_ReturnsGitRootFromSubdirectory()
	{
		File.WriteAllText(Path.Combine(_tempDir, ".git"), "gitdir: ../repo/.git/worktrees/project");
		var subDir = Path.Combine(_tempDir, "src", "project");
		Directory.CreateDirectory(subDir);

		var projectRoot = AgentEnvironmentDetector.ResolveProjectRoot(subDir);

		Assert.Equal(_tempDir, projectRoot);
	}

	[Fact]
	public void Detect_NoGitRoot_DoesNotScanAncestorDirectories()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".vscode"));
		var subDir = Path.Combine(_tempDir, "src", "project");
		Directory.CreateDirectory(subDir);

		var environments = AgentEnvironmentDetector.Detect(subDir);

		Assert.DoesNotContain(environments, e => e.Kind == AgentEnvironmentKind.VsCode);
	}

	[Fact]
	public void GetCopilotCliEnvironment_EmptyUserHome_ReturnsNull()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".copilot"));

		var environment = AgentEnvironmentDetector.GetCopilotCliEnvironment(string.Empty, _tempDir);

		Assert.Null(environment);
	}

	[Fact]
	public void GetCopilotCliEnvironment_ValidUserHome_ReturnsCopilotCliEnvironment()
	{
		var userHome = Path.Combine(_tempDir, "home");
		Directory.CreateDirectory(Path.Combine(userHome, ".copilot"));

		var environment = AgentEnvironmentDetector.GetCopilotCliEnvironment(userHome, _tempDir);

		Assert.NotNull(environment);
		Assert.Equal(AgentEnvironmentKind.CopilotCli, environment.Kind);
		Assert.Equal(Path.Combine(_tempDir, ".github", "skills"), environment.SkillsDirectory);
		Assert.Equal(Path.Combine(userHome, ".copilot", "mcp.json"), environment.McpConfigPath);
	}
}
