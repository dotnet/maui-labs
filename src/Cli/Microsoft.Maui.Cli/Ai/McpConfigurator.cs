// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Writes MCP server configuration for agent environments. Performs a
/// schema-preserving merge so existing configuration entries are retained.
/// </summary>
internal static class McpConfigurator
{
	internal static JsonObject OwnedDefinition(AgentEnvironmentKind kind)
	{
		var definition = kind == AgentEnvironmentKind.OpenCode
			? new JsonObject { ["type"] = "local", ["command"] = new JsonArray("maui", "devflow", "mcp") }
			: new JsonObject { ["command"] = "maui", ["args"] = new JsonArray("devflow", "mcp") };
		if (kind == AgentEnvironmentKind.VsCode) definition["type"] = "stdio";
		if (kind == AgentEnvironmentKind.CopilotCli) definition["type"] = "local";
		return definition;
	}

	internal static string OwnedHash(JsonObject definition)
	{
		var owned = new JsonObject();
		foreach (var field in new[] { "command", "args", "type" })
			if (definition.ContainsKey(field)) owned[field] = definition[field]?.DeepClone();
		return AiContentHash.Bytes(Encoding.UTF8.GetBytes(owned.ToJsonString()));
	}

	internal static string? InspectOwnedHash(DetectedEnvironment environment, string root, string name = ServerName)
	{
		AiAssetRegistry.Guard(environment.McpConfigPath, root);
		var config = ReadConfig(environment.McpConfigPath);
		var key = AgentEnvironmentDetector.Describe(environment.Kind).McpContainer;
		ValidateContainer(config, key);
		if (config[key] is not JsonObject servers || !servers.ContainsKey(name)) return null;
		if (servers[name] is not JsonObject server) throw new InvalidOperationException("Invalid MCP server registration.");
		return OwnedHash(server);
	}

	internal static IEnumerable<string> ConfiguredNames(DetectedEnvironment environment, string root)
	{
		AiAssetRegistry.Guard(environment.McpConfigPath, root);
		var config = ReadConfig(environment.McpConfigPath);
		var key = AgentEnvironmentDetector.Describe(environment.Kind).McpContainer;
		ValidateContainer(config, key);
		return config[key] is JsonObject servers ? servers.Select(s => s.Key).ToArray() : [];
	}

	internal static async Task MergeOwnedAsync(DetectedEnvironment environment, string root, string? expectedHash, CancellationToken ct)
	{
		AiAssetRegistry.Guard(environment.McpConfigPath, root);
		var currentHash = InspectOwnedHash(environment, root);
		if (currentHash != expectedHash)
			throw new IOException("MCP registration changed after planning; retry the command.");
		if (currentHash == OwnedHash(OwnedDefinition(environment.Kind)))
			return;
		var config = ReadConfig(environment.McpConfigPath);
		var key = AgentEnvironmentDetector.Describe(environment.Kind).McpContainer;
		ValidateContainer(config, key);
		if (config[key] is not JsonObject servers) config[key] = servers = new JsonObject();
		var isNew = servers[ServerName] is null;
		if (servers[ServerName] is not JsonObject server) servers[ServerName] = server = new JsonObject();
		foreach (var field in new[] { "command", "args", "type" }) server.Remove(field);
		foreach (var field in OwnedDefinition(environment.Kind)) server[field.Key] = field.Value?.DeepClone();
		if (isNew && environment.Kind == AgentEnvironmentKind.CopilotCli) server["tools"] = new JsonArray("*");
		if (File.Exists(environment.McpConfigPath))
		{
			var original = await File.ReadAllTextAsync(environment.McpConfigPath, ct);
			if (ContainsJsonComments(original) && await WriteBackupAsync(environment.McpConfigPath, original, root, ct) is null)
				throw new IOException("Could not preserve original JSONC configuration.");
		}
		if (!await WriteAtomicAsync(environment.McpConfigPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), root, ct))
			throw new IOException("Could not write MCP registration.");
	}

	private static JsonObject ReadConfig(string path) => !File.Exists(path) ? new() :
		JsonNode.Parse(File.ReadAllText(path), documentOptions: s_jsonDocumentOptions) as JsonObject
			?? throw new InvalidOperationException("MCP configuration must be a JSON object.");
	private static void ValidateContainer(JsonObject config, string key)
	{
		if (config.ContainsKey(key) && config[key] is not JsonObject)
			throw new InvalidOperationException("Invalid MCP configuration container.");
	}
	private const string ServerName = "maui-devflow";
	private static readonly JsonDocumentOptions s_jsonDocumentOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	enum ConfigureResult
	{
		AlreadyConfigured,
		Updated,
		IncompatibleSchema
	}

	/// <summary>
	/// Ensures the <c>maui-devflow</c> MCP server entry exists in the
	/// environment's MCP configuration file. Creates the file if it does not exist.
	/// The operation is idempotent — it does nothing if the entry already exists.
	/// </summary>
	/// <param name="env">Target agent environment.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns><c>true</c> if the configuration is in place; <c>false</c> on failure.</returns>
	public static Task<bool> ConfigureAsync(DetectedEnvironment env, CancellationToken ct = default)
		=> ConfigureAsync(env, projectRoot: null, ct);

	public static async Task<bool> ConfigureAsync(DetectedEnvironment env, string? projectRoot, CancellationToken ct = default)
		=> (await ConfigureWithResultAsync(env, projectRoot, ct).ConfigureAwait(false)).Success;

	public static async Task<McpConfigurationResult> ConfigureWithResultAsync(
		DetectedEnvironment env,
		string? projectRoot,
		CancellationToken ct = default)
	{
		try
		{
			var configPath = env.McpConfigPath;
			var configDir = Path.GetDirectoryName(configPath);
			var writeRoot = GetConfigWriteRoot(configPath, projectRoot, env.Kind);
			if (writeRoot is null)
				return McpConfigurationResult.Failure;

			if (!FileSystemPathGuard.IsPathWithinRoot(configPath, writeRoot) ||
				FileSystemPathGuard.IsReparsePoint(configPath))
				return McpConfigurationResult.Failure;

			if (!string.IsNullOrEmpty(configDir))
			{
				if (!FileSystemPathGuard.IsPathWithinRoot(configDir, writeRoot))
				{
					return McpConfigurationResult.Failure;
				}

				Directory.CreateDirectory(configDir);
				if (!FileSystemPathGuard.IsPathWithinRoot(configDir, writeRoot))
					return McpConfigurationResult.Failure;
			}

			JsonObject root;
			string? existingJson = null;
			var backupExistingConfig = false;
			if (File.Exists(configPath))
			{
				existingJson = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
				if (JsonNode.Parse(existingJson, documentOptions: s_jsonDocumentOptions) is not JsonObject existingRoot)
					return McpConfigurationResult.Failure;

				root = existingRoot;
				backupExistingConfig = ContainsJsonComments(existingJson);
			}
			else
			{
				root = new JsonObject();
			}

			var configureResult = EnsureServerEntry(root, env.Kind);

			if (configureResult == ConfigureResult.AlreadyConfigured)
				return McpConfigurationResult.SuccessResult;
			if (configureResult == ConfigureResult.IncompatibleSchema)
				return McpConfigurationResult.Failure;

			var options = new JsonSerializerOptions { WriteIndented = true };
			string? backupPath = null;
			if (backupExistingConfig && existingJson is not null)
			{
				backupPath = await WriteBackupAsync(configPath, existingJson, writeRoot, ct).ConfigureAwait(false);
				if (backupPath is null)
					return McpConfigurationResult.Failure;
			}

			var wroteConfig = await WriteAtomicAsync(configPath, root.ToJsonString(options), writeRoot, ct).ConfigureAwait(false);

			return wroteConfig
				? new McpConfigurationResult(true, backupPath)
				: McpConfigurationResult.Failure;
		}
		catch (IOException)
		{
			return McpConfigurationResult.Failure;
		}
		catch (UnauthorizedAccessException)
		{
			return McpConfigurationResult.Failure;
		}
		catch (JsonException)
		{
			return McpConfigurationResult.Failure;
		}
	}

	/// <summary>
	/// Merges the server's launch configuration using the client's native schema.
	/// </summary>
	private static ConfigureResult EnsureServerEntry(JsonObject root, AgentEnvironmentKind kind)
	{
		var key = kind switch
		{
			AgentEnvironmentKind.VsCode => "servers",
			AgentEnvironmentKind.OpenCode => "mcp",
			_ => "mcpServers"
		};
		var existing = root[key];
		if (existing is not null and not JsonObject)
			return ConfigureResult.IncompatibleSchema;

		if (existing is not JsonObject servers)
		{
			servers = new JsonObject();
			root[key] = servers;
		}

		var expected = kind == AgentEnvironmentKind.OpenCode
			? new JsonObject
			{
				["type"] = "local",
				["command"] = new JsonArray("maui", "devflow", "mcp")
			}
			: new JsonObject
			{
				["command"] = "maui",
				["args"] = new JsonArray("devflow", "mcp")
			};
		if (kind == AgentEnvironmentKind.VsCode)
			expected["type"] = "stdio";
		if (kind == AgentEnvironmentKind.CopilotCli)
		{
			expected["type"] = "local";
			if (servers[ServerName] is not JsonObject configuredServer || configuredServer["tools"] is null)
				expected["tools"] = new JsonArray("*");
		}

		var server = servers[ServerName] as JsonObject;
		if (server is not null && expected.All(property => JsonNode.DeepEquals(server[property.Key], property.Value)))
			return ConfigureResult.AlreadyConfigured;

		if (server is null)
			servers[ServerName] = expected;
		else
			foreach (var property in expected)
				server[property.Key] = property.Value?.DeepClone();

		return ConfigureResult.Updated;
	}

	static Task<bool> WriteAtomicAsync(string configPath, string contents, string writeRoot, CancellationToken ct)
		=> FileSystemPathGuard.WriteFileAtomicallyWithinRootAsync(
			configPath,
			writeRoot,
			Encoding.UTF8.GetBytes(contents),
			ct);

	static async Task<string?> WriteBackupAsync(string configPath, string contents, string writeRoot, CancellationToken ct)
	{
		var backupPath = GetBackupPath(configPath);
		var wroteBackup = await FileSystemPathGuard.WriteFileAtomicallyWithinRootAsync(
			backupPath,
			writeRoot,
			Encoding.UTF8.GetBytes(contents),
			ct).ConfigureAwait(false);

		return wroteBackup ? backupPath : null;
	}

	static string? GetConfigWriteRoot(string configPath, string? projectRoot, AgentEnvironmentKind kind)
	{
		var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
		var configRoot = string.IsNullOrEmpty(configDir) ? Directory.GetCurrentDirectory() : configDir;

		if (projectRoot is not null && kind != AgentEnvironmentKind.CopilotCli)
			return projectRoot;

		if (projectRoot is not null && FileSystemPathGuard.IsPathWithinRoot(configRoot, projectRoot))
			return projectRoot;

		var userHome = AgentEnvironmentDetector.UserHome;
		if (!string.IsNullOrWhiteSpace(userHome) &&
			FileSystemPathGuard.IsPathWithinRoot(configRoot, userHome))
		{
			return userHome;
		}

		return projectRoot is null ? configRoot : null;
	}

	static string GetBackupPath(string configPath)
		=> $"{configPath}.bak";

	static bool ContainsJsonComments(string contents)
	{
		var reader = new Utf8JsonReader(
			Encoding.UTF8.GetBytes(contents),
			new JsonReaderOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Allow
			});

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.Comment)
				return true;
		}

		return false;
	}

}

internal sealed record McpConfigurationResult(bool Success, string? BackupPath)
{
	public static McpConfigurationResult SuccessResult { get; } = new(true, null);
	public static McpConfigurationResult Failure { get; } = new(false, null);
}
