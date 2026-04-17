using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on an Android emulator.
/// </summary>
public sealed class AndroidEmulatorFixture : AppFixtureBase
{
    const string PackageId = "com.companyname.mauitodo";

    Process? _emulatorProcess;
    bool _weStartedEmulator;
    string? _serialNumber;
    int _apiLevel;
    string _sdkRoot = null!;

    public override string Platform => "android";

    protected override async Task InitializePlatformAsync()
    {
        _sdkRoot = ResolveSdkRoot();
        _apiLevel = GetTargetApiLevel();
        var avdName = GetTargetAvdName(_apiLevel);

        await EnsureAvdExistsAsync(avdName, _apiLevel);
        _serialNumber = await EnsureEmulatorRunningAsync(avdName);

        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath();
            await BuildSampleAsync(projectPath, "net10.0-android",
                $"-p:EmbedAssembliesIntoApk=true -p:MauiDevFlowPort={AgentPort}");

            var apkPath = FindApk();
            await InstallApkAsync(apkPath);

            await AdbCheckedAsync($"forward tcp:{AgentPort} tcp:{AgentPort}", timeoutSeconds: 15);
            await LaunchAppAsync();
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_serialNumber != null)
        {
            try { await AdbAsync($"shell am force-stop {PackageId}", timeoutSeconds: 5); } catch { }
            try { await RunProcessAsync(AdbPath(), $"-s {_serialNumber} forward --remove tcp:{AgentPort}", timeoutSeconds: 5); } catch { }
        }

        if (_weStartedEmulator && _emulatorProcess is { HasExited: false })
        {
            try { await AdbAsync("emu kill", timeoutSeconds: 10); } catch { }

            try
            {
                _emulatorProcess.Kill(entireProcessTree: true);
                await _emulatorProcess.WaitForExitAsync(new CancellationTokenSource(10000).Token);
            }
            catch
            {
            }
        }

        _emulatorProcess?.Dispose();
    }

    static string ResolveSdkRoot()
    {
        var root = Environment.GetEnvironmentVariable("ANDROID_HOME")
            ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(home, "Library", "Android", "sdk"),
                Path.Combine(home, "Android", "Sdk"),
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Android\Sdk",
            };

            root = candidates.FirstOrDefault(Directory.Exists)
                ?? throw new InvalidOperationException(
                    "Android SDK not found. Set ANDROID_HOME or ANDROID_SDK_ROOT.");
        }

        if (!Directory.Exists(root))
            throw new InvalidOperationException($"Android SDK directory not found at: {root}");

        return root;
    }

    string AdbPath() => Path.Combine(_sdkRoot, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
    string EmulatorPath() => Path.Combine(_sdkRoot, "emulator", OperatingSystem.IsWindows() ? "emulator.exe" : "emulator");

    string AvdManagerPath()
    {
        var cmdlineToolsDir = Path.Combine(_sdkRoot, "cmdline-tools");
        if (!Directory.Exists(cmdlineToolsDir))
            throw new InvalidOperationException($"cmdline-tools not found at: {cmdlineToolsDir}");

        var latestVersion = Directory.GetDirectories(cmdlineToolsDir)
            .Select(Path.GetFileName)
            .Where(n => n != "latest")
            .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "latest";

        return Path.Combine(cmdlineToolsDir, latestVersion, "bin", OperatingSystem.IsWindows() ? "avdmanager.bat" : "avdmanager");
    }

    string SdkManagerPath()
    {
        var cmdlineToolsDir = Path.Combine(_sdkRoot, "cmdline-tools");
        var latestVersion = Directory.GetDirectories(cmdlineToolsDir)
            .Select(Path.GetFileName)
            .Where(n => n != "latest")
            .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "latest";

        return Path.Combine(cmdlineToolsDir, latestVersion, "bin", OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager");
    }

    Task<(string Stdout, string Stderr, int ExitCode)> AdbAsync(string arguments, int timeoutSeconds = 30) =>
        RunProcessAsync(AdbPath(), $"-s {_serialNumber} {arguments}", timeoutSeconds: timeoutSeconds);

    Task<string> AdbCheckedAsync(string arguments, int timeoutSeconds = 30) =>
        RunProcessCheckedAsync(AdbPath(), $"-s {_serialNumber} {arguments}", timeoutSeconds: timeoutSeconds);

    static int GetTargetApiLevel()
    {
        var apiStr = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_API") ?? "35";
        var first = apiStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return int.Parse(first);
    }

    static string GetTargetAvdName(int apiLevel) =>
        Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_AVD")
        ?? $"devflow-tests-api{apiLevel}";

    async Task EnsureAvdExistsAsync(string avdName, int apiLevel)
    {
        var (stdout, _, _) = await RunProcessAsync(AvdManagerPath(), "list avd -c");
        var existingAvds = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (existingAvds.Any(a => a.Equals(avdName, StringComparison.OrdinalIgnoreCase)))
            return;

        var systemImage = $"system-images;android-{apiLevel};google_apis;arm64-v8a";
        await RunProcessCheckedAsync(SdkManagerPath(), $"--install \"{systemImage}\"", timeoutSeconds: 600);

        var psi = new ProcessStartInfo(AvdManagerPath(), $"create avd -n {avdName} -k \"{systemImage}\" -d pixel_6 --force")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start avdmanager");

        await process.StandardInput.WriteLineAsync("no");
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"avdmanager create failed: {stderr}");
        }
    }

    async Task<string> EnsureEmulatorRunningAsync(string avdName)
    {
        var adb = AdbPath();
        var requestedSerial = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_SERIAL");

        if (!string.IsNullOrWhiteSpace(requestedSerial))
        {
            var (avdOutput, _, exitCode) = await RunProcessAsync(adb,
                $"-s {requestedSerial} emu avd name", timeoutSeconds: 10);

            if (exitCode == 0 && avdOutput.Trim().StartsWith(avdName, StringComparison.OrdinalIgnoreCase))
            {
                _weStartedEmulator = false;
                return requestedSerial;
            }

            throw new InvalidOperationException(
                $"Requested Android serial '{requestedSerial}' is not running AVD '{avdName}'.");
        }

        var (devicesOutput, _, _) = await RunProcessAsync(adb, "devices");
        var runningEmulators = devicesOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("emulator-") && l.Contains("device"))
            .Select(l => l.Split('\t')[0].Trim())
            .ToList();

        foreach (var serial in runningEmulators)
        {
            var (avdOutput, _, exitCode) = await RunProcessAsync(adb, $"-s {serial} emu avd name", timeoutSeconds: 5);
            if (exitCode == 0 && avdOutput.Trim().StartsWith(avdName, StringComparison.OrdinalIgnoreCase))
            {
                _weStartedEmulator = false;
                return serial;
            }
        }

        var emulatorPort = GetEmulatorConsolePort();
        var expectedSerial = $"emulator-{emulatorPort}";

        var psi = new ProcessStartInfo(EmulatorPath(), $"-avd {avdName} -port {emulatorPort} -no-snapshot -no-audio -no-window -gpu swiftshader_indirect")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _emulatorProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start emulator for AVD {avdName}");
        _weStartedEmulator = true;

        var newSerial = await WaitForEmulatorSerialAsync(adb, expectedSerial, avdName, timeoutSeconds: 120);
        await WaitForDeviceBootAsync(adb, newSerial, timeoutSeconds: 180);
        return newSerial;
    }

    static int GetEmulatorConsolePort()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_EMULATOR_PORT"), out var configuredPort))
            return configuredPort;

        // Android emulator console ports must be even and reserve the next odd port too.
        for (var port = 5580; port <= 5680; port += 2)
        {
            if (IsPortAvailable(port) && IsPortAvailable(port + 1))
                return port;
        }

        throw new InvalidOperationException("Could not find a free Android emulator port in the 5580-5680 range.");
    }

    static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    async Task<string> WaitForEmulatorSerialAsync(string adb, string expectedSerial, string avdName, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (_emulatorProcess is { HasExited: true })
                throw new InvalidOperationException(
                    $"Emulator process exited with code {_emulatorProcess.ExitCode} before becoming ready.");

            var (output, _, _) = await RunProcessAsync(adb, "devices");
            var emulatorSerials = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.StartsWith("emulator-") && l.Contains("device"))
                .Select(l => l.Split('\t')[0].Trim())
                .ToList();

            if (emulatorSerials.Contains(expectedSerial, StringComparer.OrdinalIgnoreCase))
                return expectedSerial;

            foreach (var serial in emulatorSerials)
            {
                var (avdOutput, _, exitCode) = await RunProcessAsync(adb, $"-s {serial} emu avd name", timeoutSeconds: 5);
                if (exitCode == 0 && avdOutput.Trim().StartsWith(avdName, StringComparison.OrdinalIgnoreCase))
                    return serial;
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException(
            $"Emulator for AVD '{avdName}' did not appear in 'adb devices' within {timeoutSeconds}s.");
    }

    static async Task WaitForDeviceBootAsync(string adb, string serial, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var (output, _, exitCode) = await RunProcessAsync(adb, $"-s {serial} shell getprop sys.boot_completed", timeoutSeconds: 5);
            if (exitCode == 0 && output.Trim() == "1")
                return;

            await Task.Delay(3000);
        }

        throw new TimeoutException($"Device {serial} did not finish booting within {timeoutSeconds}s.");
    }

    static string FindApk()
    {
        var binDir = Path.Combine(GetSampleBuildOutputRoot(), "net10.0-android");

        if (!Directory.Exists(binDir))
            throw new InvalidOperationException($"Android build output not found at: {binDir}");

        var apks = Directory.GetFiles(binDir, "*-Signed.apk", SearchOption.AllDirectories);
        if (apks.Length == 0)
            apks = Directory.GetFiles(binDir, "*.apk", SearchOption.AllDirectories);

        if (apks.Length == 0)
            throw new InvalidOperationException($"No APK found under {binDir}");

        return apks[0];
    }

    Task InstallApkAsync(string apkPath) =>
        AdbCheckedAsync($"install -r \"{apkPath}\"", timeoutSeconds: 120);

    async Task LaunchAppAsync()
    {
        var output = await AdbCheckedAsync(
            $"shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {PackageId}",
            timeoutSeconds: 10);

        var activityLine = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.Contains('/'));

        if (string.IsNullOrEmpty(activityLine))
            throw new InvalidOperationException(
                $"Could not resolve launcher activity for {PackageId}. Output: {output}");

        await AdbCheckedAsync($"shell am force-stop {PackageId}", timeoutSeconds: 10);
        var launchOutput = await AdbCheckedAsync($"shell am start -W -n {activityLine}", timeoutSeconds: 30);

        if (launchOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Failed to launch {PackageId}: {launchOutput}");

        await WaitForAppProcessAsync(timeoutSeconds: 30);
    }

    async Task WaitForAppProcessAsync(int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var (output, _, exitCode) = await AdbAsync($"shell pidof {PackageId}", timeoutSeconds: 5);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return;

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Android app process '{PackageId}' did not appear within {timeoutSeconds}s.");
    }
}
