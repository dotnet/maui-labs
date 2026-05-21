// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AiCommandsTests
{
	[Fact]
	public void Create_ReturnsCommandNamedAi()
	{
		var command = AiCommands.Create();

		Assert.NotNull(command);
		Assert.Equal("ai", command.Name);
	}

	[Fact]
	public void Create_HasFiveSubcommands()
	{
		var command = AiCommands.Create();

		Assert.Equal(5, command.Subcommands.Count);
	}

	[Theory]
	[InlineData("init")]
	[InlineData("list")]
	[InlineData("status")]
	[InlineData("update")]
	[InlineData("add")]
	public void Create_ContainsExpectedSubcommand(string subcommandName)
	{
		var command = AiCommands.Create();

		Assert.Contains(command.Subcommands, c => c.Name == subcommandName);
	}

	[Fact]
	public void InitCommand_HasExpectedOptions()
	{
		var command = AiCommands.Create();
		var init = Assert.Single(command.Subcommands, c => c.Name == "init");

		Assert.Contains(init.Options, o => o.Name == "--repo");
		Assert.Contains(init.Options, o => o.Name == "--branch");
		Assert.Contains(init.Options, o => o.Name == "--force");
		Assert.Contains(init.Options, o => o.Name == "--no-mcp");
		Assert.Contains(init.Options, o => o.Name == "--skill");
		Assert.Contains(init.Options, o => o.Name == "--env");
	}

	[Fact]
	public void ListCommand_HasRepoAndBranchOptions()
	{
		var command = AiCommands.Create();
		var list = Assert.Single(command.Subcommands, c => c.Name == "list");

		Assert.Contains(list.Options, o => o.Name == "--repo");
		Assert.Contains(list.Options, o => o.Name == "--branch");
	}

	[Fact]
	public void StatusCommand_HasCheckUpdatesOption()
	{
		var command = AiCommands.Create();
		var status = Assert.Single(command.Subcommands, c => c.Name == "status");

		Assert.Contains(status.Options, o => o.Name == "--repo");
		Assert.Contains(status.Options, o => o.Name == "--branch");
		Assert.Contains(status.Options, o => o.Name == "--check-updates");
	}

	[Fact]
	public void UpdateCommand_HasExpectedOptions()
	{
		var command = AiCommands.Create();
		var update = Assert.Single(command.Subcommands, c => c.Name == "update");

		Assert.Contains(update.Options, o => o.Name == "--repo");
		Assert.Contains(update.Options, o => o.Name == "--branch");
		Assert.Contains(update.Options, o => o.Name == "--force");
		Assert.Contains(update.Options, o => o.Name == "--skill");
	}

	[Fact]
	public void AddCommand_HasRequiredSkillArgument()
	{
		var command = AiCommands.Create();
		var add = Assert.Single(command.Subcommands, c => c.Name == "add");

		var skillArg = Assert.Single(add.Arguments);
		Assert.Equal("skill", skillArg.Name);
	}

	[Fact]
	public void AddCommand_HasExpectedOptions()
	{
		var command = AiCommands.Create();
		var add = Assert.Single(command.Subcommands, c => c.Name == "add");

		Assert.Contains(add.Options, o => o.Name == "--repo");
		Assert.Contains(add.Options, o => o.Name == "--branch");
		Assert.Contains(add.Options, o => o.Name == "--force");
		Assert.Contains(add.Options, o => o.Name == "--no-mcp");
		Assert.Contains(add.Options, o => o.Name == "--env");
	}

	[Fact]
	public void BranchOption_HasShortAlias()
	{
		var command = AiCommands.Create();
		var init = Assert.Single(command.Subcommands, c => c.Name == "init");
		var branch = Assert.Single(init.Options, o => o.Name == "--branch");

		Assert.Contains("-b", branch.Aliases);
	}

	[Fact]
	public void ForceOption_HasShortAlias()
	{
		var command = AiCommands.Create();
		var init = Assert.Single(command.Subcommands, c => c.Name == "init");
		var force = Assert.Single(init.Options, o => o.Name == "--force");

		Assert.Contains("-y", force.Aliases);
	}

	[Fact]
	public void AiCommand_AllOptionsHaveValidAliases()
	{
		var command = AiCommands.Create();

		AssertNoWhitespaceAliases(command);
	}

	[Fact]
	public void BuildRootCommand_IncludesAiSubcommand()
	{
		var rootCommand = Program.BuildRootCommand();

		Assert.Contains(rootCommand.Subcommands, c => c.Name == "ai");
	}

	[Theory]
	[InlineData("maui-devflow-onboard")]
	[InlineData("maui-devflow-debug")]
	[InlineData("maui-devflow-session-review")]
	[InlineData("maui-ai-debugging")]
	public void IsDevFlowManagedSkillName_KnownDevFlowSkill_ReturnsTrue(string skillName)
	{
		Assert.True(AiCommands.IsDevFlowManagedSkillName(skillName));
	}

	[Fact]
	public void IsDevFlowManagedSkillName_NonDevFlowSkill_ReturnsFalse()
	{
		Assert.False(AiCommands.IsDevFlowManagedSkillName("android-slim-bindings"));
	}

	[Fact]
	public void GetDevFlowBootstrapTargets_DeduplicatesSharedGitHubSkillTarget()
	{
		var environments = new[]
		{
			new DetectedEnvironment
			{
				Kind = AgentEnvironmentKind.VsCode,
				SkillsDirectory = Path.Combine("repo", ".github", "skills")
			},
			new DetectedEnvironment
			{
				Kind = AgentEnvironmentKind.CopilotCli,
				SkillsDirectory = Path.Combine("repo", ".github", "skills")
			}
		};

		var targets = AiCommands.GetDevFlowBootstrapTargets(environments);

		var target = Assert.Single(targets);
		Assert.Equal("github", target.Target);
		Assert.Null(target.CustomPath);
	}

	[Fact]
	public void GetDevFlowBootstrapTargets_OpenCode_UsesCustomPath()
	{
		var environments = new[]
		{
			new DetectedEnvironment
			{
				Kind = AgentEnvironmentKind.OpenCode,
				SkillsDirectory = Path.Combine("repo", ".opencode", "skills")
			}
		};

		var target = Assert.Single(AiCommands.GetDevFlowBootstrapTargets(environments));

		Assert.Equal("auto", target.Target);
		Assert.Equal(Path.Combine(".opencode", "skills"), target.CustomPath);
	}

	[Fact]
	public void GetDevFlowStatusRows_MapsBundledSkillStatus()
	{
		var result = new JsonObject
		{
			["skills"] = new JsonArray(new JsonObject
			{
				["skillId"] = "maui-devflow-debug",
				["installedVersion"] = "1.0.0",
				["status"] = "up-to-date",
				["path"] = ".github/skills/maui-devflow-debug"
			})
		};
		var target = new AiDevFlowBootstrapTarget(
			"project",
			"github",
			null,
			"VsCode",
			Path.Combine("repo", ".github", "skills"));

		var row = Assert.Single(AiCommands.GetDevFlowStatusRows(result, target));

		Assert.Equal("maui-devflow-debug", row.Item);
		Assert.Equal("DevFlow", row.Type);
		Assert.Equal("VsCode", row.Target);
		Assert.Equal("1.0.0", row.Installed);
		Assert.Equal("up-to-date", row.Status);
		Assert.Equal(".github/skills/maui-devflow-debug", row.Path);
	}

	[Theory]
	[InlineData("up-to-date", false, false)]
	[InlineData("Missing", false, true)]
	[InlineData("missing", false, true)]
	[InlineData("Update available", false, true)]
	[InlineData("update-available-from-current-cli", false, true)]
	[InlineData("installed-from-newer-cli", false, true)]
	[InlineData("up-to-date", true, true)]
	public void NeedsUpdate_RecognizesActionableStatuses(string status, bool force, bool expected)
	{
		var row = new AiCommands.AiAssetStatusRow("item", "Skill", "Claude", "", status, "path");

		Assert.Equal(expected, AiCommands.NeedsUpdate(row, force));
	}

	[Fact]
	public void FilterEnvironments_NoFilter_ReturnsAllEnvironments()
	{
		var environments = new[]
		{
			new DetectedEnvironment { Kind = AgentEnvironmentKind.Claude },
			new DetectedEnvironment { Kind = AgentEnvironmentKind.VsCode }
		};

		Assert.Equal(2, AiCommands.FilterEnvironments(environments, envFilter: null).Count);
	}

	[Fact]
	public void FilterEnvironments_EnvFilter_ReturnsMatchingEnvironment()
	{
		var environments = new[]
		{
			new DetectedEnvironment { Kind = AgentEnvironmentKind.Claude },
			new DetectedEnvironment { Kind = AgentEnvironmentKind.VsCode }
		};

		var env = Assert.Single(AiCommands.FilterEnvironments(environments, ["VsCode"]));

		Assert.Equal(AgentEnvironmentKind.VsCode, env.Kind);
	}

	[Fact]
	public void ShouldCreateDefaultClaudeEnvironment_NoFilter_ReturnsTrue()
	{
		Assert.True(AiCommands.ShouldCreateDefaultClaudeEnvironment(envFilter: null));
	}

	[Theory]
	[InlineData("Claude", true)]
	[InlineData("claude", true)]
	[InlineData("VsCode", false)]
	[InlineData("CopilotCli", false)]
	public void ShouldCreateDefaultClaudeEnvironment_RespectsEnvFilter(string envFilter, bool expected)
	{
		Assert.Equal(expected, AiCommands.ShouldCreateDefaultClaudeEnvironment([envFilter]));
	}

	[Fact]
	public void GetAiCommandWorkingDirectory_SubdirectoryUnderGitRoot_ReturnsGitRoot()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		try
		{
			var subdirectory = Path.Combine(tempDir, "src", "MyApp");
			Directory.CreateDirectory(Path.Combine(tempDir, ".git"));
			Directory.CreateDirectory(subdirectory);

			var workingDir = AiCommands.GetAiCommandWorkingDirectory(subdirectory);

			Assert.Equal(Path.GetFullPath(tempDir), workingDir);
		}
		finally
		{
			if (Directory.Exists(tempDir))
				Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void FileSystemPathComparer_MatchesCurrentPlatformSemantics()
	{
		var paths = new HashSet<string>(AiCommands.FileSystemPathComparer)
		{
			Path.Combine("repo", ".github", "skills", "Foo"),
			Path.Combine("repo", ".github", "skills", "foo")
		};

		Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, paths.Count);
	}

	[Theory]
	[InlineData(false, "success")]
	[InlineData(true, "partial")]
	public void GetInitStatus_ReflectsInstallFailures(bool hasInstallFailures, string expected)
	{
		Assert.Equal(expected, AiCommands.GetInitStatus(hasInstallFailures));
	}

	[Theory]
	[InlineData(new[] { 1, 2 }, new[] { 0 }, false)]
	[InlineData(new[] { -2, 1 }, new[] { 0 }, true)]
	[InlineData(new[] { 1 }, new[] { -1 }, true)]
	[InlineData(new[] { 1 }, new[] { -2 }, true)]
	public void HasInitInstallFailures_DetectsNegativeFileCounts(int[] skillFileCounts, int[] assetFileCounts, bool expected)
	{
		Assert.Equal(expected, AiCommands.HasInitInstallFailures(skillFileCounts, assetFileCounts));
	}

	[Theory]
	[InlineData(new[] { 1, 2 }, new[] { 1 }, false)]
	[InlineData(new[] { -2, 1 }, new[] { 1 }, true)]
	[InlineData(new[] { 0, 1 }, new[] { 1 }, true)]
	[InlineData(new[] { 1 }, new[] { -1 }, true)]
	[InlineData(new[] { 1 }, new[] { 0 }, true)]
	public void HasUpdateInstallFailures_DetectsFailedUpdates(int[] skillFileCounts, int[] assetFileCounts, bool expected)
	{
		Assert.Equal(expected, AiCommands.HasUpdateInstallFailures(skillFileCounts, assetFileCounts));
	}

	[Theory]
	[InlineData(false, 0, 0, 0, true)]
	[InlineData(true, 1, 0, 0, true)]
	[InlineData(true, 0, 1, 0, true)]
	[InlineData(true, 0, 0, 1, true)]
	[InlineData(true, 0, 0, 0, false)]
	public void HasUpdateFilterMatches_RequiresAtLeastOneMatchedTargetWhenFiltered(
		bool filterSpecified,
		int devFlowTargetCount,
		int selectedAgentAssetCount,
		int installedSkillMatchCount,
		bool expected)
	{
		Assert.Equal(
			expected,
			AiCommands.HasUpdateFilterMatches(
				filterSpecified,
				devFlowTargetCount,
				selectedAgentAssetCount,
				installedSkillMatchCount));
	}

	[Fact]
	public void CreateGitHubHttpClient_ConfiguresTimeout()
	{
		using var http = AiCommands.CreateGitHubHttpClient();

		Assert.Equal(AiCommands.GitHubHttpTimeout, http.Timeout);
	}

	[Theory]
	[InlineData(false, "success")]
	[InlineData(true, "partial_failure")]
	public void GetUpdateStatus_ReflectsUpdateFailures(bool hasUpdateFailures, string expected)
	{
		Assert.Equal(expected, AiCommands.GetUpdateStatus(hasUpdateFailures));
	}

	private static void AssertNoWhitespaceAliases(Command command)
	{
		foreach (var option in command.Options)
		{
			Assert.False(
				option.Name.Any(char.IsWhiteSpace),
				$"Option name contains whitespace: \"{option.Name}\" in command '{command.Name}'");

			foreach (var alias in option.Aliases)
			{
				Assert.False(
					alias.Any(char.IsWhiteSpace),
					$"Option alias contains whitespace: \"{alias}\" in command '{command.Name}'");
			}
		}

		foreach (var subcommand in command.Subcommands)
		{
			AssertNoWhitespaceAliases(subcommand);
		}
	}
}
