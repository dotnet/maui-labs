using System.Text;
using Microsoft.Maui.Cli.Providers.Android;
using Xamarin.Android.Tools;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class MauiAdbRunnerTests
{
	[Fact]
	public void ParseReverseList_ReadsMappingsWithTransportIdentifiers()
	{
		var rules = MauiAdbRunner.ParseReverseList("host-17 tcp:19223 tcp:19223\n");

		var rule = Assert.Single(rules);
		Assert.Equal(AdbProtocol.Tcp, rule.Local.Protocol);
		Assert.Equal(19223, rule.Local.Port);
		Assert.Equal(AdbProtocol.Tcp, rule.Remote.Protocol);
		Assert.Equal(19223, rule.Remote.Port);
	}

	[Fact]
	public void ParseReverseList_PreservesAdbReversePortRoles()
	{
		var rules = MauiAdbRunner.ParseReverseList("host-3 tcp:41113 tcp:42224");

		var rule = Assert.Single(rules);
		Assert.Equal(41113, rule.Remote.Port);
		Assert.Equal(42224, rule.Local.Port);
	}

	[Fact]
	public void ParseReverseList_ReadsEveryMappingLine()
	{
		var rules = MauiAdbRunner.ParseReverseList(
			"host-17 tcp:19223 tcp:19223\r\nhost-17 tcp:5000 tcp:5001\r\n");

		Assert.Equal(2, rules.Count);
		Assert.Contains(rules, rule => rule.Local.Port == 19223 && rule.Remote.Port == 19223);
		Assert.Contains(rules, rule => rule.Remote.Port == 5000 && rule.Local.Port == 5001);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   \r\n")]
	public void ParseReverseList_WithoutOutput_ReturnsNoMappings(string? output)
		=> Assert.Empty(MauiAdbRunner.ParseReverseList(output));

	[Fact]
	public void ParseReverseList_IgnoresRowsThatAreNotPortMappings()
	{
		var rules = MauiAdbRunner.ParseReverseList(
			"cannot connect to daemon\nhost-17 localabstract:foo localabstract:bar\nhost-17 tcp:19223 tcp:19223");

		Assert.Equal(19223, Assert.Single(rules).Local.Port);
	}

	[Fact]
	public async Task ReversePortAsync_CapturesChildStdout()
	{
		using var adb = FakeAdb.Create(exitCode: 1, stdout: "19223");
		var runner = new MauiAdbRunner(adb.Path);
		var spec = new AdbPortSpec(AdbProtocol.Tcp, 19223);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => runner.ReversePortAsync("emulator-5554", spec, spec));

		Assert.Contains("19223", error.Message, StringComparison.Ordinal);
		Assert.Equal("-s emulator-5554 reverse tcp:19223 tcp:19223", adb.ReadArguments());
	}

	[Fact]
	public async Task ForwardPortAsync_CapturesChildStderr()
	{
		using var adb = FakeAdb.Create(exitCode: 1, stderr: "cannot bind listener");
		var runner = new MauiAdbRunner(adb.Path);
		var spec = new AdbPortSpec(AdbProtocol.Tcp, 9223);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => runner.ForwardPortAsync("emulator-5554", spec, spec));

		Assert.Contains("cannot bind listener", error.Message, StringComparison.Ordinal);
		Assert.Equal("-s emulator-5554 forward tcp:9223 tcp:9223", adb.ReadArguments());
	}

	[Fact]
	public async Task PortMappingCommands_OnSuccess_UseExpectedArguments()
	{
		using var adb = FakeAdb.Create(exitCode: 0, stdout: "19223");
		var runner = new MauiAdbRunner(adb.Path);
		var spec = new AdbPortSpec(AdbProtocol.Tcp, 19223);

		await runner.ReversePortAsync("emulator-5554", spec, spec);
		Assert.Equal("-s emulator-5554 reverse tcp:19223 tcp:19223", adb.ReadArguments());

		await runner.RemoveReversePortAsync("emulator-5554", spec);
		Assert.Equal("-s emulator-5554 reverse --remove tcp:19223", adb.ReadArguments());

		await runner.RemoveForwardPortAsync("emulator-5554", spec);
		Assert.Equal("-s emulator-5554 forward --remove tcp:19223", adb.ReadArguments());
	}

	sealed class FakeAdb : IDisposable
	{
		FakeAdb(string directory, string path, string argumentsPath)
		{
			Directory = directory;
			Path = path;
			ArgumentsPath = argumentsPath;
		}

		string Directory { get; }

		public string Path { get; }

		string ArgumentsPath { get; }

		public static FakeAdb Create(int exitCode, string? stdout = null, string? stderr = null)
		{
			var directory = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"maui-adb-tests",
				Guid.NewGuid().ToString("n"));
			System.IO.Directory.CreateDirectory(directory);
			var argumentsPath = System.IO.Path.Combine(directory, "args.txt");

			string path;
			if (OperatingSystem.IsWindows())
			{
				path = System.IO.Path.Combine(directory, "adb.cmd");
				var script = new StringBuilder()
					.AppendLine("@echo off")
					.AppendLine($"echo %*> \"{argumentsPath}\"");
				if (!string.IsNullOrEmpty(stdout))
					script.AppendLine($"echo {stdout}");
				if (!string.IsNullOrEmpty(stderr))
					script.AppendLine($"echo {stderr} 1>&2");
				script.AppendLine($"exit /b {exitCode}");
				File.WriteAllText(path, script.ToString());
			}
			else
			{
				path = System.IO.Path.Combine(directory, "adb");
				var script = new StringBuilder()
					.AppendLine("#!/bin/sh")
					.AppendLine($"echo \"$@\" > \"{argumentsPath}\"");
				if (!string.IsNullOrEmpty(stdout))
					script.AppendLine($"echo \"{stdout}\"");
				if (!string.IsNullOrEmpty(stderr))
					script.AppendLine($"echo \"{stderr}\" 1>&2");
				script.AppendLine($"exit {exitCode}");
				File.WriteAllText(path, script.ToString());
				File.SetUnixFileMode(
					path,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			}

			return new FakeAdb(directory, path, argumentsPath);
		}

		public string ReadArguments()
			=> File.ReadAllText(ArgumentsPath).Trim();

		public void Dispose()
		{
			try
			{
				System.IO.Directory.Delete(Directory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}
}
