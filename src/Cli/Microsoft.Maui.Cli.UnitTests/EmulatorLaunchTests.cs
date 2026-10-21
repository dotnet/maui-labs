// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class EmulatorLaunchTests
{
	[UnixTheory]
	[InlineData(false, false, false)]
	[InlineData(true, false, false)]
	[InlineData(false, false, true)]
	[InlineData(false, true, true)]
	[InlineData(true, false, true)]
	[InlineData(true, true, true)]
	public async Task StartAvdAsync_ForwardsActualEmulatorArguments(bool wait, bool coldBoot, bool noWindow)
	{
		using var sdk = new ScriptedAndroidSdk();
		var adb = new Adb(() => sdk.Path);
		var manager = new AvdManager(() => sdk.Path, () => null, adb);
		using var provider = new AndroidProvider(new FakeJdkManager(), adb: adb, avdManager: manager);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		await provider.StartAvdAsync("TestEmulator", coldBoot, wait, timeout.Token, noWindow);

		var expected = new List<string> { "-avd", "TestEmulator" };
		if (coldBoot)
			expected.Add("-no-snapshot-load");
		if (noWindow)
			expected.AddRange(["-no-window", "-no-audio", "-no-boot-anim", "-gpu", "swiftshader_indirect"]);
		Assert.Equal(expected, File.ReadAllLines(sdk.ArgumentsPath));
		if (wait)
			Assert.Contains("shell pm path android", File.ReadAllText(sdk.AdbCallsPath), StringComparison.Ordinal);
		else
			Assert.DoesNotContain("shell pm path android", File.ReadAllText(sdk.AdbCallsPath), StringComparison.Ordinal);
	}

	sealed class UnixTheoryAttribute : TheoryAttribute
	{
		public UnixTheoryAttribute()
		{
			if (OperatingSystem.IsWindows())
				Skip = "Uses Unix executables in a synthetic Android SDK; command forwarding is tested on all platforms.";
		}
	}

	sealed class ScriptedAndroidSdk : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(
			Directory.GetCurrentDirectory(), "artifacts", "emulator-launch-tests", Guid.NewGuid().ToString("N"));

		public string ArgumentsPath => System.IO.Path.Combine(Path, "emulator", "arguments.txt");
		public string AdbCallsPath => System.IO.Path.Combine(Path, "platform-tools", "calls.txt");
		string PidPath => System.IO.Path.Combine(Path, "emulator", "pid.txt");

		public ScriptedAndroidSdk()
		{
			// Only replace the external executables: the provider, AVD manager and
			// upstream launch/boot orchestration all run unchanged.
			WriteExecutable("emulator", "emulator", """
				#!/bin/sh
				cd "$(dirname "$0")"
				echo $$ > pid.txt
				printf '%s\n' "$@" > arguments.txt
				exec sleep 30
				""");
			WriteExecutable("platform-tools", "adb", """
				#!/bin/sh
				cd "$(dirname "$0")"
				echo "$*" >> calls.txt
				case "$*" in
				  "devices -l")
				    echo "List of devices attached"
				    if [ -f ../emulator/arguments.txt ]; then
				      printf 'emulator-5554\tdevice product:sdk model:TestEmulator device:generic\n'
				    fi
				    ;;
				  *"getprop ro.boot.qemu.avd_name") echo TestEmulator ;;
				  *"getprop sys.boot_completed") echo 1 ;;
				  *"shell pm path android") echo package:/system/framework/framework-res.apk ;;
				esac
				""");
		}

		void WriteExecutable(string folder, string name, string script)
		{
			var directory = System.IO.Path.Combine(Path, folder);
			Directory.CreateDirectory(directory);
			var executable = System.IO.Path.Combine(directory, name);
			File.WriteAllText(executable, script + "\n");
			if (!OperatingSystem.IsWindows())
				File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		public void Dispose()
		{
			if (File.Exists(PidPath) && int.TryParse(File.ReadAllText(PidPath).Trim(), out var pid))
			{
				try
				{
					using var process = Process.GetProcessById(pid);
					process.Kill();
					process.WaitForExit(5000);
				}
				catch (ArgumentException) { }
				catch (InvalidOperationException) { }
			}
			Directory.Delete(Path, recursive: true);
		}
	}
}
