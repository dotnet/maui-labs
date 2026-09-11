// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Ai;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Root command group for explicitly typed AI assets.
/// </summary>
public static partial class AiCommands
{
	internal static readonly TimeSpan GitHubHttpTimeout = TimeSpan.FromSeconds(30);
	internal static Func<HttpClient>? HttpClientFactoryForTests { get; set; }

	private static readonly HashSet<string> s_devFlowManagedSkills = new(StringComparer.OrdinalIgnoreCase)
	{
		"maui-devflow-onboard",
		"maui-devflow-debug",
		"maui-devflow-session-review",
		"maui-ai-debugging",
		"maui-devflow-connect",
		"devflow-connect",
		"devflow-onboard",
		"devflow-debug"
	};

	public static Command Create()
	{
		var command = new Command("ai", "Bootstrap and manage MAUI skills, agents, and MCP configuration. Skills and agents are project-scoped, honoring detected nested skill directories within the current Git project (or current directory outside Git). MCP configuration uses the selected client's project location, except Copilot CLI MCP is user-wide (~/.copilot/mcp-config.json). Use --dry-run to preview; --ci or --json runs without prompts.");
		command.Add(CreateAssetCommand("init"));
		foreach (var name in new[] { "list", "status", "update" })
			command.Add(CreateAssetCommand(name));
		var add = new Command("add", "Add one explicitly named skill, agent, or known MCP registration.");
		foreach (var kind in Enum.GetValues<AiAssetKind>())
			add.Add(CreateAssetCommand("add", kind));
		command.Add(add);
		return command;
	}

	internal static bool IsDevFlowManagedSkillName(string skillName) =>
		s_devFlowManagedSkills.Contains(skillName);

	/// <summary>
	/// Creates the replacement/adoption permission, separate from prompt acceptance.
	/// </summary>
	static Option<bool> CreateForceOption() =>
		new("--force") { Description = "Allow replacing customized content or adopting an unmanaged asset, including downgrading bundled DevFlow skills to this CLI's version; does not accept prompts" };

	/// <summary>
	/// Creates an <see cref="HttpClient"/> configured for GitHub API access.
	/// Respects the <c>GITHUB_TOKEN</c> environment variable for authentication.
	/// </summary>
	internal static HttpClient CreateGitHubHttpClient()
	{
		if (HttpClientFactoryForTests is { } factory)
			return factory();

		var http = new HttpClient
		{
			Timeout = GitHubHttpTimeout
		};
		http.DefaultRequestHeaders.UserAgent.Add(
			new System.Net.Http.Headers.ProductInfoHeaderValue("Microsoft.Maui.Cli", "1.0"));
		http.DefaultRequestHeaders.Accept.Add(
			new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

		var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
		if (!string.IsNullOrEmpty(token))
			http.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

		return http;
	}
}
