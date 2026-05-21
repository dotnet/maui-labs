// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class McpConfiguratorTests : IDisposable
{
	private readonly string _tempDir;

	public McpConfiguratorTests()
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
	public async Task ConfigureAsync_CreatesNewConfigFile_WhenNoneExists()
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		Assert.True(File.Exists(configPath));

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var server = json?["mcpServers"]?["maui-devflow"];
		Assert.NotNull(server);
		Assert.Equal("maui", server["command"]?.GetValue<string>());
	}

	[Fact]
	public async Task ConfigureAsync_ServerEntryHasCorrectArgs()
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		await McpConfigurator.ConfigureAsync(env);

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var args = json?["mcpServers"]?["maui-devflow"]?["args"]?.AsArray();
		Assert.NotNull(args);
		Assert.Equal(2, args.Count);
		Assert.Equal("devflow", args[0]?.GetValue<string>());
		Assert.Equal("mcp", args[1]?.GetValue<string>());
	}

	[Fact]
	public async Task ConfigureAsync_MergesIntoExistingConfig()
	{
		var configDir = Path.Combine(_tempDir, ".vscode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");

		// Write an existing config with another server entry
		var existing = new JsonObject
		{
			["mcpServers"] = new JsonObject
			{
				["other-server"] = new JsonObject
				{
					["command"] = "other",
					["args"] = new JsonArray("arg1")
				}
			}
		};
		await File.WriteAllTextAsync(configPath, existing.ToJsonString());

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.VsCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".github", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var servers = json?["mcpServers"]?.AsObject();
		Assert.NotNull(servers);

		// Both entries should exist
		Assert.NotNull(servers["other-server"]);
		Assert.NotNull(servers["maui-devflow"]);
	}

	[Fact]
	public async Task ConfigureAsync_MergesIntoJsoncConfig()
	{
		var configDir = Path.Combine(_tempDir, ".vscode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");
		await File.WriteAllTextAsync(configPath, """
			{
			  // Existing user MCP server.
			  "mcpServers": {
			    "other-server": {
			      "command": "other",
			    },
			  },
			}
			""");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.VsCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".github", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		var backupPath = Path.Combine(configDir, "mcp.json.bak");
		Assert.True(File.Exists(backupPath));
		var backup = await File.ReadAllTextAsync(backupPath);
		Assert.Contains("// Existing user MCP server.", backup);

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var servers = json?["mcpServers"]?.AsObject();
		Assert.NotNull(servers);
		Assert.NotNull(servers["other-server"]);
		Assert.NotNull(servers["maui-devflow"]);
	}

	[Fact]
	public async Task ConfigureAsync_JsoncBackupUsesStablePath()
	{
		var configDir = Path.Combine(_tempDir, ".vscode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.VsCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".github", "skills")
		};

		await File.WriteAllTextAsync(configPath, """
			{
			  // First backup.
			  "mcpServers": {}
			}
			""");
		await McpConfigurator.ConfigureAsync(env);

		await File.WriteAllTextAsync(configPath, """
			{
			  // Second backup.
			  "mcpServers": {
			    "maui-devflow": { "command": "wrong" }
			  }
			}
			""");
		await McpConfigurator.ConfigureAsync(env);

		var backupPath = Path.Combine(configDir, "mcp.json.bak");
		var backup = await File.ReadAllTextAsync(backupPath);
		Assert.Contains("// Second backup.", backup);
		Assert.Single(Directory.GetFiles(configDir, "mcp.json*.bak"));
	}

	[Fact]
	public async Task ConfigureAsync_SymlinkedBackupPath_DoesNotOverwriteTarget()
	{
		var configDir = Path.Combine(_tempDir, ".vscode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");
		var outsideFile = Path.Combine(_tempDir, "outside.txt");
		await File.WriteAllTextAsync(outsideFile, "outside content");
		if (!TryCreateFileSymlink(Path.Combine(configDir, "mcp.json.bak"), outsideFile))
			return;

		await File.WriteAllTextAsync(configPath, """
			{
			  // Existing user MCP server.
			  "mcpServers": {}
			}
			""");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.VsCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".github", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		Assert.Equal("outside content", await File.ReadAllTextAsync(outsideFile));
	}

	[Fact]
	public async Task ConfigureAsync_Idempotent_DoesNotDuplicateEntry()
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		// Configure twice
		await McpConfigurator.ConfigureAsync(env);
		var contentAfterFirst = await File.ReadAllTextAsync(configPath);

		await McpConfigurator.ConfigureAsync(env);
		var contentAfterSecond = await File.ReadAllTextAsync(configPath);

		// File should not change on second run (entry already exists)
		Assert.Equal(contentAfterFirst, contentAfterSecond);
	}

	[Theory]
	[InlineData("\"broken\"")]
	[InlineData("""{ "command": "wrong" }""")]
	[InlineData("""{ "command": "maui", "args": ["wrong"] }""")]
	public async Task ConfigureAsync_RepairsMalformedStandardServerEntry(string malformedEntry)
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");
		await File.WriteAllTextAsync(configPath, $$"""
			{
			  "mcpServers": {
			    "maui-devflow": {{malformedEntry}}
			  }
			}
			""");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var server = json?["mcpServers"]?["maui-devflow"];
		Assert.NotNull(server);
		Assert.Equal("maui", server["command"]?.GetValue<string>());
		var args = server["args"]?.AsArray();
		Assert.NotNull(args);
		Assert.Equal("devflow", args[0]?.GetValue<string>());
		Assert.Equal("mcp", args[1]?.GetValue<string>());
	}

	[Fact]
	public async Task ConfigureAsync_OpenCode_UsesNestedMcpServersKey()
	{
		var configDir = Path.Combine(_tempDir, ".opencode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "config.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.OpenCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".opencode", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var server = json?["mcp"]?["servers"]?["maui-devflow"];
		Assert.NotNull(server);
		Assert.Equal("maui", server["command"]?.GetValue<string>());
	}

	[Fact]
	public async Task ConfigureAsync_OpenCode_MergesIntoExistingConfig()
	{
		var configDir = Path.Combine(_tempDir, ".opencode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "config.json");

		// OpenCode uses "mcp" -> "servers" structure
		var existing = new JsonObject
		{
			["mcp"] = new JsonObject
			{
				["servers"] = new JsonObject
				{
					["existing-server"] = new JsonObject
					{
						["command"] = "existing"
					}
				}
			}
		};
		await File.WriteAllTextAsync(configPath, existing.ToJsonString());

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.OpenCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".opencode", "skills")
		};

		await McpConfigurator.ConfigureAsync(env);

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var servers = json?["mcp"]?["servers"]?.AsObject();
		Assert.NotNull(servers);
		Assert.NotNull(servers["existing-server"]);
		Assert.NotNull(servers["maui-devflow"]);
	}

	[Fact]
	public async Task ConfigureAsync_CreatesConfigDirectory_WhenMissing()
	{
		// Config directory does not exist yet
		var configDir = Path.Combine(_tempDir, "new-env", ".claude");
		var configPath = Path.Combine(configDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, "new-env", ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		Assert.True(File.Exists(configPath));
	}

	[Fact]
	public async Task ConfigureAsync_CorruptedJson_ReturnsFalse()
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");

		// Write invalid JSON content
		await File.WriteAllTextAsync(configPath, "not json at all {{{");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.False(result);
	}

	[Fact]
	public async Task ConfigureAsync_IncompatibleStandardSchema_ReturnsFalseAndLeavesConfigUnchanged()
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");
		var originalContent = """
			{
			  "mcpServers": []
			}
			""";
		await File.WriteAllTextAsync(configPath, originalContent);

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.False(result);
		Assert.Equal(originalContent, await File.ReadAllTextAsync(configPath));
		Assert.Empty(Directory.EnumerateFiles(configDir, "*.tmp"));
	}

	[Fact]
	public async Task ConfigureAsync_SymlinkedProjectConfigDirectoryOutsideProject_ReturnsFalse()
	{
		var projectRoot = Path.Combine(_tempDir, "project");
		var outsideRoot = Path.Combine(_tempDir, "outside");
		Directory.CreateDirectory(projectRoot);
		Directory.CreateDirectory(outsideRoot);

		if (!TryCreateDirectorySymlink(Path.Combine(projectRoot, ".claude"), outsideRoot))
			return;

		var configPath = Path.Combine(projectRoot, ".claude", "mcp.json");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(projectRoot, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env, projectRoot);

		Assert.False(result);
		Assert.False(File.Exists(Path.Combine(outsideRoot, "mcp.json")));
	}

	[Fact]
	public async Task ConfigureAsync_PathOutsideProject_DoesNotCreateDirectory()
	{
		var projectRoot = Path.Combine(_tempDir, "project");
		var outsideRoot = Path.Combine(_tempDir, "outside");
		Directory.CreateDirectory(projectRoot);
		var configPath = Path.Combine(outsideRoot, ".claude", "mcp.json");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(projectRoot, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env, projectRoot);

		Assert.False(result);
		Assert.False(Directory.Exists(Path.Combine(outsideRoot, ".claude")));
	}

	[Fact]
	public async Task ConfigureAsync_IncompatibleOpenCodeSchema_ReturnsFalseAndLeavesConfigUnchanged()
	{
		var configDir = Path.Combine(_tempDir, ".opencode");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "config.json");
		var originalContent = """
			{
			  "mcp": {
			    "servers": []
			  }
			}
			""";
		await File.WriteAllTextAsync(configPath, originalContent);

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.OpenCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".opencode", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.False(result);
		Assert.Equal(originalContent, await File.ReadAllTextAsync(configPath));
		Assert.Empty(Directory.EnumerateFiles(configDir, "*.tmp"));
	}

	[Fact]
	public async Task ConfigureAsync_ReturnsTrue_WhenEntryAlreadyExists()
	{
		var configDir = Path.Combine(_tempDir, ".claude");
		Directory.CreateDirectory(configDir);
		var configPath = Path.Combine(configDir, "mcp.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".claude", "skills")
		};

		// First call creates the entry
		var first = await McpConfigurator.ConfigureAsync(env);
		Assert.True(first);

		// Second call should also return true (already configured)
		var second = await McpConfigurator.ConfigureAsync(env);
		Assert.True(second);
	}

	static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
	{
		try
		{
			Directory.CreateSymbolicLink(linkPath, targetPath);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			return false;
		}
	}

	static bool TryCreateFileSymlink(string linkPath, string targetPath)
	{
		try
		{
			File.CreateSymbolicLink(linkPath, targetPath);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			return false;
		}
	}
}
