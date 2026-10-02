// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class XcodeMigrationDistributionTests
{
	private const string SkillName = "maui-xcode27-migration";
	private const string SkillSource = "plugins/dotnet-maui/skills/" + SkillName;

	[Theory]
	[InlineData("CopilotCli", ".github/skills")]
	[InlineData("Claude", ".claude/skills")]
	[InlineData("VsCode", ".github/skills")]
	public async Task LocalMarketplace_ListAddStatusUpdate_DistributesOnlyCompleteMigrationSkill(
		string environment, string skillsPath)
	{
		using var catalog = new LocalMarketplace(FindRepositoryRoot());
		using var test = new AiCommandFixture();
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(catalog, disposeHandler: false);
		var expected = catalog.Files
			.Where(file => file.Key.StartsWith(SkillSource + "/", StringComparison.Ordinal))
			.ToDictionary(file => file.Key[(SkillSource.Length + 1)..], file => file.Value, StringComparer.Ordinal);
		Assert.Contains("SKILL.md", expected.Keys);
		Assert.Contains(expected.Keys, path => path.StartsWith("references/", StringComparison.Ordinal));

		var before = test.Snapshot();
		var listed = await test.Invoke("list", "skill", "--env", environment);
		AssertSuccess(listed);
		var discovered = Assert.Single(Rows(listed.Result), row => row["name"]!.GetValue<string>() == SkillName);
		Assert.Equal("repository", discovered["owner"]!.GetValue<string>());
		Assert.Equal("dotnet/maui-labs", discovered["origin"]!["repo"]!.GetValue<string>());
		Assert.Equal("main", discovered["origin"]!["branch"]!.GetValue<string>());
		Assert.Equal(SkillSource, discovered["origin"]!["path"]!.GetValue<string>());
		Assert.Equal(catalog.Commit, discovered["origin"]!["resolvedCommit"]!.GetValue<string>());
		Assert.Contains(Rows(listed.Result), row => row["name"]!.GetValue<string>() != SkillName);
		Assert.Contains(".github/plugin/marketplace.json", catalog.RequestedFiles);
		Assert.Contains("plugins/dotnet-maui/plugin.json", catalog.RequestedFiles);
		Assert.Equal(before, test.Snapshot());

		var directory = Path.GetFullPath(Path.Combine(test.Root, skillsPath, SkillName));
		var added = await test.Invoke("add", "skill", SkillName, "--env", environment);
		AssertSuccess(added);
		var installed = Assert.Single(Rows(added.Result));
		Assert.Equal("succeeded", installed["outcome"]!.GetValue<string>());
		Assert.Equal(directory, installed["path"]!.GetValue<string>());
		Assert.Equal("project", installed["scope"]!.GetValue<string>());
		Assert.Equal(environment, Assert.Single(installed["environments"]!.AsArray())!.GetValue<string>());
		AssertCompleteInstallation(test.Root, directory, expected);
		var version = await SkillVersionStore.ReadAsync(directory);
		Assert.NotNull(version);
		Assert.Equal("dotnet/maui-labs", version.Source);
		Assert.Equal("main", version.Branch);
		Assert.Equal(SkillSource, version.PluginPath);
		Assert.Equal(catalog.Commit, version.Commit);
		Assert.Equal(AiContentHash.DirectoryHash(directory), version.ContentHash);

		before = test.Snapshot();
		var repeated = await test.Invoke("add", "skill", SkillName, "--env", environment);
		AssertSuccess(repeated);
		Assert.Equal("already-current", Assert.Single(Rows(repeated.Result))["reasonCode"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());

		var requestCount = catalog.RequestCount;
		var status = await test.Invoke("status", "skill", "--env", environment);
		AssertSuccess(status);
		var inventory = Assert.Single(Rows(status.Result));
		Assert.Equal(SkillName, inventory["name"]!.GetValue<string>());
		Assert.True(inventory["managed"]!.GetValue<bool>());
		Assert.Equal("installed", inventory["state"]!.GetValue<string>());
		Assert.Equal(requestCount, catalog.RequestCount);
		Assert.Equal(before, test.Snapshot());

		var current = await test.Invoke("update", "skill", "--env", environment);
		AssertSuccess(current);
		Assert.Equal("already-current", Assert.Single(Rows(current.Result))["reasonCode"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());

		// Exercise an actual update, not just the already-current fast path.
		var reference = expected.Keys.First(path => path.StartsWith("references/", StringComparison.Ordinal));
		File.WriteAllText(Path.Combine(directory, reference), "Local customization");
		var repaired = await test.Invoke("update", "skill", "--skill", SkillName, "--env", environment, "--force");
		AssertSuccess(repaired);
		Assert.Equal("succeeded", Assert.Single(Rows(repaired.Result))["outcome"]!.GetValue<string>());
		AssertCompleteInstallation(test.Root, directory, expected);
		Assert.Equal(AiContentHash.DirectoryHash(directory), (await SkillVersionStore.ReadAsync(directory))!.ContentHash);
		Assert.Empty(test.Handler.Requests);
	}

	private static IEnumerable<JsonObject> Rows(JsonObject result) =>
		result["results"]!.AsArray().Select(row => row!.AsObject());

	private static void AssertSuccess((int Exit, JsonObject Result) result) =>
		Assert.True(result.Exit == 0, result.Result.ToJsonString());

	private static void AssertCompleteInstallation(string root, string directory, Dictionary<string, byte[]> expected)
	{
		var installedFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'));
		Assert.Equal(expected.Keys.Append(".skill-version").Order(StringComparer.Ordinal), installedFiles.Order(StringComparer.Ordinal));
		foreach (var (path, bytes) in expected)
			Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, path)));

		// No sibling skills, agents, MCP configuration, or user-home files may be installed.
		var allowed = expected.Keys.Append(".skill-version").Select(path => Path.GetFullPath(Path.Combine(directory, path)))
			.Append(Path.Combine(root, ".git")).Order(StringComparer.Ordinal);
		Assert.Equal(allowed, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
		Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(directory)!));
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, ".github", "plugin", "marketplace.json")))
				return directory.FullName;
		throw new InvalidOperationException("The distribution test requires the repository's local marketplace files.");
	}

	// Only GitHub transport metadata is synthesized; manifests and every skill file
	// are an immutable byte snapshot of the checkout, including uncommitted additions.
	private sealed class LocalMarketplace : HttpMessageHandler
	{
		internal Dictionary<string, byte[]> Files { get; }
		internal HashSet<string> RequestedFiles { get; } = new(StringComparer.Ordinal);
		internal int RequestCount { get; private set; }
		internal string Commit { get; }
		private readonly string tree;

		internal LocalMarketplace(string root)
		{
			Files = Directory.GetFiles(Path.Combine(root, "plugins"), "*", SearchOption.AllDirectories)
				.Append(Path.Combine(root, ".github", "plugin", "marketplace.json"))
				.ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
			Commit = AiContentHash.FilesHash(Files)[..40];
			tree = AiTestCatalog.TreeFor(Commit);
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestCount++;
			var uri = request.RequestUri!;
			Assert.Equal(HttpMethod.Get, request.Method);
			if (uri.Host == "raw.githubusercontent.com")
			{
				var prefix = $"/dotnet/maui-labs/{Commit}/";
				Assert.StartsWith(prefix, uri.AbsolutePath);
				var path = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..]);
				RequestedFiles.Add(path);
				return Task.FromResult(Files.TryGetValue(path, out var bytes)
					? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
					: new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			Assert.Equal("api.github.com", uri.Host);
			const string api = "/repos/dotnet/maui-labs";
			if (uri.AbsolutePath is api + "/commits/main" || uri.AbsolutePath == $"{api}/commits/{Commit}")
				return Json(new { sha = Commit, commit = new { tree = new { sha = tree } } });
			if (uri.AbsolutePath == $"{api}/git/trees/{tree}")
			{
				Assert.Equal("?recursive=1", uri.Query);
				return Json(new { truncated = false, tree = Files.Keys.Select(path => new { path, type = "blob" }) });
			}
			if (uri.AbsolutePath == api + "/commits")
			{
				Assert.Contains("sha=" + Commit, uri.Query);
				return Json(new[] { new { sha = Commit } });
			}
			throw new InvalidOperationException($"Unexpected local marketplace request: {uri}");
		}

		private static Task<HttpResponseMessage> Json<T>(T value) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
	}
}
