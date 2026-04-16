// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class OutputFormatterTests
{
	[Fact]
	public void JsonOutputFormatter_WriteError_ProducesValidJson()
	{
		var sb = new StringBuilder();
		using var writer = new StringWriter(sb);
		var formatter = new JsonOutputFormatter(writer);

		var error = new ErrorResult
		{
			Code = ErrorCodes.JdkNotFound,
			Category = "platform",
			Message = "JDK not found"
		};

		formatter.WriteError(error);
		var output = sb.ToString();

		Assert.Contains("\"code\":", output);
		Assert.Contains("\"E2001\"", output);
		Assert.Contains("\"message\":", output);
		Assert.Contains("JDK not found", output);
	}

	[Fact]
	public void JsonOutputFormatter_Write_ProducesValidJson()
	{
		var sb = new StringBuilder();
		using var writer = new StringWriter(sb);
		var formatter = new JsonOutputFormatter(writer);

		var data = new JsonObject
		{
			["name"] = "test",
			["value"] = 42
		};
		formatter.Write(data);

		var output = sb.ToString();
		Assert.Contains("\"name\":", output);
		Assert.Contains("\"test\"", output);
		Assert.Contains("\"value\":", output);
		Assert.Contains("42", output);
	}

	[Fact]
	public void JsonOutputFormatter_WriteCliCommandResult_ProducesValidJson()
	{
		var sb = new StringBuilder();
		using var writer = new StringWriter(sb);
		var formatter = new JsonOutputFormatter(writer);

		formatter.Write(new CliCommandResult
		{
			Success = true,
			Status = "requires_interaction",
			Message = "Run the command in a terminal",
			Command = "sdkmanager",
			Arguments = "--licenses",
			FullCommand = "sdkmanager --licenses"
		});

		var output = sb.ToString();
		Assert.Contains("\"success\": true", output);
		Assert.Contains("\"status\": \"requires_interaction\"", output);
		Assert.Contains("\"message\": \"Run the command in a terminal\"", output);
		Assert.Contains("\"command\": \"sdkmanager\"", output);
		Assert.Contains("\"arguments\": \"--licenses\"", output);
		Assert.Contains("\"full_command\": \"sdkmanager --licenses\"", output);
	}

	[Fact]
	public void JsonOutputFormatter_WriteException_ConvertsToErrorResult()
	{
		var sb = new StringBuilder();
		using var writer = new StringWriter(sb);
		var formatter = new JsonOutputFormatter(writer);

		var exception = new MauiToolException(
			ErrorCodes.AndroidSdkNotFound,
			"Android SDK not found");

		formatter.WriteError(exception);
		var output = sb.ToString();

		Assert.Contains("\"E2101\"", output);
		Assert.Contains("Android SDK not found", output);
	}

	private static (SpectreOutputFormatter formatter, TestConsole console) CreateTestFormatter(bool verbose = false)
	{
		var console = new TestConsole();
		var formatter = new SpectreOutputFormatter(console, verbose);
		return (formatter, console);
	}

	private static Process StartTestProcessThatWritesStderr()
	{
		var startInfo = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd", "/c echo boom 1>&2")
			: new ProcessStartInfo("/bin/bash", "-lc \"echo boom >&2\"");

		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		startInfo.RedirectStandardInput = true;
		startInfo.CreateNoWindow = true;

		var process = new Process
		{
			StartInfo = startInfo
		};

		Assert.True(process.Start());
		return process;
	}

	[Fact]
	public void SpectreOutputFormatter_WriteSuccess_OutputsMessage()
	{
		var (formatter, console) = CreateTestFormatter();

		formatter.WriteSuccess("Operation completed");
		var output = console.Output;

		Assert.Contains("Operation completed", output);
		Assert.Contains("✓", output);
	}

	[Fact]
	public void SpectreOutputFormatter_WriteWarning_OutputsMessage()
	{
		var (formatter, console) = CreateTestFormatter();

		formatter.WriteWarning("This is a warning");
		var output = console.Output;

		Assert.Contains("This is a warning", output);
		Assert.Contains("⚠", output);
	}

	[Fact]
	public void SpectreOutputFormatter_WriteInfo_OutputsMessage()
	{
		var (formatter, console) = CreateTestFormatter();

		formatter.WriteInfo("Information message");
		var output = console.Output;

		Assert.Contains("Information message", output);
		Assert.Contains("ℹ", output);
	}

	[Fact]
	public void Program_HandleCommandException_ForCancellation_WritesCancelledInsteadOfError()
	{
		var (formatter, console) = CreateTestFormatter();

		var exitCode = Program.HandleCommandException(formatter, new OperationCanceledException());

		Assert.Equal(130, exitCode);
		Assert.Contains("Cancelled.", console.Output);
		Assert.DoesNotContain("Error", console.Output);
	}

	[Fact]
	public async Task MonitoredProcess_Attach_DoesNotEchoStderr_WhenNotVerbose()
	{
		var (formatter, console) = CreateTestFormatter();
		using var process = StartTestProcessThatWritesStderr();
		using var monitored = MonitoredProcess.Attach(process, formatter, useJson: false, verbose: false, prefix: "trace", CancellationToken.None);

		await monitored.WaitForExitAsync();

		Assert.DoesNotContain("boom", console.Output);
		Assert.Contains("boom", monitored.StandardError.ToString());
	}

	[Fact]
	public async Task MonitoredProcess_Attach_EchoesStderr_AsProgress_WhenVerbose()
	{
		var (formatter, console) = CreateTestFormatter(verbose: true);
		using var process = StartTestProcessThatWritesStderr();
		using var monitored = MonitoredProcess.Attach(process, formatter, useJson: false, verbose: true, prefix: "trace", CancellationToken.None);

		await monitored.WaitForExitAsync();

		Assert.Contains("[trace:stderr] boom", console.Output);
		Assert.DoesNotContain("⚠", console.Output);
	}

	[Fact]
	public void SpectreOutputFormatter_WriteError_IncludesErrorCode()
	{
		var (formatter, console) = CreateTestFormatter();

		var error = new ErrorResult
		{
			Code = ErrorCodes.DeviceNotFound,
			Category = "tool",
			Message = "Device not found"
		};

		formatter.WriteError(error);
		var output = console.Output;

		Assert.Contains("E1006", output);
		Assert.Contains("Device not found", output);
	}

	[Fact]
	public void SpectreOutputFormatter_WriteDoctorReport_FormatsCorrectly()
	{
		var (formatter, console) = CreateTestFormatter();

		var report = new DoctorReport
		{
			CorrelationId = "test123",
			Timestamp = DateTime.UtcNow,
			Status = HealthStatus.Healthy,
			Checks = new List<HealthCheck>
			{
				new HealthCheck
				{
					Category = "dotnet",
					Name = ".NET SDK",
					Status = CheckStatus.Ok,
					Message = "8.0.100"
				},
				new HealthCheck
				{
					Category = "android",
					Name = "JDK",
					Status = CheckStatus.Warning,
					Message = "JDK found but outdated"
				}
			},
			Summary = new DoctorSummary { Total = 2, Ok = 1, Warning = 1, Error = 0 }
		};

		formatter.WriteResult(report);
		var output = console.Output;

		Assert.Contains(".NET SDK", output);
		Assert.Contains("JDK", output);
	}

	[Fact]
	public void SpectreOutputFormatter_WriteDeviceList_FormatsTable()
	{
		var (formatter, console) = CreateTestFormatter();

		var result = new DeviceListResult
		{
			Devices = new List<Device>
			{
				new Device
				{
					Name = "Pixel 6",
					Id = "emulator-5554",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true
				}
			}
		};

		formatter.WriteResult(result);
		var output = console.Output;

		Assert.Contains("Pixel 6", output);
		Assert.Contains("emulator-5554", output);
		Assert.Contains("android", output);
	}

	[Fact]
	public void SpectreOutputFormatter_WriteTable_FormatsColumns()
	{
		var (formatter, console) = CreateTestFormatter();

		var items = new[] { ("Apple", "Fruit"), ("Carrot", "Vegetable") };
		formatter.WriteTable(items,
			("Name", i => i.Item1),
			("Category", i => i.Item2));

		var output = console.Output;

		Assert.Contains("Name", output);
		Assert.Contains("Category", output);
		Assert.Contains("Apple", output);
		Assert.Contains("Carrot", output);
	}

	[Theory]
	[InlineData(0.4, "0.4s")]
	[InlineData(5.4, "5.4s")]
	[InlineData(36, "36.0s")]
	[InlineData(62.3, "1:02.3s")]
	public void SpectreOutputFormatter_FormatElapsed_UsesReadableDurations(double seconds, string expected)
	{
		var actual = SpectreOutputFormatter.FormatElapsed(TimeSpan.FromSeconds(seconds));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void SpectreOutputFormatter_FormatTimedStatusMarkup_AppendsElapsedToFirstLine()
	{
		var actual = SpectreOutputFormatter.FormatTimedStatusMarkup(
			"Publishing dotnet-pgo...\n[grey]  Restored packages[/]",
			TimeSpan.FromSeconds(36));

		Assert.StartsWith("Publishing dotnet-pgo... [grey](36.0s)[/]", actual);
		Assert.Contains("\n[grey]  Restored packages[/]", actual);
	}

	[Fact]
	public void SpectreOutputFormatter_FormatCompletedStatusMessage_StripsTrailingEllipsis()
	{
		var actual = SpectreOutputFormatter.FormatCompletedStatusMessage(
			"Building the app...",
			TimeSpan.FromSeconds(36));

		Assert.Equal("Building the app (36.0s)", actual);
	}
}
