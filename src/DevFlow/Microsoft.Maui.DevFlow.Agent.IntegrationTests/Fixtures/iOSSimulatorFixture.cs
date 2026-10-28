using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on an iOS Simulator.
/// </summary>
public sealed class iOSSimulatorFixture : AppFixtureBase
{
    string? _simulatorUdid;
    bool _weBootedSimulator;
    bool _createdSimulator;
    string? _appBundleId;

    public override string Platform => "ios";
    public override string? DeviceId => _simulatorUdid;
    public override string? AppIdentifier => _appBundleId;

    protected override async Task InitializePlatformAsync()
    {
        var (udid, alreadyBooted) = await FindOrBootSimulatorAsync();
        _simulatorUdid = udid;
        _weBootedSimulator = !alreadyBooted;

        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath("ios");
            await BuildSampleAsync(projectPath, "net10.0-ios",
                $"-p:_DeviceTarget=simulator -p:RuntimeIdentifier={GetSimulatorRuntimeIdentifier()}");

            var appBundle = FindSimulatorAppBundle();
            _appBundleId = ReadBundleId(appBundle);

            await InstallAppAsync(appBundle);
            await LaunchAppAsync();
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_simulatorUdid != null && _appBundleId != null)
        {
            try
            {
                await RunProcessAsync("xcrun", $"simctl terminate {_simulatorUdid} {_appBundleId}", timeoutSeconds: 10);
            }
            catch
            {
            }
        }

        if (_weBootedSimulator && _simulatorUdid != null)
        {
            try
            {
                await RunProcessCheckedAsync("xcrun", $"simctl shutdown {_simulatorUdid}", timeoutSeconds: 15);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine($"[iOSFixture] Simulator shutdown failed: {ex.Message}");
            }
        }

        if (_createdSimulator && _simulatorUdid != null)
        {
            await RunProcessCheckedAsync("xcrun", $"simctl delete {_simulatorUdid}", timeoutSeconds: 30);
        }
    }

    public override async Task ResetPermissionAsync(PermissionService permission)
    {
        if (_simulatorUdid == null || _appBundleId == null)
            throw new InvalidOperationException("The iOS Simulator app is not initialized.");

        using var driver = new iOSSimulatorAppDriver
        {
            DeviceUdid = _simulatorUdid,
            BundleId = _appBundleId
        };
        await driver.ResetPermissionAsync(permission);

        try
        {
            await RunProcessAsync(
                "xcrun",
                $"simctl terminate {_simulatorUdid} {_appBundleId}",
                timeoutSeconds: 10);
        }
        catch
        {
        }

        await LaunchAppAsync();
        await WaitForAgentAsync(timeoutSeconds: 60);
    }

    async Task<(string Udid, bool AlreadyBooted)> FindOrBootSimulatorAsync()
    {
        var versionPattern = Environment.GetEnvironmentVariable("DEVFLOW_TEST_IOS_VERSION");
        if (string.IsNullOrWhiteSpace(versionPattern))
            versionPattern = null;

        var isolatedSetting = Environment.GetEnvironmentVariable("DEVFLOW_TEST_IOS_ISOLATED");
        if (!string.IsNullOrEmpty(isolatedSetting) && !bool.TryParse(isolatedSetting, out _))
            throw new InvalidOperationException("DEVFLOW_TEST_IOS_ISOLATED must be 'true' or 'false'.");
        var isolated = string.Equals(isolatedSetting, "true", StringComparison.OrdinalIgnoreCase);
        var json = await RunProcessCheckedAsync("xcrun",
            isolated ? "simctl list --json" : "simctl list devices --json");
        var candidates = isolated
            ? ParseIsolatedIPhoneCandidates(json, versionPattern)
            : ParseIPhoneCandidates(json, versionPattern);

        if (candidates.Count == 0)
            throw new InvalidOperationException(versionPattern != null
                ? $"No iPhone simulators found matching iOS version pattern '{versionPattern}'"
                : "No iPhone simulators found");

        if (isolated)
        {
            var template = SelectBestDevice(candidates);
            var arguments = CreateIsolatedSimulatorArguments(template);
            var created = (await RunProcessCheckedAsync("xcrun", arguments, timeoutSeconds: 60)).Trim();
            if (!Guid.TryParse(created, out var udid))
                throw new InvalidOperationException($"simctl create did not return a simulator UUID: {created}");
            _simulatorUdid = udid.ToString().ToUpperInvariant();
            _createdSimulator = true;
            _weBootedSimulator = true;
            Console.WriteLine($"[iOSFixture] Created {_simulatorUdid} from {template.DeviceTypeIdentifier} ({template.RuntimeIdentifier}).");
            await RunProcessCheckedAsync("xcrun", $"simctl boot {_simulatorUdid}", timeoutSeconds: 60);
            await WaitForBootCompletionAsync(_simulatorUdid);
            return (_simulatorUdid, false);
        }

        var booted = candidates.Where(c => c.State == "Booted").ToList();
        if (booted.Count > 0)
        {
            var best = SelectBestDevice(booted);
            await WaitForBootCompletionAsync(best.Udid);
            return (best.Udid, true);
        }

        var selected = SelectBestDevice(candidates);
        _simulatorUdid = selected.Udid;
        await RunProcessCheckedAsync("xcrun", $"simctl boot {selected.Udid}", timeoutSeconds: 60);
        _weBootedSimulator = true;
        await WaitForBootCompletionAsync(selected.Udid);
        return (selected.Udid, false);
    }

    internal static string CreateIsolatedSimulatorArguments(SimulatorDeviceCandidate template)
    {
        if (string.IsNullOrWhiteSpace(template.DeviceTypeIdentifier)
            || string.IsNullOrWhiteSpace(template.RuntimeIdentifier))
            throw new InvalidOperationException("Creating an isolated simulator requires its device type and runtime identifiers.");

        return $"simctl create DevFlowIntegration-{Environment.ProcessId} {template.DeviceTypeIdentifier} {template.RuntimeIdentifier}";
    }

    internal static IReadOnlyList<SimulatorDeviceCandidate> ParseIsolatedIPhoneCandidates(
        string simctlListJson, string? versionPattern)
    {
        using var document = JsonDocument.Parse(simctlListJson);
        var candidates = new List<SimulatorDeviceCandidate>();
        foreach (var runtime in document.RootElement.GetProperty("runtimes").EnumerateArray())
        {
            var identifier = runtime.GetProperty("identifier").GetString();
            if (identifier == null || !identifier.Contains(".iOS-", StringComparison.OrdinalIgnoreCase)
                || !runtime.GetProperty("isAvailable").GetBoolean())
                continue;
            var version = ExtractOsVersion(identifier);
            if (version == null || (versionPattern != null && !MatchesVersionPattern(version, versionPattern)))
                continue;
            foreach (var device in runtime.GetProperty("supportedDeviceTypes").EnumerateArray())
            {
                var deviceType = device.GetProperty("identifier").GetString();
                var name = device.GetProperty("name").GetString() ?? string.Empty;
                if (IsIPhoneDeviceType(deviceType, name))
                    candidates.Add(new SimulatorDeviceCandidate(string.Empty, name, version, "Shutdown", deviceType, identifier));
            }
        }
        return candidates;
    }

    static async Task WaitForBootCompletionAsync(string udid)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Console.WriteLine($"[iOSFixture] Waiting for simulator {udid} boot completion (limit 300s).");
            await RunProcessCheckedAsync("xcrun", $"simctl bootstatus {udid} -b", timeoutSeconds: 300);
            Console.WriteLine($"[iOSFixture] Simulator {udid} boot completed in {timer.Elapsed.TotalSeconds:F1}s.");
        }
        catch (TimeoutException)
        {
            try
            {
                var (stdout, stderr, exitCode) = await RunProcessAsync("/usr/bin/log",
                    $"show --last 3m --style compact --predicate \"process == 'CoreSimulatorService' AND eventMessage CONTAINS '{udid}'\"",
                    timeoutSeconds: 15);
                Console.Error.WriteLine($"[iOSFixture] Boot diagnostics for {udid} (exit {exitCode}):\n{stdout}\n{stderr}");
            }
            catch (TimeoutException ex)
            {
                Console.Error.WriteLine($"[iOSFixture] Boot log collection also timed out: {ex.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// Parses <c>xcrun simctl list devices --json</c> output into the set of available iPhone
    /// simulator candidates, optionally narrowed to an iOS version pattern.
    /// </summary>
    /// <remarks>
    /// Pulled out of <see cref="FindOrBootSimulatorAsync"/> so it can be unit tested against
    /// synthetic JSON without a simulator, an Xcode install, or a device — a booted simulator
    /// whose user-visible name has been customized (e.g. renamed to "GDUI-Test" in Simulator.app
    /// or via <c>simctl rename</c>) is otherwise silently excluded and the fixture picks a
    /// different, unintended device instead of failing loudly.
    /// </remarks>
    internal static IReadOnlyList<SimulatorDeviceCandidate> ParseIPhoneCandidates(
        string simctlListDevicesJson,
        string? versionPattern)
    {
        using var doc = JsonDocument.Parse(simctlListDevicesJson);
        var devicesRoot = doc.RootElement.GetProperty("devices");

        var candidates = new List<SimulatorDeviceCandidate>();

        foreach (var runtime in devicesRoot.EnumerateObject())
        {
            if (!runtime.Name.Contains("iOS", StringComparison.OrdinalIgnoreCase))
                continue;

            var osVersion = ExtractOsVersion(runtime.Name);
            if (osVersion == null)
                continue;

            if (versionPattern != null && !MatchesVersionPattern(osVersion, versionPattern))
                continue;

            foreach (var device in runtime.Value.EnumerateArray())
            {
                var name = device.GetProperty("name").GetString() ?? string.Empty;
                var udid = device.GetProperty("udid").GetString() ?? string.Empty;
                var state = device.GetProperty("state").GetString() ?? string.Empty;
                var isAvailable = !device.TryGetProperty("isAvailable", out var available) || available.GetBoolean();
                var deviceTypeIdentifier = device.TryGetProperty("deviceTypeIdentifier", out var deviceType)
                    ? deviceType.GetString()
                    : null;

                if (!isAvailable || !IsIPhoneDeviceType(deviceTypeIdentifier, name))
                    continue;

                candidates.Add(new SimulatorDeviceCandidate(udid, name, osVersion, state, deviceTypeIdentifier, runtime.Name));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Decides whether a simulator device is an iPhone.
    /// </summary>
    /// <remarks>
    /// Prefers <c>deviceTypeIdentifier</c> (e.g.
    /// <c>com.apple.CoreSimulator.SimDeviceType.iPhone-15-Pro</c>), which simctl derives from the
    /// device's actual hardware model and does not change when a user renames the device. Checking
    /// <c>name.Contains("iPhone")</c> alone — the previous behavior — excludes any iPhone whose
    /// user-visible name was customized (e.g. "GDUI-Test"), even though it is unambiguously an
    /// iPhone. Falls back to the name check only when <c>deviceTypeIdentifier</c> is absent, which
    /// covers older simctl JSON that never emitted the field.
    /// </remarks>
    internal static bool IsIPhoneDeviceType(string? deviceTypeIdentifier, string name)
    {
        if (!string.IsNullOrEmpty(deviceTypeIdentifier))
        {
            var lastDot = deviceTypeIdentifier.LastIndexOf('.');
            var typeName = lastDot >= 0 ? deviceTypeIdentifier[(lastDot + 1)..] : deviceTypeIdentifier;
            return typeName.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase);
        }

        return name.Contains("iPhone", StringComparison.OrdinalIgnoreCase);
    }

    internal static SimulatorDeviceCandidate SelectBestDevice(IReadOnlyList<SimulatorDeviceCandidate> devices) =>
        devices
            .OrderByDescending(ExtractIPhoneModelNumber)
            .ThenByDescending(d => d.Runtime)
            .First();

    static int ExtractIPhoneModelNumber(SimulatorDeviceCandidate device)
    {
        var match = Regex.Match(
            device.DeviceTypeIdentifier ?? string.Empty,
            @"(?:^|\.)iPhone-(\d+)",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return int.Parse(match.Groups[1].Value);

        match = Regex.Match(device.Name, @"iPhone\s+(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    static string? ExtractOsVersion(string runtimeId)
    {
        var match = Regex.Match(runtimeId, @"iOS[- ](\d+)[- ](\d+)");
        if (match.Success)
            return $"{match.Groups[1].Value}.{match.Groups[2].Value}";

        match = Regex.Match(runtimeId, @"iOS[- ](\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    static bool MatchesVersionPattern(string version, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern).Replace("x", @"\d+") + "$";
        return Regex.IsMatch(version, regexPattern, RegexOptions.IgnoreCase);
    }

    static string GetSimulatorRuntimeIdentifier() =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "iossimulator-arm64" : "iossimulator-x64";

    static string FindSimulatorAppBundle()
    {
        var binDir = Path.Combine(GetSampleBuildOutputRoot("ios"), "net10.0-ios", GetSimulatorRuntimeIdentifier());

        if (!Directory.Exists(binDir))
            throw new InvalidOperationException($"iOS simulator build output not found at: {binDir}");

        var appBundles = Directory.GetDirectories(binDir, "*.app", SearchOption.AllDirectories);
        if (appBundles.Length == 0)
            throw new InvalidOperationException($"No .app bundle found under {binDir}");

        return appBundles[0];
    }

    static string ReadBundleId(string appBundlePath)
    {
        var plistPath = Path.Combine(appBundlePath, "Info.plist");
        if (!File.Exists(plistPath))
            throw new InvalidOperationException($"Info.plist not found at: {plistPath}");

        var result = RunProcessAsync("/usr/libexec/PlistBuddy",
            $"-c \"Print :CFBundleIdentifier\" \"{plistPath}\"").Result;

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to read bundle ID from {plistPath}");

        return result.Stdout.Trim();
    }

    Task InstallAppAsync(string appBundlePath) =>
        RunProcessCheckedAsync("xcrun", $"simctl install {_simulatorUdid} \"{appBundlePath}\"", timeoutSeconds: 180);

    async Task LaunchAppAsync()
    {
        var envVars = new Dictionary<string, string>
        {
            ["SIMCTL_CHILD_DEVFLOW_TEST_PORT"] = AgentPort.ToString()
        };

        try
        {
            await RunProcessCheckedAsync("xcrun",
                $"simctl launch {_simulatorUdid} {_appBundleId}",
                envVars: envVars,
                timeoutSeconds: 90);
        }
        catch (TimeoutException)
        {
            try
            {
                var (stdout, stderr, exitCode) = await RunProcessAsync("xcrun",
                    $"simctl spawn {_simulatorUdid} log show --last 3m --style compact --predicate \"eventMessage CONTAINS '{_appBundleId}'\"",
                    timeoutSeconds: 15);
                Console.Error.WriteLine($"[iOSFixture] Launch diagnostics (exit {exitCode}):\n{stdout}\n{stderr}");
            }
            catch (TimeoutException ex)
            {
                Console.Error.WriteLine($"[iOSFixture] Simulator log collection also timed out: {ex.Message}");
            }
            throw;
        }
    }
}

/// <summary>
/// One iPhone simulator entry parsed out of <c>xcrun simctl list devices --json</c>.
/// </summary>
internal readonly record struct SimulatorDeviceCandidate(
    string Udid,
    string Name,
    string Runtime,
    string State,
    string? DeviceTypeIdentifier,
    string? RuntimeIdentifier = null);
