// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
	public static async Task<bool> ConfigureAsync(DetectedEnvironment env, CancellationToken ct = default)
	{
		try
		{
			var configPath = env.McpConfigPath;

			JsonObject root;
			if (File.Exists(configPath))
			{
				var existingJson = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
				if (JsonNode.Parse(existingJson, documentOptions: s_jsonDocumentOptions) is not JsonObject existingRoot)
					return false;

				root = existingRoot;
			}
			else
			{
				root = new JsonObject();
			}

			var serverEntry = new JsonObject
			{
				["command"] = "maui",
				["args"] = new JsonArray("devflow", "mcp")
			};

			var configureResult = env.Kind == AgentEnvironmentKind.OpenCode
				? EnsureOpenCodeEntry(root, serverEntry)
				: EnsureStandardEntry(root, serverEntry);

			if (configureResult == ConfigureResult.AlreadyConfigured)
				return true;
			if (configureResult == ConfigureResult.IncompatibleSchema)
				return false;

			// Ensure the config directory exists before writing.
			var configDir = Path.GetDirectoryName(configPath);
			if (!string.IsNullOrEmpty(configDir))
				Directory.CreateDirectory(configDir);

			var options = new JsonSerializerOptions { WriteIndented = true };
			await WriteAtomicAsync(configPath, root.ToJsonString(options), ct).ConfigureAwait(false);

			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// Adds the server entry under the standard <c>mcpServers</c> key used by
	/// Claude, VS Code, and Copilot CLI.
	/// </summary>
	private static ConfigureResult EnsureStandardEntry(JsonObject root, JsonObject serverEntry)
	{
		var existing = root["mcpServers"];
		if (existing is not null and not JsonObject)
			return ConfigureResult.IncompatibleSchema;

		if (existing is not JsonObject mcpServers)
		{
			mcpServers = new JsonObject();
			root["mcpServers"] = mcpServers;
		}

		if (mcpServers[ServerName] is not null)
			return ConfigureResult.AlreadyConfigured;

		mcpServers[ServerName] = serverEntry;
		return ConfigureResult.Updated;
	}

	/// <summary>
	/// Adds the server entry under the OpenCode-specific <c>mcp.servers</c> key.
	/// </summary>
	private static ConfigureResult EnsureOpenCodeEntry(JsonObject root, JsonObject serverEntry)
	{
		var existingMcp = root["mcp"];
		if (existingMcp is not null and not JsonObject)
			return ConfigureResult.IncompatibleSchema;

		if (existingMcp is not JsonObject mcp)
		{
			mcp = new JsonObject();
			root["mcp"] = mcp;
		}

		var existingServers = mcp["servers"];
		if (existingServers is not null and not JsonObject)
			return ConfigureResult.IncompatibleSchema;

		if (existingServers is not JsonObject servers)
		{
			servers = new JsonObject();
			mcp["servers"] = servers;
		}

		if (servers[ServerName] is not null)
			return ConfigureResult.AlreadyConfigured;

		servers[ServerName] = serverEntry;
		return ConfigureResult.Updated;
	}

	static async Task WriteAtomicAsync(string configPath, string contents, CancellationToken ct)
	{
		var configDir = Path.GetDirectoryName(configPath);
		var tempDir = string.IsNullOrEmpty(configDir) ? Directory.GetCurrentDirectory() : configDir;
		var tempPath = Path.Combine(tempDir, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");

		try
		{
			await File.WriteAllTextAsync(tempPath, contents, ct).ConfigureAwait(false);
			File.Move(tempPath, configPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(tempPath))
				TryDeleteFile(tempPath);
		}
	}

	static void TryDeleteFile(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
			// Best-effort temp cleanup should not hide the config write result.
		}
		catch (UnauthorizedAccessException)
		{
			// Best-effort temp cleanup should not hide the config write result.
		}
	}
}
