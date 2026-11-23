// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class CliTelemetryPolicyTests
{
	[Fact]
	public void ResolveConnectionString_NoDestination_DisablesReporting() =>
		Assert.Null(CliTelemetryPolicy.ResolveConnectionString(_ => null, null));

	[Fact]
	public void ResolveConnectionString_BuildDestination_EnablesReporting() =>
		Assert.Equal("build-destination", CliTelemetryPolicy.ResolveConnectionString(_ => null, "build-destination"));

	[Fact]
	public void ResolveConnectionString_MauiDestination_OverridesBuildDestination() =>
		Assert.Equal("maui-destination", CliTelemetryPolicy.ResolveConnectionString(
			key => key == CliTelemetryPolicy.ConnectionStringVariable ? " maui-destination " : null, "build-destination"));

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ResolveConnectionString_ExplicitBlank_DisablesBuildDestination(string value) =>
		Assert.Null(CliTelemetryPolicy.ResolveConnectionString(
			key => key == CliTelemetryPolicy.ConnectionStringVariable ? value : null, "build-destination"));

	[Theory]
	[InlineData("MAUI_CLI_TELEMETRY_OPTOUT", "1")]
	[InlineData("MAUI_CLI_TELEMETRY_OPTOUT", "true")]
	[InlineData("MAUI_CLI_TELEMETRY_OPTOUT", " TRUE ")]
	[InlineData("DOTNET_CLI_TELEMETRY_OPTOUT", "1")]
	[InlineData("DOTNET_CLI_TELEMETRY_OPTOUT", "True")]
	[InlineData("OTEL_SDK_DISABLED", "true")]
	[InlineData("OTEL_SDK_DISABLED", "1")]
	public void ResolveConnectionString_AnyOptOut_DisablesBeforeReadingDestination(string name, string value)
	{
		Assert.Null(CliTelemetryPolicy.ResolveConnectionString(key =>
		{
			Assert.NotEqual(CliTelemetryPolicy.ConnectionStringVariable, key);
			return key == name ? value : null;
		}, "build-destination"));
	}

	[Theory]
	[InlineData("false")]
	[InlineData("0")]
	[InlineData(null)]
	public void ResolveConnectionString_NotOptedOut_UsesConfiguredDestination(string? value) =>
		Assert.Equal("build-destination", CliTelemetryPolicy.ResolveConnectionString(
			key => key == CliTelemetryPolicy.ConnectionStringVariable ? null : value, "build-destination"));

	[Fact]
	public void ResolveConnectionString_MauiFalse_CannotOverrideDotnetOptOut() =>
		Assert.Null(CliTelemetryPolicy.ResolveConnectionString(key => key switch
		{
			"MAUI_CLI_TELEMETRY_OPTOUT" => "false",
			"DOTNET_CLI_TELEMETRY_OPTOUT" => "true",
			_ => null
		}, "build-destination"));

	[Fact]
	public void ResolveConnectionString_DoesNotInheritApplicationDestination() =>
		Assert.Null(CliTelemetryPolicy.ResolveConnectionString(
			key => key == "APPLICATIONINSIGHTS_CONNECTION_STRING" ? "application-destination" : null, null));

	[Fact]
	public void GetCommandName_UsesCanonicalCommands_NotArgumentsOrOptionValues()
	{
		var parse = CreateRoot().Parse(["--json", "project", "verify", @"C:\private\My App.csproj", "--token", "secret-value"]);
		Assert.Empty(parse.Errors);
		Assert.Equal("project check", CliTelemetryPolicy.GetCommandName(parse));
	}

	[Theory]
	[InlineData("")]
	[InlineData("--version")]
	[InlineData("version")]
	[InlineData("--help")]
	[InlineData("project check --help")]
	[InlineData("project check --dry-run")]
	[InlineData("project check --unknown secret")]
	[InlineData("unknown-private-command")]
	public void GetCommandName_InformationalInvalidOrDryRun_Skips(string command) =>
		Assert.Null(CliTelemetryPolicy.GetCommandName(CreateRoot().Parse(command)));

	[Fact]
	public void GetCommandName_FalseDryRun_DoesNotSuppressReporting() =>
		Assert.Equal("project check", CliTelemetryPolicy.GetCommandName(CreateRoot().Parse("project check --dry-run false")));

	[Theory]
	[InlineData("project check", false, null, true)]
	[InlineData("project check --json", false, null, false)]
	[InlineData("project check --ci", false, null, false)]
	[InlineData("project check", true, null, false)]
	[InlineData("project check", false, "CI", false)]
	[InlineData("project check", false, "GITHUB_ACTIONS", false)]
	[InlineData("devflow mcp", false, null, false)]
	[InlineData("go", false, null, false)]
	public void ShouldShowNotice_RespectsOutputAndProtocolModes(string command, bool redirected, string? ciVariable, bool expected)
	{
		var parse = CreateRoot().Parse(command);
		Assert.Empty(parse.Errors);
		Assert.Equal(expected, CliTelemetryPolicy.ShouldShowNotice(parse,
			CliTelemetryPolicy.GetCommandName(parse)!, redirected, key => key == ciVariable ? "true" : null));
	}

	internal static RootCommand CreateRoot()
	{
		var check = new Command("check")
		{
			new Argument<string?>("project") { Arity = ArgumentArity.ZeroOrOne },
			new Option<string?>("--token")
		};
		check.Aliases.Add("verify");
		check.SetAction(_ => 0);
		var version = new Command("version");
		version.SetAction(_ => 0);
		var mcp = new Command("mcp");
		mcp.SetAction(_ => 0);
		var go = new Command("go");
		go.SetAction(_ => 0);
		return new RootCommand
		{
			GlobalOptions.JsonOption,
			GlobalOptions.VerboseOption,
			GlobalOptions.DryRunOption,
			GlobalOptions.CiOption,
			new Command("project") { check },
			version,
			new Command("devflow") { mcp },
			go
		};
	}
}
