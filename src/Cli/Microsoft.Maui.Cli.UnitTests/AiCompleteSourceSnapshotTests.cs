using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiCompleteSourceSnapshotTests
{
	[Fact]
	public async Task PluginOnlySource_InstallsAndUpdatesCompleteTextAndBinaryFilesFromPinnedSnapshot()
	{
		using var test = new AiCommandFixture();
		using var source = new StrictPluginSource();
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(source, disposeHandler: false);
		var installed = await test.Invoke("add", "skill", "plugin-skill", "--repo", StrictPluginSource.Repo,
			"--branch", StrictPluginSource.Branch, "--env", "Claude");
		Assert.Equal(0, installed.Exit);
		AssertOrigin(Assert.Single(installed.Result["results"]!.AsArray())!["origin"]!, StrictPluginSource.FirstCommit);
		var directory = Path.Combine(test.Root, ".claude", "skills", "plugin-skill");
		await AssertInstalledFiles(directory, StrictPluginSource.FirstCommit);
		AssertPinnedRequests(source.Requests, StrictPluginSource.FirstCommit);
		Assert.Contains(source.Requests, uri => uri == StrictPluginSource.Raw(StrictPluginSource.FirstCommit, "plugins/test/plugin.json"));
		Assert.Contains(source.Requests, uri => uri == StrictPluginSource.Raw(StrictPluginSource.FirstCommit, ".github/plugin/marketplace.json"));
		Assert.DoesNotContain(source.Requests, uri => uri.AbsolutePath.Contains("/.github/skills/", StringComparison.Ordinal));

		source.CurrentCommit = StrictPluginSource.SecondCommit;
		source.Requests.Clear();
		var updated = await test.Invoke("update", "skill", "--skill", "plugin-skill", "--env", "Claude");
		Assert.Equal(0, updated.Exit);
		var row = Assert.Single(updated.Result["results"]!.AsArray())!;
		Assert.Equal("succeeded", row["outcome"]!.GetValue<string>());
		AssertOrigin(row["origin"]!, StrictPluginSource.SecondCommit);
		await AssertInstalledFiles(directory, StrictPluginSource.SecondCommit);
		AssertPinnedRequests(source.Requests, StrictPluginSource.SecondCommit);
	}

	[Fact]
	public async Task MissingRequiredSidecarOnUpdate_PreservesAllExistingBytesMetadataAndActionableMcp()
	{
		using var test = new AiCommandFixture();
		using var source = new StrictPluginSource();
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(source, disposeHandler: false);
		var installed = await test.Invoke("init", "--skill", "plugin-skill", "--mcp", "maui-devflow",
			"--repo", StrictPluginSource.Repo, "--branch", StrictPluginSource.Branch, "--env", "Claude");
		Assert.Equal(0, installed.Exit);

		var staleDefinition = McpConfigurator.OwnedDefinition(AgentEnvironmentKind.Claude);
		staleDefinition["command"] = "previous-maui";
		var config = new JsonObject { ["mcpServers"] = new JsonObject { ["maui-devflow"] = staleDefinition } };
		File.WriteAllText(Path.Combine(test.Root, ".mcp.json"), config.ToJsonString());
		var registry = AiAssetRegistry.Read(test.Root);
		var mcp = Assert.Single(registry["installations"]!.AsArray().OfType<JsonObject>(), row => row["kind"]!.GetValue<string>() == "mcp");
		mcp["contentHash"] = McpConfigurator.OwnedHash(staleDefinition);
		File.WriteAllText(AiAssetRegistry.RegistryPath(test.Root), registry.ToJsonString());
		var preview = await test.Invoke("update", "mcp", "--env", "Claude", "--dry-run");
		Assert.Equal(0, preview.Exit);
		Assert.Equal("replace", Assert.Single(preview.Result["results"]!.AsArray())!["action"]!.GetValue<string>());

		var before = CaptureFiles(test.Root);
		source.CurrentCommit = StrictPluginSource.SecondCommit;
		source.MissingSidecar = true;
		source.Requests.Clear();
		var updated = await test.Invoke("update", "--skill", "plugin-skill", "--mcp", "maui-devflow", "--env", "Claude");
		Assert.Equal(1, updated.Exit);
		Assert.Equal("Could not download skill 'plugin-skill' completely.",
			Assert.Single(updated.Result["results"]!.AsArray())!["error"]!.GetValue<string>());
		AssertPinnedRequests(source.Requests, StrictPluginSource.SecondCommit);
		Assert.Contains(source.Requests, uri => uri == StrictPluginSource.Raw(StrictPluginSource.SecondCommit,
			StrictPluginSource.SkillPath + "/references/usage.md"));
		Assert.Equal(before, CaptureFiles(test.Root));
		await AssertInstalledFiles(Path.Combine(test.Root, ".claude", "skills", "plugin-skill"), StrictPluginSource.FirstCommit);
		Assert.Equal("previous-maui", JsonNode.Parse(File.ReadAllText(Path.Combine(test.Root, ".mcp.json")))!["mcpServers"]!["maui-devflow"]!["command"]!.GetValue<string>());
	}

	private static string[] CaptureFiles(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
		.Order(StringComparer.Ordinal).Select(path => Directory.Exists(path) ? path + "/" :
			path + ":" + Convert.ToBase64String(File.ReadAllBytes(path)) + ":" + File.GetLastWriteTimeUtc(path).Ticks).ToArray();

	private static void AssertOrigin(JsonNode origin, string commit)
	{
		Assert.Equal(StrictPluginSource.Repo, origin["repo"]!.GetValue<string>());
		Assert.Equal(StrictPluginSource.Branch, origin["branch"]!.GetValue<string>());
		Assert.Equal(StrictPluginSource.SkillPath, origin["path"]!.GetValue<string>());
		Assert.Equal(commit, origin["resolvedCommit"]!.GetValue<string>());
	}

	private static async Task AssertInstalledFiles(string directory, string commit)
	{
		var expected = StrictPluginSource.Files(commit);
		Assert.Equal(expected.Keys.Append(".skill-version").Order(StringComparer.Ordinal),
			Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/')).Order(StringComparer.Ordinal));
		foreach (var (path, bytes) in expected)
			Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, path)));
		var version = await SkillVersionStore.ReadAsync(directory);
		Assert.NotNull(version);
		Assert.Equal(StrictPluginSource.Repo, version.Source);
		Assert.Equal(StrictPluginSource.Branch, version.Branch);
		Assert.Equal(StrictPluginSource.SkillPath, version.PluginPath);
		Assert.Equal(commit, version.ResolvedCommit);
		Assert.Equal(AiContentHash.DirectoryHash(directory), version.ContentHash);
		Assert.Equal(StrictPluginSource.History(commit), version.Commit);
	}

	private static void AssertPinnedRequests(List<Uri> requests, string commit)
	{
		Assert.Equal(StrictPluginSource.ResolveUri, Assert.Single(requests, uri => uri.AbsolutePath.Contains("/commits/", StringComparison.Ordinal)));
		foreach (var uri in requests.Where(uri => uri != StrictPluginSource.ResolveUri))
		{
			if (uri.Host == "raw.githubusercontent.com")
				Assert.StartsWith($"/{StrictPluginSource.Repo}/{commit}/", uri.AbsolutePath);
			else if (uri.AbsolutePath.Contains("/git/trees/", StringComparison.Ordinal))
				Assert.Equal(StrictPluginSource.TreeUri(commit), uri);
			else
				Assert.Equal(StrictPluginSource.HistoryUri(commit), uri);
		}
	}

	private sealed class StrictPluginSource : HttpMessageHandler
	{
		internal const string Repo = "strict/source";
		internal const string Branch = "release";
		internal const string SkillPath = "plugins/test/skills/plugin-skill";
		internal const string FirstCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		internal const string SecondCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
		internal string CurrentCommit { get; set; } = FirstCommit;
		internal bool MissingSidecar { get; set; }
		internal List<Uri> Requests { get; } = [];
		internal static Uri ResolveUri => new($"https://api.github.com/repos/{Repo}/commits/{Branch}");
		internal static string Tree(string commit) => new(commit == FirstCommit ? 'c' : 'd', 40);
		internal static string History(string commit) => new(commit == FirstCommit ? 'e' : 'f', 40);
		internal static Uri TreeUri(string commit) => new($"https://api.github.com/repos/{Repo}/git/trees/{Tree(commit)}?recursive=1");
		internal static Uri HistoryUri(string commit) => new($"https://api.github.com/repos/{Repo}/commits?sha={commit}&path={Uri.EscapeDataString(SkillPath)}&per_page=1");
		internal static Uri Raw(string commit, string path) => new($"https://raw.githubusercontent.com/{Repo}/{commit}/{path}");
		internal static Dictionary<string, byte[]> Files(string commit) => new()
		{
			["SKILL.md"] = Encoding.UTF8.GetBytes($"---\nname: plugin-skill\ndescription: plugin-only skill\n---\nSnapshot {commit}"),
			["assets/pixel.bin"] = commit == FirstCommit ? [0, 255, 128, 1, 13, 10, 0] : [255, 0, 129, 2, 0, 254, 253, 252],
			["references/usage.md"] = Encoding.UTF8.GetBytes($"Nested reference for {commit}\n")
		};

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri!;
			Requests.Add(uri);
			if (uri == ResolveUri)
				return Json(new JsonObject { ["sha"] = CurrentCommit, ["commit"] = new JsonObject { ["tree"] = new JsonObject { ["sha"] = Tree(CurrentCommit) } } }.ToJsonString());
			foreach (var commit in new[] { FirstCommit, SecondCommit })
			{
				if (uri == TreeUri(commit))
					return Json(new JsonObject { ["tree"] = new JsonArray(Files(commit).Keys.Select(path =>
						(JsonNode)new JsonObject { ["path"] = $"{SkillPath}/{path}", ["type"] = "blob" }).ToArray()) }.ToJsonString());
				if (uri == HistoryUri(commit)) return Json(new JsonArray(new JsonObject { ["sha"] = History(commit) }).ToJsonString());
				if (uri == Raw(commit, ".github/plugin/marketplace.json"))
					return Json("""{"plugins":[{"name":"test","source":"plugins/test"}]}""");
				if (uri == Raw(commit, "plugins/test/plugin.json"))
					return Json("""{"name":"test","skills":["skills"]}""");
				foreach (var (path, bytes) in Files(commit))
					if (uri == Raw(commit, $"{SkillPath}/{path}"))
						return Task.FromResult(MissingSidecar && commit == SecondCommit && path == "references/usage.md"
							? new HttpResponseMessage(HttpStatusCode.NotFound)
							: new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
			}
			throw new InvalidOperationException($"Unexpected source request (no suffix matching or fallback): {uri}");
		}

		private static Task<HttpResponseMessage> Json(string body) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
	}
}
