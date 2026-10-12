// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiAutomationContractTests : IDisposable
{
	readonly string _originalDirectory = Directory.GetCurrentDirectory();
	readonly string? _originalHome = AgentEnvironmentDetector.UserHomeOverrideForTests;
	readonly string? _originalState = DevFlowSkillManager.StateRootOverrideForTests;
	readonly Func<HttpClient>? _originalHttp = AiCommands.HttpClientFactoryForTests;
	readonly string _root;
	readonly JsonObject _schema;

	public AiAutomationContractTests()
	{
		var repository = new DirectoryInfo(_originalDirectory);
		while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "docs", "Cli", "ai-result.schema.json")))
			repository = repository.Parent;
		Assert.NotNull(repository);
		_schema = JsonNode.Parse(File.ReadAllText(Path.Combine(repository.FullName, "docs", "Cli", "ai-result.schema.json")))!.AsObject();
		var temporary = Path.Combine(Path.GetTempPath(), $"maui-ai-contract-{Guid.NewGuid():N}");
		Directory.CreateDirectory(temporary);
		Directory.SetCurrentDirectory(temporary);
		_root = Directory.GetCurrentDirectory();
		File.WriteAllText(Path.Combine(_root, ".git"), "");
		var home = Path.Combine(_root, "home");
		Directory.CreateDirectory(home);
		AgentEnvironmentDetector.UserHomeOverrideForTests = home;
		DevFlowSkillManager.StateRootOverrideForTests = Path.Combine(_root, "owner-state");
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(new RejectNetwork());
	}

	[Theory]
	[InlineData("list mcp --env Claude", 0)]
	[InlineData("status --env Claude", 0)]
	[InlineData("update mcp --env Claude --yes", 0)]
	[InlineData("add mcp maui-devflow --env CopilotCli --dry-run", 0)]
	[InlineData("add mcp maui-devflow --env Claude --yes", 0)]
	[InlineData("add skill maui-devflow-debug --env Claude --yes", 0)]
	[InlineData("init --skill maui-devflow-debug --env Claude --dry-run", 0)]
	[InlineData("add agent example --env Claude --dry-run", 1)]
	[InlineData("init --yes", 1)]
	public async Task CommandResults_ConformToPublishedContract(string command, int expectedExit)
	{
		var (exit, result) = await Run(command);
		Assert.Equal(expectedExit, exit);
		Assert.Equal(expectedExit == 0 ? "success" : "failed", result["status"]!.GetValue<string>());
		CheckContract(_schema, result);
		if (expectedExit != 0)
			Assert.Contains(result["results"]!.AsArray(), row => row!["outcome"]!.GetValue<string>() is "failed" or "blocked");
	}

	[Fact]
	public async Task Conflict_AndMissingInventoryHaveDifferentExitSemantics()
	{
		var config = Path.Combine(_root, ".mcp.json");
		const string original = """{"mcpServers":{"maui-devflow":{"command":"custom","args":["different"],"env":{"TOKEN":"keep-private"}}}}""";
		await File.WriteAllTextAsync(config, original);
		var (blockedExit, blocked) = await Run("add mcp maui-devflow --env Claude --yes");
		CheckContract(_schema, blocked);
		Assert.Equal(1, blockedExit);
		Assert.Equal("failed", blocked["status"]!.GetValue<string>());
		var row = Assert.Single(blocked["results"]!.AsArray())!;
		Assert.Equal("blocked", row["outcome"]!.GetValue<string>());
		Assert.Equal("content-conflict", row["reasonCode"]!.GetValue<string>());
		Assert.True(row["requiresForce"]!.GetValue<bool>());
		Assert.Equal(original, await File.ReadAllTextAsync(config));
		Assert.DoesNotContain("keep-private", blocked.ToJsonString());

		var (appliedExit, applied) = await Run("add mcp maui-devflow --env Claude --force --yes");
		Assert.Equal(0, appliedExit);
		CheckContract(_schema, applied);
		File.Delete(config);
		var (inventoryExit, inventory) = await Run("status mcp --env Claude");
		CheckContract(_schema, inventory);
		Assert.Equal(0, inventoryExit);
		Assert.Equal("success", inventory["status"]!.GetValue<string>());
		Assert.Contains(inventory["results"]!.AsArray(), item => item!["state"]!.GetValue<string>() == "missing");
		var (updateExit, update) = await Run("update mcp --env Claude --force --yes");
		CheckContract(_schema, update);
		Assert.Equal(0, updateExit);
		Assert.Contains(update["results"]!.AsArray(), item => item!["reasonCode"]!.GetValue<string>() == "skipped-missing");
		Assert.False(File.Exists(config));
	}

	[Theory]
	[InlineData("missing-result-field")]
	[InlineData("wrong-version")]
	[InlineData("invalid-outcome")]
	[InlineData("wrong-nullability")]
	[InlineData("invalid-commit")]
	[InlineData("missing-environment-field")]
	[InlineData("invalid-mcp-scope")]
	public async Task ContractChecks_RejectMalformedResults(string mutation)
	{
		var (_, result) = await Run("add mcp maui-devflow --env Claude --dry-run");
		var row = result["results"]![0]!.AsObject();
		switch (mutation)
		{
			case "missing-result-field": row.Remove("action"); break;
			case "wrong-version": result["schemaVersion"] = 2; break;
			case "invalid-outcome": row["outcome"] = "probably-fine"; break;
			case "wrong-nullability": row["managed"] = null; break;
			case "invalid-commit":
				row["origin"] = new JsonObject { ["repo"] = "owner/repo", ["branch"] = "main", ["path"] = "asset", ["resolvedCommit"] = "not-a-sha" };
				break;
			case "missing-environment-field": result["environments"]![0]!.AsObject().Remove("markerPath"); break;
			case "invalid-mcp-scope": result["environments"]![0]!["mcpScope"] = "global"; break;
		}
		Assert.NotNull(Record.Exception(() => CheckContract(_schema, result)));
	}

	[Fact]
	public async Task ContractChecks_AcceptAdditiveFieldsAndUnknownReasons()
	{
		var (_, result) = await Run("add mcp maui-devflow --env Claude --dry-run");
		result["futureField"] = true;
		var row = result["results"]![0]!.AsObject();
		row["futureField"] = 1;
		row["state"] = "future-observed-state";
		row["reasonCode"] = "future-reason";
		row["origin"] = new JsonObject { ["repo"] = "owner/repo", ["branch"] = "main", ["path"] = "asset", ["resolvedCommit"] = new string('a', 40) };
		CheckContract(_schema, result);
	}

	// This checks the deliberately small vocabulary used by our published schema,
	// not arbitrary JSON Schema. Unknown validation keywords fail to prevent drift.
	static void CheckContract(JsonObject schema, JsonNode? value)
	{
		foreach (var key in schema.Select(pair => pair.Key))
			Assert.Contains(key, new[] { "$schema", "$id", "title", "description", "type", "required", "properties", "items", "enum", "const", "pattern" });
		var types = schema["type"] is JsonArray alternatives
			? alternatives.Select(node => node!.GetValue<string>()).ToArray()
			: [schema["type"]!.GetValue<string>()];
		Assert.Contains(types, type => type switch
		{
			"null" => value is null,
			"object" => value is JsonObject,
			"array" => value is JsonArray,
			"string" => value is JsonValue s && s.TryGetValue<string>(out _),
			"boolean" => value is JsonValue b && b.TryGetValue<bool>(out _),
			"integer" => value is JsonValue i && i.TryGetValue<int>(out _),
			_ => throw new InvalidOperationException($"Unsupported schema type: {type}")
		});
		if (schema.ContainsKey("const"))
			Assert.True(JsonNode.DeepEquals(schema["const"], value));
		if (schema["enum"] is JsonArray values)
			Assert.Contains(values, allowed => JsonNode.DeepEquals(allowed, value));
		if (value is JsonObject obj)
		{
			if (schema["required"] is JsonArray required)
				foreach (var key in required)
					Assert.True(obj.ContainsKey(key!.GetValue<string>()), $"Missing required property: {key}");
			if (schema["properties"] is JsonObject properties)
				foreach (var (key, child) in properties)
					if (obj.TryGetPropertyValue(key, out var property))
						CheckContract(child!.AsObject(), property);
		}
		if (value is JsonArray items && schema["items"] is JsonObject itemSchema)
			foreach (var item in items)
				CheckContract(itemSchema, item);
		if (value is JsonValue text && text.TryGetValue<string>(out var contents) && schema["pattern"] is JsonValue pattern)
			Assert.Matches(new Regex(pattern.GetValue<string>(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), contents);
	}

	static async Task<(int Exit, JsonObject Result)> Run(string command)
	{
		var original = Console.Out;
		using var writer = new StringWriter();
		try
		{
			Console.SetOut(writer);
			var exit = await Program.BuildRootCommand().Parse($"ai {command} --json --ci").InvokeAsync();
			return (exit, JsonNode.Parse(writer.ToString())!.AsObject());
		}
		finally { Console.SetOut(original); }
	}

	public void Dispose()
	{
		Directory.SetCurrentDirectory(_originalDirectory);
		AgentEnvironmentDetector.UserHomeOverrideForTests = _originalHome;
		DevFlowSkillManager.StateRootOverrideForTests = _originalState;
		AiCommands.HttpClientFactoryForTests = _originalHttp;
		Directory.Delete(_root, recursive: true);
	}

	sealed class RejectNetwork : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> throw new InvalidOperationException($"This contract fixture must not access the network: {request.RequestUri}");
	}
}
