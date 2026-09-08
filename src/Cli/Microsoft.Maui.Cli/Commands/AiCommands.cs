// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Root command group for AI-assisted MAUI development: install and manage agent skills.
/// </summary>
public static partial class AiCommands
{
	private const string DefaultRepo = "dotnet/maui-labs";
	private const string DefaultBranch = "main";
	private const string RepositorySkillsRoot = ".github/skills";
	private const string RepositorySkillsPluginName = "dotnet-maui-repo";
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
		var command = new Command("ai", "Bootstrap and manage MAUI agent skills, Copilot agents, and MCP configuration. Installs target the current Git project (or current directory outside Git). Use --dry-run to preview; --ci or --json runs without prompts.");
		command.Add(CreateInitCommand());
		command.Add(CreateListCommand());
		command.Add(CreateStatusCommand());
		command.Add(CreateUpdateCommand());
		command.Add(CreateAddCommand());
		return command;
	}

	internal static bool IsDevFlowManagedSkillName(string skillName) =>
		s_devFlowManagedSkills.Contains(skillName);

	internal static string GetBundledSkillId(string name) => name.ToLowerInvariant() switch
	{
		"devflow-onboard" => "maui-devflow-onboard",
		"maui-ai-debugging" or "maui-devflow-connect" or "devflow-connect" or "devflow-debug" => "maui-devflow-debug",
		_ => name.ToLowerInvariant()
	};

	/// <summary>
	/// Creates the shared --repo option used by multiple subcommands.
	/// </summary>
	static Option<string> CreateRepoOption() =>
		new("--repo") { Description = "GitHub repository", DefaultValueFactory = _ => DefaultRepo, Hidden = true };

	/// <summary>
	/// Creates the shared --branch / -b option used by multiple subcommands.
	/// </summary>
	static Option<string> CreateBranchOption() =>
		new("--branch", "-b") { Description = "GitHub branch", DefaultValueFactory = _ => DefaultBranch, Hidden = true };

	/// <summary>
	/// Creates the shared --force / -y option for skipping confirmation prompts.
	/// </summary>
	static Option<bool> CreateForceOption() =>
		new("--force", "-y") { Description = "Skip prompts and replace existing assets and local edits; permits replacing newer DevFlow skills with this CLI's bundle" };

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
