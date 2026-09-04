// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// End-to-end integration tests for the <c>maui ai</c> feature that exercise
/// full filesystem flows without any network calls. Uses real SKILL.md content,
/// real directory structures, and verifies file creation, version tracking,
/// MCP config, and idempotency.
/// </summary>
public class AiIntegrationTests : IDisposable
{
	private readonly string _tempRoot;

	public AiIntegrationTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "maui-ai-integration-" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempRoot);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempRoot))
			Directory.Delete(_tempRoot, recursive: true);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 1. Environment Detection → Full Cycle
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public void EnvironmentDetection_ClaudeAndVsCode_FullCycle()
	{
		var projectDir = Path.Combine(_tempRoot, "project1");
		Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));
		Directory.CreateDirectory(Path.Combine(projectDir, ".vscode"));

		var environments = AgentEnvironmentDetector.Detect(projectDir);

		var claude = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.Claude);
		var vscode = Assert.Single(environments, e => e.Kind == AgentEnvironmentKind.VsCode);

		// Paths are absolute
		Assert.True(Path.IsPathRooted(claude.SkillsDirectory));
		Assert.True(Path.IsPathRooted(claude.McpConfigPath));
		Assert.True(Path.IsPathRooted(vscode.SkillsDirectory));
		Assert.True(Path.IsPathRooted(vscode.McpConfigPath));

		// Paths point to the expected locations
		Assert.Equal(Path.Combine(projectDir, ".claude", "skills"), claude.SkillsDirectory);
		Assert.Equal(Path.Combine(projectDir, ".claude", "mcp.json"), claude.McpConfigPath);
		Assert.Equal(Path.Combine(projectDir, ".github", "skills"), vscode.SkillsDirectory);
		Assert.Equal(Path.Combine(projectDir, ".vscode", "mcp.json"), vscode.McpConfigPath);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 2. SkillVersionStore Round-Trip with Real Content
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task SkillVersionStore_RoundTrip_AllFieldsPreserved()
	{
		var skillDir = Path.Combine(_tempRoot, "versions", "devflow-connect");
		var version = new InstalledSkillVersion
		{
			Name = "devflow-connect",
			Commit = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
			Branch = "main",
			UpdatedAt = "2025-07-14T10:30:00.0000000Z",
			Source = "dotnet/maui-labs",
			PluginPath = ".github/plugins/dotnet-maui/skills/devflow-connect"
		};

		await SkillVersionStore.WriteAsync(skillDir, version);
		var result = await SkillVersionStore.ReadAsync(skillDir);

		Assert.NotNull(result);
		Assert.Equal(version.Name, result.Name);
		Assert.Equal(version.Commit, result.Commit);
		Assert.Equal(version.Branch, result.Branch);
		Assert.Equal(version.UpdatedAt, result.UpdatedAt);
		Assert.Equal(version.Source, result.Source);
		Assert.Equal(version.PluginPath, result.PluginPath);

		// File is valid JSON
		var filePath = Path.Combine(skillDir, ".skill-version");
		Assert.True(File.Exists(filePath));
		var json = await File.ReadAllTextAsync(filePath);
		var node = JsonNode.Parse(json);
		Assert.NotNull(node);

		// File is in the correct location
		Assert.StartsWith(skillDir, Path.GetDirectoryName(filePath)!);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 3. McpConfigurator Creates Correct Claude Config
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task McpConfigurator_Claude_CreatesCorrectConfig()
	{
		var projectDir = Path.Combine(_tempRoot, "claude-config");
		var claudeDir = Path.Combine(projectDir, ".claude");
		Directory.CreateDirectory(claudeDir);
		var configPath = Path.Combine(claudeDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(claudeDir, "skills"),
			McpConfigPath = configPath,
			McpConfigExists = false
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		Assert.True(File.Exists(configPath));

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		Assert.NotNull(json);

		var server = json["mcpServers"]?["maui-devflow"];
		Assert.NotNull(server);
		Assert.Equal("maui", server["command"]?.GetValue<string>());

		var args = server["args"]?.AsArray();
		Assert.NotNull(args);
		Assert.Equal(2, args.Count);
		Assert.Equal("devflow", args[0]?.GetValue<string>());
		Assert.Equal("mcp", args[1]?.GetValue<string>());
	}

	// ──────────────────────────────────────────────────────────────────────
	// 4. McpConfigurator Creates Correct OpenCode Config
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task McpConfigurator_OpenCode_CreatesCorrectConfig()
	{
		var projectDir = Path.Combine(_tempRoot, "opencode-config");
		var openCodeDir = Path.Combine(projectDir, ".opencode");
		Directory.CreateDirectory(openCodeDir);
		var configPath = Path.Combine(openCodeDir, "config.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.OpenCode,
			SkillsDirectory = Path.Combine(openCodeDir, "skills"),
			McpConfigPath = configPath,
			McpConfigExists = false
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		Assert.True(File.Exists(configPath));

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		Assert.NotNull(json);

		// OpenCode uses mcp.servers (different schema from Claude's mcpServers)
		var server = json["mcp"]?["servers"]?["maui-devflow"];
		Assert.NotNull(server);
		Assert.Equal("maui", server["command"]?.GetValue<string>());

		var args = server["args"]?.AsArray();
		Assert.NotNull(args);
		Assert.Equal(2, args.Count);
		Assert.Equal("devflow", args[0]?.GetValue<string>());
		Assert.Equal("mcp", args[1]?.GetValue<string>());

		// Verify it does NOT use the standard mcpServers key
		Assert.Null(json["mcpServers"]);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 5. McpConfigurator Preserves Existing Entries
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task McpConfigurator_PreservesExistingEntries()
	{
		var projectDir = Path.Combine(_tempRoot, "preserve-entries");
		var claudeDir = Path.Combine(projectDir, ".claude");
		Directory.CreateDirectory(claudeDir);
		var configPath = Path.Combine(claudeDir, "mcp.json");

		// Write an existing config with a custom server entry
		var existing = new JsonObject
		{
			["mcpServers"] = new JsonObject
			{
				["my-custom-server"] = new JsonObject
				{
					["command"] = "custom-tool",
					["args"] = new JsonArray("serve", "--port", "9999")
				}
			}
		};
		await File.WriteAllTextAsync(configPath, existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(claudeDir, "skills"),
			McpConfigPath = configPath,
			McpConfigExists = true
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var servers = json?["mcpServers"]?.AsObject();
		Assert.NotNull(servers);

		// Custom entry still present
		var custom = servers["my-custom-server"];
		Assert.NotNull(custom);
		Assert.Equal("custom-tool", custom["command"]?.GetValue<string>());

		// maui-devflow added alongside it
		var maui = servers["maui-devflow"];
		Assert.NotNull(maui);
		Assert.Equal("maui", maui["command"]?.GetValue<string>());
	}

	// ──────────────────────────────────────────────────────────────────────
	// 6. McpConfigurator Idempotent — Run Twice
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task McpConfigurator_Idempotent_RunTwice()
	{
		var projectDir = Path.Combine(_tempRoot, "idempotent");
		var claudeDir = Path.Combine(projectDir, ".claude");
		Directory.CreateDirectory(claudeDir);
		var configPath = Path.Combine(claudeDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(claudeDir, "skills"),
			McpConfigPath = configPath,
			McpConfigExists = false
		};

		// First run
		var result1 = await McpConfigurator.ConfigureAsync(env);
		Assert.True(result1);
		var contentAfterFirst = await File.ReadAllTextAsync(configPath);

		// Second run
		var result2 = await McpConfigurator.ConfigureAsync(env);
		Assert.True(result2);
		var contentAfterSecond = await File.ReadAllTextAsync(configPath);

		// File unchanged — no duplicate entries
		Assert.Equal(contentAfterFirst, contentAfterSecond);

		// Verify only one maui-devflow entry exists
		var json = JsonNode.Parse(contentAfterSecond);
		var servers = json?["mcpServers"]?.AsObject();
		Assert.NotNull(servers);
		var mauiEntries = servers.Where(kv => kv.Key == "maui-devflow").ToList();
		Assert.Single(mauiEntries);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 7. ParseFrontmatter with YAML Block Scalar
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public void ParseFrontmatter_YamlBlockScalar_JoinsWithSpaces()
	{
		var content = "---\nname: block-scalar-skill\ndescription: >-\n  multi\n  line\n  text\n---\n# Body\n";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("block-scalar-skill", name);
		Assert.Equal("multi line text", description);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 8. ParseFrontmatter with Real SKILL.md from Marketplace
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public void ParseFrontmatter_RealSkillMd_DevflowConnect()
	{
		var content = """
			---
			name: devflow-connect
			description: >-
			  Diagnose and fix DevFlow agent connectivity issues between the maui CLI
			  and running .NET MAUI apps. USE FOR: "maui devflow" connection failures,
			  agent not found, port conflicts, adb forwarding issues on Android,
			  broker discovery problems.
			---
			# DevFlow Connect Troubleshooter
			
			This skill helps diagnose and resolve connectivity issues with the
			.NET MAUI DevFlow agent.
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("devflow-connect", name);
		Assert.NotNull(description);
		Assert.Contains("DevFlow agent connectivity", description);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 9. NormalizePath Handles Middle './' Segments (tested indirectly)
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task NormalizePath_MiddleDotSlash_HandledIndirectly()
	{
		// NormalizePath is private, so we test it indirectly via GetPluginAsync.
		// When pluginSourcePath contains "./" segments, the URL should be cleaned.
		// We set up a mock handler that captures the requested URL.
		string? capturedUrl = null;

		var handler = new UrlCapturingHandler(url =>
		{
			capturedUrl = url;
			// Return a valid plugin.json response
			var pluginJson = """{"name":"test-plugin","skills":["skills"]}""";
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(pluginJson, Encoding.UTF8, "application/json")
			};
		});

		using var http = new HttpClient(handler);

		// The path "plugins/dotnet-maui/./skills/" should be normalized to "plugins/dotnet-maui/skills"
		await MarketplaceClient.GetPluginAsync(http, "owner/repo", "main", "plugins/dotnet-maui/./skills/");

		Assert.NotNull(capturedUrl);
		// The URL should NOT contain "/./" — the path should be normalized
		Assert.DoesNotContain("/./", capturedUrl);
		// The URL should contain the normalized path
		Assert.Contains("plugins/dotnet-maui/skills/plugin.json", capturedUrl);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 10. Full Init Simulation (filesystem only)
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task FullInitSimulation_InstallsSkillWithMockHttp()
	{
		var projectDir = Path.Combine(_tempRoot, "full-init");
		var skillsDir = Path.Combine(projectDir, ".claude", "skills");
		Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));

		var skillMdContent = """
			---
			name: devflow-connect
			description: Diagnose DevFlow agent connectivity issues
			---
			# DevFlow Connect
			
			Use this skill to troubleshoot DevFlow agent connections.
			""";

		var commitJson = """[{"sha":"abc123def456"}]""";

		var handler = new MockHttpMessageHandler();
		handler.AddResponse("raw.githubusercontent.com", skillMdContent);
		handler.AddResponse("api.github.com/repos/dotnet/maui-labs/commits", commitJson);

		using var http = new HttpClient(handler);

		var skill = new SkillInfo
		{
			Name = "devflow-connect",
			Description = "Diagnose DevFlow agent connectivity issues",
			PluginName = "dotnet-maui",
			RemotePath = ".github/plugins/dotnet-maui/skills/devflow-connect",
			Files = [".github/plugins/dotnet-maui/skills/devflow-connect/SKILL.md"]
		};

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = skillsDir,
			McpConfigPath = Path.Combine(projectDir, ".claude", "mcp.json"),
			McpConfigExists = false
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, projectDir, "dotnet/maui-labs", "main", force: true);

		Assert.Equal(1, filesInstalled);
		Assert.Equal(Path.Combine(skillsDir, "devflow-connect"), installPath);

		// SKILL.md exists with correct content
		var skillMdPath = Path.Combine(installPath, "SKILL.md");
		Assert.True(File.Exists(skillMdPath));
		var writtenContent = await File.ReadAllTextAsync(skillMdPath);
		Assert.Contains("DevFlow Connect", writtenContent);

		// .skill-version exists
		var versionPath = Path.Combine(installPath, ".skill-version");
		Assert.True(File.Exists(versionPath));

		var version = await SkillVersionStore.ReadAsync(installPath);
		Assert.NotNull(version);
		Assert.Equal("devflow-connect", version.Name);
		Assert.Equal("abc123def456", version.Commit);
		Assert.Equal("main", version.Branch);
		Assert.Equal("dotnet/maui-labs", version.Source);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 11. Dry-Run — SkillInstaller with force=false and existing version
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task SkillInstaller_DryRun_SkipsWhenVersionExists()
	{
		var projectDir = Path.Combine(_tempRoot, "dry-run");
		var skillsDir = Path.Combine(projectDir, ".claude", "skills");
		var skillDir = Path.Combine(skillsDir, "devflow-connect");
		Directory.CreateDirectory(skillDir);

		// Write .skill-version manually to simulate a previous install
		var version = new InstalledSkillVersion
		{
			Name = "devflow-connect",
			Commit = "existing-commit-sha",
			Branch = "main",
			UpdatedAt = "2025-07-01T00:00:00Z",
			Source = "dotnet/maui-labs",
			PluginPath = ".github/plugins/dotnet-maui/skills/devflow-connect"
		};
		await SkillVersionStore.WriteAsync(skillDir, version);

		// Use a handler that records whether any request was made
		var handler = new RequestTrackingHandler();
		using var http = new HttpClient(handler);

		var skill = new SkillInfo
		{
			Name = "devflow-connect",
			RemotePath = ".github/plugins/dotnet-maui/skills/devflow-connect",
			Files = [".github/plugins/dotnet-maui/skills/devflow-connect/SKILL.md"]
		};

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = skillsDir,
			McpConfigPath = Path.Combine(projectDir, ".claude", "mcp.json")
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, projectDir, "dotnet/maui-labs", "main", force: false);

		// Returns (0, path) — skipped
		Assert.Equal(0, filesInstalled);
		Assert.Equal(skillDir, installPath);

		// No HTTP calls made
		Assert.Equal(0, handler.RequestCount);
	}

	// ──────────────────────────────────────────────────────────────────────
	// 12. Invalid Skill Name Rejected
	// ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task SkillInstaller_InvalidSkillName_Rejected()
	{
		var projectDir = Path.Combine(_tempRoot, "invalid-name");
		var skillsDir = Path.Combine(projectDir, ".claude", "skills");
		Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));

		var skill = new SkillInfo
		{
			Name = "../malicious",
			RemotePath = "some/remote/path",
			Files = ["some/remote/path/SKILL.md"]
		};

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = skillsDir,
			McpConfigPath = Path.Combine(projectDir, ".claude", "mcp.json")
		};

		using var http = new HttpClient();

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, projectDir, "owner/repo", "main", force: false);

		// Returns (-1, "") — rejected before any file operations
		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);

		// No files created
		Assert.False(Directory.Exists(skillsDir));
	}

	// ──────────────────────────────────────────────────────────────────────
	// Test helper: MockHttpMessageHandler
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns predefined responses based on URL substring matching.
	/// Avoids any real network calls while testing the full install pipeline.
	/// </summary>
	private sealed class MockHttpMessageHandler : HttpMessageHandler
	{
		private readonly Dictionary<string, (byte[] Content, HttpStatusCode Status)> _responses = new();

		public void AddResponse(string urlContains, string content, HttpStatusCode status = HttpStatusCode.OK)
			=> _responses[urlContains] = (Encoding.UTF8.GetBytes(content), status);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			foreach (var (key, value) in _responses)
			{
				if (request.RequestUri?.ToString().Contains(key) == true)
					return Task.FromResult(new HttpResponseMessage(value.Status)
						{ Content = new ByteArrayContent(value.Content) });
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}

	/// <summary>
	/// Tracks whether any HTTP requests were made, returns 404 for all.
	/// </summary>
	private sealed class RequestTrackingHandler : HttpMessageHandler
	{
		public int RequestCount { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			RequestCount++;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}

	/// <summary>
	/// Captures the requested URL and returns a configurable response.
	/// Used to verify URL normalization behavior indirectly.
	/// </summary>
	private sealed class UrlCapturingHandler : HttpMessageHandler
	{
		private readonly Func<string, HttpResponseMessage> _responseFactory;

		public UrlCapturingHandler(Func<string, HttpResponseMessage> responseFactory)
			=> _responseFactory = responseFactory;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var url = request.RequestUri?.ToString() ?? string.Empty;
			return Task.FromResult(_responseFactory(url));
		}
	}
}
