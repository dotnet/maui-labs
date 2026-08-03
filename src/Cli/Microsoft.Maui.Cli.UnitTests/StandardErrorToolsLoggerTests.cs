// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Providers.Apple;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// The Apple tools library ships <c>ConsoleLogger</c>, which writes Info/Debug/Warning to
/// stdout. That corrupts <c>--json</c> output, so the CLI substitutes this logger. These
/// tests pin the behaviour that keeps stdout clean.
/// </summary>
public class StandardErrorToolsLoggerTests
{
	[Fact]
	public void LogInfo_WritesToTheConfiguredWriter()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogInfo("Executing: {0}", "/usr/bin/xcrun");

		Assert.Equal($"Info: Executing: /usr/bin/xcrun{Environment.NewLine}", writer.ToString());
	}

	[Fact]
	public void LogWarning_WritesWarningPrefix()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogWarning("Xcode {0} is unsupported", "12.0");

		Assert.Equal($"Warning: Xcode 12.0 is unsupported{Environment.NewLine}", writer.ToString());
	}

	[Fact]
	public void LogError_WithoutException_WritesErrorPrefix()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogError("simctl failed", null);

		Assert.Equal($"Error: simctl failed{Environment.NewLine}", writer.ToString());
	}

	[Fact]
	public void LogError_WithException_IncludesTheException()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogError("simctl failed", new InvalidOperationException("boom"));

		var output = writer.ToString();
		Assert.StartsWith("Error: simctl failed", output);
		Assert.Contains("boom", output);
	}

	[Fact]
	public void LogDebug_IsSuppressedByDefault()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogDebug("noisy detail");

		Assert.Equal(string.Empty, writer.ToString());
	}

	[Fact]
	public void LogDebug_IsWrittenWhenVerbose()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer, verbose: true);

		logger.LogDebug("noisy detail");

		Assert.Equal($"Debug: noisy detail{Environment.NewLine}", writer.ToString());
	}

	[Fact]
	public void LogDebug_FollowsDefaultVerboseWhenNotSpecified()
	{
		// `maui --verbose ...` has to reach providers built by DI, which never see the
		// parse result, so verbosity is published through a static default.
		var original = StandardErrorToolsLogger.DefaultVerbose;

		try
		{
			StandardErrorToolsLogger.DefaultVerbose = true;

			var writer = new StringWriter();
			new StandardErrorToolsLogger(writer).LogDebug("noisy detail");

			Assert.Equal($"Debug: noisy detail{Environment.NewLine}", writer.ToString());
		}
		finally
		{
			StandardErrorToolsLogger.DefaultVerbose = original;
		}
	}

	[Fact]
	public void ExplicitVerbose_OverridesDefaultVerbose()
	{
		var original = StandardErrorToolsLogger.DefaultVerbose;

		try
		{
			StandardErrorToolsLogger.DefaultVerbose = true;

			var writer = new StringWriter();
			new StandardErrorToolsLogger(writer, verbose: false).LogDebug("noisy detail");

			Assert.Equal(string.Empty, writer.ToString());
		}
		finally
		{
			StandardErrorToolsLogger.DefaultVerbose = original;
		}
	}

	[Fact]
	public void Log_MalformedFormatString_FallsBackToRawMessage()
	{
		// Upstream messages sometimes contain literal braces (paths, JSON); a bad template
		// must not take the command down.
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogInfo("unbalanced {brace", "arg");

		Assert.Equal($"Info: unbalanced {{brace{Environment.NewLine}", writer.ToString());
	}

	[Fact]
	public void Log_MessageWithBracesAndNoArgs_IsNotFormatted()
	{
		var writer = new StringWriter();
		var logger = new StandardErrorToolsLogger(writer);

		logger.LogInfo("{\"key\": \"value\"}");

		Assert.Equal($"Info: {{\"key\": \"value\"}}{Environment.NewLine}", writer.ToString());
	}

	[Fact]
	public void DefaultWriter_IsStandardError()
	{
		var stdout = new StringWriter();
		var stderr = new StringWriter();
		var originalOut = Console.Out;
		var originalError = Console.Error;

		try
		{
			Console.SetOut(stdout);
			Console.SetError(stderr);

			// Constructed after the redirect so the default Console.Error is the fake.
			new StandardErrorToolsLogger().LogInfo("hello");
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}

		Assert.Equal(string.Empty, stdout.ToString());
		Assert.Equal($"Info: hello{Environment.NewLine}", stderr.ToString());
	}
}
