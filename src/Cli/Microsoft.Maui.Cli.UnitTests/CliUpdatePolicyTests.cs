// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class CliUpdatePolicyTests
{
	[Fact]
	public void IsInteractive_NotificationOptOutDoesNotSuppressExplicitUpdateCompletion() =>
		Assert.True(CliUpdatePolicy.IsInteractive(false, false, false,
			name => name == "MAUI_CLI_NO_UPDATE_CHECK" ? "1" : null));

	[Theory]
	[InlineData(true, false, false, null)]
	[InlineData(false, true, false, null)]
	[InlineData(false, false, true, null)]
	[InlineData(false, false, false, "CI")]
	[InlineData(false, false, false, "GITHUB_ACTIONS")]
	[InlineData(false, false, false, "TF_BUILD")]
	public void IsInteractive_AutomationSuppressesDelayedCompletion(bool json, bool ci, bool redirected, string? key) =>
		Assert.False(CliUpdatePolicy.IsInteractive(json, ci, redirected, name => name == key ? "true" : null));

	[Fact]
	public void ShouldCheck_GlobalOptionsBeforeCommand_AreSupported() =>
		Assert.True(CliUpdatePolicy.ShouldCheck(["--verbose", "true", "version"], false, false, false, false, _ => null, true));

	[Theory]
	[InlineData("doctor", true)]
	[InlineData("version", true)]
	[InlineData("profile", true)]
	[InlineData("update", false)]
	[InlineData("devflow", false)]
	[InlineData("go", false)]
	[InlineData("ai", false)]
	public void ShouldCheck_OnlyOrdinaryCommands(string command, bool expected) =>
		Assert.Equal(expected, CliUpdatePolicy.ShouldCheck([command], false, false, false, false, _ => null, true));

	[Fact]
	public void ShouldCheck_EveryRegisteredCommandHasAnExplicitPolicy()
	{
		string[] ordinary = ["doctor", "device", "android", "apple", "project", "version", "port", "profile"];
		string[] excluded = ["update", "devflow", "go", "ai"];
		foreach (var command in Program.BuildRootCommand().Subcommands)
		{
			Assert.Contains(command.Name, ordinary.Concat(excluded));
			Assert.Equal(ordinary.Contains(command.Name),
				CliUpdatePolicy.ShouldCheck([command.Name], false, false, false, false, _ => null, true));
		}
	}

	[Theory]
	[InlineData(true, false, false, false, true)]
	[InlineData(false, true, false, false, true)]
	[InlineData(false, false, true, false, true)]
	[InlineData(false, false, false, true, true)]
	[InlineData(false, false, false, false, false)]
	public void ShouldCheck_AutomationOrUnsupportedInstall_Skips(bool json, bool ci, bool dryRun, bool redirected, bool global) =>
		Assert.False(CliUpdatePolicy.ShouldCheck(["doctor"], json, ci, dryRun, redirected, _ => null, global));

	[Theory]
	[InlineData("CI", "true")]
	[InlineData("TF_BUILD", "True")]
	[InlineData("GITHUB_ACTIONS", "true")]
	[InlineData("MAUI_CLI_NO_UPDATE_CHECK", "1")]
	public void ShouldCheck_EnvironmentDisablesChecks(string key, string value) =>
		Assert.False(CliUpdatePolicy.ShouldCheck(["doctor"], false, false, false, false, name => name == key ? value : null, true));
}
