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
		var configPath = Path.Combine(_tempDir, ".mcp.json");

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
		var configPath = Path.Combine(_tempDir, ".mcp.json");

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
			["inputs"] = new JsonArray(new JsonObject { ["id"] = "token", ["type"] = "promptString" }),
			["servers"] = new JsonObject
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
		var servers = json?["servers"]?.AsObject();
		Assert.NotNull(servers);
		Assert.True(JsonNode.DeepEquals(existing["inputs"], json?["inputs"]));
		Assert.Null(json?["mcpServers"]);
		Assert.Equal("stdio", servers["maui-devflow"]?["type"]?.GetValue<string>());

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
			  "servers": {
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

		var result = await McpConfigurator.ConfigureWithResultAsync(env, projectRoot: null);

		Assert.True(result.Success);
		var backupPath = Path.Combine(configDir, "mcp.json.bak");
		Assert.Equal(backupPath, result.BackupPath);
		Assert.True(File.Exists(backupPath));
		var backup = await File.ReadAllTextAsync(backupPath);
		Assert.Contains("// Existing user MCP server.", backup);

		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var servers = json?["servers"]?.AsObject();
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
			  "servers": {}
			}
			""");
		await McpConfigurator.ConfigureAsync(env);

		await File.WriteAllTextAsync(configPath, """
			{
			  // Second backup.
			  "servers": {
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
			  "servers": {}
			}
			""");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.VsCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".github", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.False(result);
		Assert.Equal("outside content", await File.ReadAllTextAsync(outsideFile));
		Assert.Contains("// Existing user MCP server.", await File.ReadAllTextAsync(configPath));
	}

	[Fact]
	public async Task ConfigureAsync_Idempotent_DoesNotDuplicateEntry()
	{
		var configPath = Path.Combine(_tempDir, ".mcp.json");

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
	[InlineData("""{ "command": ["wrong"], "args": ["devflow", "mcp"] }""")]
	[InlineData("""{ "command": "maui", "args": [12, {}] }""")]
	public async Task ConfigureAsync_RepairsMalformedStandardServerEntry(string malformedEntry)
	{
		var configPath = Path.Combine(_tempDir, ".mcp.json");
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
	public async Task ConfigureAsync_OpenCode_UsesLocalCommandArray()
	{
		var configPath = Path.Combine(_tempDir, "opencode.json");

		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.OpenCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(_tempDir, ".opencode", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env);

		Assert.True(result);
		var json = JsonNode.Parse(await File.ReadAllTextAsync(configPath));
		var server = json?["mcp"]?["maui-devflow"];
		Assert.NotNull(server);
		Assert.Equal("local", server["type"]?.GetValue<string>());
		Assert.True(JsonNode.DeepEquals(new JsonArray("maui", "devflow", "mcp"), server["command"]));
		Assert.Null(server["args"]);
		Assert.Null(json?["mcp"]?["servers"]);
		Assert.Null(json?["mcpServers"]);
	}

	[Fact]
	public async Task ConfigureAsync_OpenCode_MergesIntoExistingConfig()
	{
		var configPath = Path.Combine(_tempDir, "opencode.json");

		var existing = new JsonObject
		{
			["model"] = "provider/model",
			["mcp"] = new JsonObject
			{
				["existing-server"] = new JsonObject
				{
					["type"] = "local",
					["command"] = new JsonArray("existing")
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
		var servers = json?["mcp"]?.AsObject();
		Assert.NotNull(servers);
		Assert.NotNull(servers["existing-server"]);
		Assert.NotNull(servers["maui-devflow"]);
		Assert.True(JsonNode.DeepEquals(existing["mcp"]?["existing-server"], servers["existing-server"]));
		Assert.Equal("provider/model", json?["model"]?.GetValue<string>());
	}

	[Fact]
	public async Task ConfigureAsync_CreatesConfigDirectory_WhenMissing()
	{
		// Config directory does not exist yet
		var configDir = Path.Combine(_tempDir, "new-env");
		var configPath = Path.Combine(configDir, ".mcp.json");

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
		var configPath = Path.Combine(_tempDir, ".mcp.json");

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
		var configPath = Path.Combine(_tempDir, ".mcp.json");
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
		Assert.Empty(Directory.EnumerateFiles(_tempDir, "*.tmp"));
	}

	[Fact]
	public async Task ConfigureAsync_SymlinkedProjectConfigDirectoryOutsideProject_ReturnsFalse()
	{
		var projectRoot = Path.Combine(_tempDir, "project");
		var outsideRoot = Path.Combine(_tempDir, "outside");
		Directory.CreateDirectory(projectRoot);
		Directory.CreateDirectory(outsideRoot);

		if (!TryCreateDirectorySymlink(Path.Combine(projectRoot, ".vscode"), outsideRoot))
			return;

		var configPath = Path.Combine(projectRoot, ".vscode", "mcp.json");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.VsCode,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(projectRoot, ".github", "skills")
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
		var configPath = Path.Combine(outsideRoot, ".mcp.json");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			McpConfigPath = configPath,
			SkillsDirectory = Path.Combine(projectRoot, ".claude", "skills")
		};

		var result = await McpConfigurator.ConfigureAsync(env, projectRoot);

		Assert.False(result);
		Assert.False(Directory.Exists(outsideRoot));
	}

	[Fact]
	public async Task ConfigureAsync_IncompatibleOpenCodeSchema_ReturnsFalseAndLeavesConfigUnchanged()
	{
		var configPath = Path.Combine(_tempDir, "opencode.json");
		var originalContent = """
			{
			  "mcp": []
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
		Assert.Empty(Directory.EnumerateFiles(_tempDir, "*.tmp"));
	}

	[Fact]
	public async Task ConfigureAsync_ReturnsTrue_WhenEntryAlreadyExists()
	{
		var configPath = Path.Combine(_tempDir, ".mcp.json");

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

	[Theory]
	[InlineData("Claude", ".mcp.json", "mcpServers")]
	[InlineData("VsCode", ".vscode/mcp.json", "servers")]
	[InlineData("CopilotCli", ".copilot/mcp-config.json", "mcpServers")]
	[InlineData("OpenCode", "opencode.json", "mcp")]
	public async Task ConfigureAsync_ClientSchemas_PreservesSettingsAndIsIdempotent(string kindName, string relativePath, string serverKey)
	{
		var configPath = Path.Combine(_tempDir, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
		var settings = JsonNode.Parse("""
			{
			  "model": "provider/model",
			  "inputs": [{"id": "token", "type": "promptString"}]
			}
			""")!.AsObject();
		var otherServer = new JsonObject { ["type"] = "http", ["url"] = "https://example.com/mcp" };
		var customServerSettings = JsonNode.Parse("""
			{
			  "command": {"invalid": true},
			  "env": {"CUSTOM": "value"},
			  "environment": {"CUSTOM": "value"},
			  "tools": ["maui_tree"],
			  "enabled": false,
			  "timeout": 12000
			}
			""")!.AsObject();
		settings[serverKey] = new JsonObject
		{
			["other-server"] = otherServer.DeepClone(),
			["maui-devflow"] = customServerSettings.DeepClone()
		};
		await File.WriteAllTextAsync(configPath, settings.ToJsonString());
		var env = new DetectedEnvironment
		{
			Kind = Enum.Parse<AgentEnvironmentKind>(kindName),
			McpConfigPath = configPath,
			SkillsDirectory = _tempDir
		};

		Assert.True(await McpConfigurator.ConfigureAsync(env, _tempDir));
		var firstContent = await File.ReadAllTextAsync(configPath);
		var result = JsonNode.Parse(firstContent)!;
		Assert.Equal("provider/model", result["model"]!.GetValue<string>());
		Assert.True(JsonNode.DeepEquals(settings["inputs"], result["inputs"]));
		Assert.True(JsonNode.DeepEquals(otherServer, result[serverKey]!["other-server"]));
		var server = result[serverKey]!["maui-devflow"]!;
		foreach (var property in customServerSettings.Where(property => property.Key != "command"))
			Assert.True(JsonNode.DeepEquals(property.Value, server[property.Key]), property.Key);
		if (env.Kind == AgentEnvironmentKind.OpenCode)
		{
			Assert.Equal("local", server["type"]!.GetValue<string>());
			Assert.True(JsonNode.DeepEquals(new JsonArray("maui", "devflow", "mcp"), server["command"]));
			Assert.Null(server["args"]);
		}
		else
		{
			Assert.Equal("maui", server["command"]!.GetValue<string>());
			Assert.True(JsonNode.DeepEquals(new JsonArray("devflow", "mcp"), server["args"]));
		}

		Assert.True(await McpConfigurator.ConfigureAsync(env, _tempDir));
		Assert.Equal(firstContent, await File.ReadAllTextAsync(configPath));
	}

	[Fact]
	public async Task ConfigureAsync_CopilotCli_CreatesNativeConfigWithTools()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, ".copilot"));
		var env = AgentEnvironmentDetector.GetCopilotCliEnvironment(_tempDir, _tempDir)!;

		Assert.True(await McpConfigurator.ConfigureAsync(env, _tempDir));

		var path = Path.Combine(_tempDir, ".copilot", "mcp-config.json");
		var server = JsonNode.Parse(await File.ReadAllTextAsync(path))!["mcpServers"]!["maui-devflow"]!;
		Assert.Equal("local", server["type"]!.GetValue<string>());
		Assert.Equal("maui", server["command"]!.GetValue<string>());
		Assert.True(JsonNode.DeepEquals(new JsonArray("devflow", "mcp"), server["args"]));
		Assert.True(JsonNode.DeepEquals(new JsonArray("*"), server["tools"]));
		Assert.False(File.Exists(Path.Combine(_tempDir, ".copilot", "mcp.json")));
	}

	[Fact]
	public async Task ConfigureAsync_OpenCodeJsonc_PreservesJsonConfigAndBacksUpComments()
	{
		var jsonPath = Path.Combine(_tempDir, "opencode.json");
		var jsoncPath = Path.Combine(_tempDir, "opencode.jsonc");
		const string json = """{"model":"provider/model"}""";
		const string jsonc = """
			{
			  // Project-specific MCP settings.
			  "mcp": {
			    "existing": { "type": "local", "command": ["existing"] },
			  },
			}
			""";
		await File.WriteAllTextAsync(jsonPath, json);
		await File.WriteAllTextAsync(jsoncPath, jsonc);
		var env = Assert.Single(AgentEnvironmentDetector.Detect(_tempDir), e => e.Kind == AgentEnvironmentKind.OpenCode);

		var result = await McpConfigurator.ConfigureWithResultAsync(env, _tempDir);

		Assert.True(result.Success);
		Assert.Equal(jsoncPath + ".bak", result.BackupPath);
		Assert.Equal(jsonc, await File.ReadAllTextAsync(result.BackupPath!));
		Assert.Equal(json, await File.ReadAllTextAsync(jsonPath));
		var updated = JsonNode.Parse(await File.ReadAllTextAsync(jsoncPath))!;
		Assert.NotNull(updated["mcp"]!["existing"]);
		Assert.NotNull(updated["mcp"]!["maui-devflow"]);
	}

	[Theory]
	[InlineData(".claude", "mcp.json", ".mcp.json", "Claude")]
	[InlineData(".opencode", "config.json", "opencode.json", "OpenCode")]
	public async Task ConfigureAsync_DetectedEnvironment_DoesNotChangeLegacyConfig(string directory, string legacyFile, string nativeFile, string kindName)
	{
		var legacyDirectory = Path.Combine(_tempDir, directory);
		Directory.CreateDirectory(legacyDirectory);
		var legacyPath = Path.Combine(legacyDirectory, legacyFile);
		const string original = """{"unrelated":"settings"}""";
		await File.WriteAllTextAsync(legacyPath, original);
		var kind = Enum.Parse<AgentEnvironmentKind>(kindName);
		var env = Assert.Single(AgentEnvironmentDetector.Detect(_tempDir), e => e.Kind == kind);

		Assert.True(await McpConfigurator.ConfigureAsync(env, _tempDir));

		Assert.True(File.Exists(Path.Combine(_tempDir, nativeFile)));
		Assert.Equal(original, await File.ReadAllTextAsync(legacyPath));
	}

	[Theory]
	[InlineData("Claude", ".mcp.json", "mcpServers")]
	[InlineData("VsCode", ".vscode/mcp.json", "servers")]
	[InlineData("CopilotCli", ".copilot/mcp-config.json", "mcpServers")]
	[InlineData("OpenCode", "opencode.json", "mcp")]
	public async Task ConfigureAsync_IncompatibleClientSchema_PreservesOriginal(string kindName, string relativePath, string serverKey)
	{
		var configPath = Path.Combine(_tempDir, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
		var original = new JsonObject { [serverKey] = new JsonArray() }.ToJsonString();
		await File.WriteAllTextAsync(configPath, original);
		var env = new DetectedEnvironment
		{
			Kind = Enum.Parse<AgentEnvironmentKind>(kindName),
			McpConfigPath = configPath,
			SkillsDirectory = _tempDir
		};

		Assert.False(await McpConfigurator.ConfigureAsync(env, _tempDir));
		Assert.Equal(original, await File.ReadAllTextAsync(configPath));
	}

	[Theory]
	[InlineData(".mcp.json", "Claude")]
	[InlineData("opencode.json", "OpenCode")]
	public async Task ConfigureAsync_SymlinkedRootConfig_LeavesTargetUntouched(string fileName, string kindName)
	{
		var projectRoot = Path.Combine(_tempDir, "project");
		Directory.CreateDirectory(projectRoot);
		var outsideFile = Path.Combine(_tempDir, "outside.json");
		await File.WriteAllTextAsync(outsideFile, "{}");
		var configPath = Path.Combine(projectRoot, fileName);
		if (!TryCreateFileSymlink(configPath, outsideFile))
			return;
		var env = new DetectedEnvironment
		{
			Kind = Enum.Parse<AgentEnvironmentKind>(kindName),
			McpConfigPath = configPath,
			SkillsDirectory = projectRoot
		};

		Assert.False(await McpConfigurator.ConfigureAsync(env, projectRoot));
		Assert.Equal("{}", await File.ReadAllTextAsync(outsideFile));
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
