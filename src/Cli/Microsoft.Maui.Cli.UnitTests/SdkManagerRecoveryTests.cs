// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class SdkManagerRecoveryTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(7)]
	public async Task InstallCommand_BareLayout_BootstrapsAndRunsInstalledTool(int toolExitCode)
	{
		var sdkPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		var toolName = OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager";
		var barePath = Path.Combine(sdkPath, "cmdline-tools", "bin", toolName);
		var installedPath = Path.Combine(sdkPath, "cmdline-tools", "16.0", "bin", toolName);
		var originalServices = Program.Services;
		var originalOut = Console.Out;
		var originalError = Console.Error;
		using var standardOutput = new StringWriter();
		using var standardError = new StringWriter();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		try
		{
			Console.SetOut(standardOutput);
			Console.SetError(standardError);
			Directory.CreateDirectory(Path.GetDirectoryName(barePath)!);
			File.WriteAllText(barePath, "unsupported layout");
			Directory.CreateDirectory(Path.Combine(sdkPath, "licenses"));
			File.WriteAllText(Path.Combine(sdkPath, "licenses", "android-sdk-license"), "test license");

			// Replace only the external SDK executable; exercise real download, extraction,
			// discovery, command orchestration and process execution without Java or Google's feed.
			var script = OperatingSystem.IsWindows()
				? $"@echo off\r\necho %~1>\"%~dp0invocation.txt\"\r\nexit /b {toolExitCode}\r\n"
				: $"#!/bin/sh\nprintf '%s\\n' \"$1\" > \"$(dirname \"$0\")/invocation.txt\"\nexit {toolExitCode}\n";
			using var archiveStream = new MemoryStream();
			using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
			{
				using var writer = new StreamWriter(
					archive.CreateEntry($"cmdline-tools/bin/{toolName}").Open(), new UTF8Encoding(false));
				writer.Write(script);
			}
			var archiveBytes = archiveStream.ToArray();
			var checksum = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();

			var builder = WebApplication.CreateSlimBuilder();
			builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
			builder.Logging.ClearProviders();
			await using var server = builder.Build();
			var manifestRequests = 0;
			var archiveRequests = 0;
			server.MapGet("/manifest", () =>
			{
				Interlocked.Increment(ref manifestRequests);
				return Results.Text($"""
					<manifest>
					  <cmdline-tools revision="16.0">
					    <urls>
					      <url size="{archiveBytes.Length}" checksum-type="sha-256" checksum="{checksum}">{server.Urls.Single()}/tools.zip</url>
					    </urls>
					  </cmdline-tools>
					</manifest>
					""", "application/xml");
			});
			server.MapGet("/tools.zip", () =>
			{
				Interlocked.Increment(ref archiveRequests);
				return Results.Bytes(archiveBytes, "application/zip");
			});
			await server.StartAsync(timeout.Token);

			using var upstream = new Xamarin.Android.Tools.SdkManager
			{
				ManifestFeedUrl = $"{server.Urls.Single()}/manifest"
			};
			using var sdkManager = new SdkManager(() => sdkPath, () => sdkPath, upstream);
			var jdkManager = new FakeJdkManager { DetectedJdkPath = sdkPath };
			using var provider = new AndroidProvider(jdkManager, sdkManager);
			provider.OverrideSdkPath(sdkPath);
			var services = ServiceConfiguration.CreateTestServiceProvider(
				androidProvider: provider, jdkManager: jdkManager);
			using var serviceLifetime = services as IDisposable;
			Program.Services = services;

			var command = Program.BuildRootCommand();
			var result = command.Parse([
				"android", "install", "--json", "--ci",
				"--sdk-install-path", sdkPath, "--packages", "platform-tools"
			]);
			Assert.Empty(result.Errors);
			var exitCode = await result.InvokeAsync(cancellationToken: timeout.Token);

			var expectedExitCode = toolExitCode == 0 ? 0 : 1;
			Assert.True(expectedExitCode == exitCode,
				$"Expected exit code {expectedExitCode}, but received {exitCode}.{Environment.NewLine}" +
				$"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
				$"stderr:{Environment.NewLine}{standardError}");
			Assert.Equal(1, manifestRequests);
			Assert.Equal(1, archiveRequests);
			Assert.Equal(installedPath, sdkManager.SdkManagerPath);
			Assert.True(sdkManager.IsAvailable);
			Assert.Equal(script, File.ReadAllText(installedPath));
			Assert.Equal("platform-tools", File.ReadAllText(
				Path.Combine(Path.GetDirectoryName(installedPath)!, "invocation.txt")).Trim());
			Assert.Equal("unsupported layout", File.ReadAllText(barePath));
		}
		finally
		{
			Console.SetOut(originalOut);
			Console.SetError(originalError);
			Program.Services = originalServices;
			if (Directory.Exists(sdkPath))
				Directory.Delete(sdkPath, recursive: true);
		}
	}
}
