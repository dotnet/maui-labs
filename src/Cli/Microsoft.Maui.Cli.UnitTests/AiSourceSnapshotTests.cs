using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiSourceSnapshotTests
{
	[Theory]
	[InlineData("""{"commit":{"tree":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}}""")]
	[InlineData("""{"sha":"tree","commit":{"tree":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}}""")]
	[InlineData("""{"sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","commit":{"tree":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}}""")]
	public async Task InvalidOrMismatchedCommitResponse_NeverFallsBackToMutableSource(string response)
	{
		using var test = new AiCommandFixture();
		using var handler = new InvalidSnapshotHandler(response);
		using var http = new HttpClient(handler);
		var service = new AiAssetService(http, test.Root, []);
		var request = new AiAssetRequest("add", AiAssetKind.Skill,
			new() { [AiAssetKind.Skill] = ["custom-skill"] }, [AgentEnvironmentKind.Claude],
			null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false, false);
		var before = test.Snapshot();
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanAsync(request, CancellationToken.None));
		Assert.Equal(1, handler.Requests);
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task MovingRef_AllRequestsAndAppliedBytesUseOneSnapshotPerPlan()
	{
		using var test = new AiCommandFixture();
		using var http = new HttpClient(test.Handler, disposeHandler: false);
		var service = new AiAssetService(http, test.Root, []);
		var request = new AiAssetRequest("init", null,
			new() { [AiAssetKind.Skill] = ["custom-skill"], [AiAssetKind.Agent] = ["maui-helper"] },
			[AgentEnvironmentKind.VsCode], "custom/source", "release", false, false);
		test.Handler.MoveAfterEveryRequest = true;
		var first = await service.PlanAsync(request, CancellationToken.None);
		var commit = AiTestCatalog.CommitFor("initial");
		Assert.Equal(2, first.Count);
		Assert.All(first, asset =>
		{
			Assert.Equal("release", asset.Origin!.Branch);
			Assert.Equal(commit, asset.Origin.ResolvedCommit);
		});
		AssertPinnedRequests(test.Handler.Requests, commit, "release");
		Assert.Contains(test.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/marketplace.json", StringComparison.Ordinal));
		Assert.Contains(test.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/plugin.json", StringComparison.Ordinal));
		Assert.Contains(test.Handler.Requests, uri => uri.AbsolutePath.EndsWith(".agent.md", StringComparison.Ordinal));
		var requestCount = test.Handler.Requests.Count;
		await service.ExecuteAsync(request, first, CancellationToken.None);
		Assert.Equal(requestCount, test.Handler.Requests.Count);
		Assert.All(first, asset => Assert.Equal("succeeded", asset.Outcome));
		Assert.Contains("initial", File.ReadAllText(Path.Combine(test.Root, ".github", "skills", "custom-skill", "SKILL.md")));
		Assert.Contains("initial", File.ReadAllText(Path.Combine(test.Root, ".github", "agents", "maui-helper.agent.md")));
		Assert.Equal(commit, (await SkillVersionStore.ReadAsync(Path.Combine(test.Root, ".github", "skills", "custom-skill")))!.ResolvedCommit);
		Assert.Equal(commit, Assert.Single(AiAssetRegistry.Inventory(test.Root, "project")).Origin!.ResolvedCommit);

		test.Handler.Requests.Clear();
		var nextRevision = test.Handler.Revision;
		var second = await service.PlanAsync(request with { Command = "update" }, CancellationToken.None);
		var nextCommit = AiTestCatalog.CommitFor(nextRevision);
		Assert.NotEqual(commit, nextCommit);
		Assert.All(second, asset => Assert.Equal(nextCommit, asset.Origin!.ResolvedCommit));
		AssertPinnedRequests(test.Handler.Requests, nextCommit, "release");
		await service.ExecuteAsync(request with { Command = "update" }, second, CancellationToken.None);
		Assert.Contains(nextRevision, File.ReadAllText(Path.Combine(test.Root, ".github", "skills", "custom-skill", "SKILL.md")));
	}

	[Theory]
	[InlineData("skill", "custom-skill", "Claude")]
	[InlineData("agent", "maui-helper", "VsCode")]
	public async Task ExplicitCommit_RemainsTrackedAndPinnedAcrossUpdates(string kind, string name, string environment)
	{
		using var test = new AiCommandFixture();
		const string pinned = "abcdef0123456789abcdef0123456789abcdef01";
		var installed = await test.Invoke("add", kind, name, "--env", environment, "--branch", pinned);
		Assert.Equal(0, installed.Exit);
		var installedRow = Assert.Single(installed.Result["results"]!.AsArray())!;
		Assert.Equal(pinned, installedRow["origin"]!["branch"]!.GetValue<string>());
		Assert.Equal(pinned, installedRow["origin"]!["resolvedCommit"]!.GetValue<string>());
		var before = test.Snapshot();
		test.Handler.Revision = "branch-has-moved";
		test.Handler.Requests.Clear();
		var updated = await test.Invoke("update", kind, "--env", environment);
		Assert.Equal(0, updated.Exit);
		var row = Assert.Single(updated.Result["results"]!.AsArray())!;
		Assert.Equal("already-current", row["reasonCode"]!.GetValue<string>());
		Assert.Equal(pinned, row["origin"]!["branch"]!.GetValue<string>());
		Assert.Equal(pinned, row["origin"]!["resolvedCommit"]!.GetValue<string>());
		AssertPinnedRequests(test.Handler.Requests, pinned, pinned);
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task ResolutionFailure_FailsClosedBeforeAnyMutableReadOrWrite()
	{
		using var test = new AiCommandFixture();
		test.Handler.FailResolution = true;
		var before = test.Snapshot();
		var result = await test.Invoke("init", "--skill", "custom-skill", "--mcp", "maui-devflow", "--env", "Claude");
		Assert.Equal(1, result.Exit);
		Assert.Contains("immutable repository commit", result.Result.ToJsonString());
		Assert.EndsWith("/commits/main", Assert.Single(test.Handler.Requests).AbsolutePath);
		Assert.Equal(before, test.Snapshot());
	}

	private static void AssertPinnedRequests(IEnumerable<Uri> requests, string commit, string reference)
	{
		var resolution = Assert.Single(requests, uri => uri.AbsolutePath.Contains("/commits/", StringComparison.Ordinal));
		Assert.EndsWith("/commits/" + reference, resolution.AbsolutePath);
		foreach (var uri in requests.Where(uri => uri != resolution))
		{
			if (uri.Host == "raw.githubusercontent.com")
				Assert.Contains($"/{commit}/", uri.AbsolutePath);
			else if (uri.AbsolutePath.Contains("/git/trees/", StringComparison.Ordinal))
				Assert.EndsWith("/git/trees/" + AiTestCatalog.TreeFor(commit), uri.AbsolutePath);
			else
			{
				Assert.EndsWith("/commits", uri.AbsolutePath);
				Assert.Contains("sha=" + commit, uri.Query);
			}
		}

	}

	private sealed class InvalidSnapshotHandler(string response) : HttpMessageHandler
	{
		internal int Requests { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests++;
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response) });
		}
	}
}
