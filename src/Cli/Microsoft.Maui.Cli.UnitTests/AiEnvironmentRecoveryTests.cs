using System.CommandLine;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiEnvironmentRecoveryTests
{
	[Fact]
	public async Task EmittedRecoveryCommand_PreservesForceForAConflictCreatedAfterExtraction()
	{
		using var test = new AiCommandFixture();
		var initial = await test.Invoke("add", "agent", "maui-helper", "--force", "--dry-run");
		Assert.Equal(1, initial.Exit);
		var error = Assert.Single(initial.Result["results"]!.AsArray())!["error"]!.GetValue<string>();
		var example = Assert.Single(error.Split('\n'), line => line.TrimStart().StartsWith("maui ai ", StringComparison.Ordinal)).Trim();
		Assert.Empty(test.Handler.Requests);

		var agentPath = Path.Combine(test.Root, ".github", "agents", "maui-helper.agent.md");
		Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);
		File.WriteAllText(agentPath, "---\nname: maui-helper\n---\nLocally authored conflicting agent instructions.");
		var before = test.Snapshot();
		var parse = Program.BuildRootCommand().Parse(example["maui ".Length..] + " --json --ci");
		Assert.Empty(parse.Errors);
		Assert.True(parse.GetValue((Option<bool>)parse.CommandResult.Command.Options.Single(option => option.Name == "--force")));
		var originalOutput = Console.Out;
		using var writer = new StringWriter();
		try
		{
			Console.SetOut(writer);
			Assert.Equal(0, await parse.InvokeAsync());
		}
		finally { Console.SetOut(originalOutput); }
		var recovered = JsonNode.Parse(writer.ToString())!;
		Assert.True(recovered["dryRun"]!.GetValue<bool>());
		var agent = Assert.Single(recovered["results"]!.AsArray())!;
		Assert.Equal("adopt", agent["action"]!.GetValue<string>());
		Assert.Equal("planned", agent["outcome"]!.GetValue<string>());
		Assert.True(agent["requiresForce"]!.GetValue<bool>());
		Assert.False(agent["managed"]!.GetValue<bool>());
		Assert.Equal(before, test.Snapshot());
	}

	[Theory]
	[InlineData("expert-reviewer", false)]
	[InlineData("Comet Squad", false)]
	[InlineData("expert-reviewer", true)]
	[InlineData("Comet Squad", true)]
	public async Task EmittedRecoveryCommand_PreservesSelectionsAndExecutesWithCompatibleClient(string agentName, bool mixedInit)
	{
		using var test = new AiCommandFixture();
		using var handler = new NamedAgentHandler(test.Handler, agentName);
		AiCommands.HttpClientFactoryForTests = () => new HttpClient(handler, disposeHandler: false);
		string[] selection = mixedInit
			? ["init", "--skill", "maui-devflow-debug", "--agent", agentName]
			: ["add", "agent", agentName];
		var before = test.Snapshot();
		var initial = await test.Invoke([.. selection, "--repo", "custom/source", "--branch", "feature/recovery", "--force", "--dry-run"]);
		Assert.Equal(1, initial.Exit);
		var error = Assert.Single(initial.Result["results"]!.AsArray())!["error"]!.GetValue<string>();
		var example = Assert.Single(error.Split('\n'), line => line.TrimStart().StartsWith("maui ai ", StringComparison.Ordinal)).Trim();
		Assert.Contains("--env VsCode", example);
		if (agentName.Contains(' ')) Assert.Contains("\"Comet Squad\"", example);
		Assert.Equal(before, test.Snapshot());
		Assert.Empty(test.Handler.Requests);

		var originalOutput = Console.Out;
		using var writer = new StringWriter();
		int exit;
		try
		{
			Console.SetOut(writer);
			exit = await Program.BuildRootCommand().Parse(example["maui ".Length..] + " --json --ci").InvokeAsync();
		}
		finally { Console.SetOut(originalOutput); }
		Assert.Equal(0, exit);
		var recovered = JsonNode.Parse(writer.ToString())!;
		Assert.True(recovered["dryRun"]!.GetValue<bool>());
		var results = recovered["results"]!.AsArray();
		Assert.Equal(mixedInit ? 2 : 1, results.Count);
		var agent = Assert.Single(results, result => result!["kind"]!.GetValue<string>() == "agent")!;
		Assert.Equal(agentName, agent["name"]!.GetValue<string>());
		Assert.Equal("planned", agent["outcome"]!.GetValue<string>());
		Assert.Equal("custom/source", agent["origin"]!["repo"]!.GetValue<string>());
		Assert.Equal("feature/recovery", agent["origin"]!["branch"]!.GetValue<string>());
		Assert.Equal("VsCode", Assert.Single(agent["environments"]!.AsArray())!.GetValue<string>());
		if (mixedInit)
			Assert.Equal("maui-devflow-debug", Assert.Single(results, result => result!["kind"]!.GetValue<string>() == "skill")!["name"]!.GetValue<string>());
		Assert.Equal(before, test.Snapshot());
		Assert.NotEmpty(test.Handler.Requests);
	}

	private sealed class NamedAgentHandler(HttpMessageHandler innerHandler, string name) : DelegatingHandler(innerHandler)
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = await base.SendAsync(request, cancellationToken);
			if (request.RequestUri!.AbsolutePath.EndsWith(".agent.md", StringComparison.Ordinal))
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				response.Content.Dispose();
				response.Content = new StringContent(content.Replace("name: maui-helper", "name: " + name, StringComparison.Ordinal));
			}
			return response;
		}
	}
}
