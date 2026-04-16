using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on an iOS Simulator.
/// </summary>
public sealed class iOSSimulatorFixture : AppFixtureBase
{
    string? _simulatorUdid;
    bool _weBootedSimulator;
    string? _appBundleId;

    public override string Platform => "ios";

    protected override async Task InitializePlatformAsync()
    {
        var (udid, alreadyBooted) = await FindOrBootSimulatorAsync();
        _simulatorUdid = udid;
        _weBootedSimulator = !alreadyBooted;

        await WithBuildLockAsync(async () =>
        {
            var projectPath = GetSampleProjectPath();
            await BuildSampleAsync(projectPath, "net10.0-ios",
                "-p:_DeviceTarget=simulator -p:RuntimeIdentifier=iossimulator-arm64");

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
                await RunProcessAsync("xcrun", $"simctl shutdown {_simulatorUdid}", timeoutSeconds: 15);
            }
            catch
            {
            }
        }
    }

    async Task<(string Udid, bool AlreadyBooted)> FindOrBootSimulatorAsync()
    {
        var versionPattern = Environment.GetEnvironmentVariable("DEVFLOW_TEST_IOS_VERSION");

        var json = await RunProcessCheckedAsync("xcrun", "simctl list devices --json");
        var doc = JsonDocument.Parse(json);
        var devicesRoot = doc.RootElement.GetProperty("devices");

        var candidates = new List<(string Udid, string Name, string Runtime, string State)>();

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

                if (!isAvailable || !name.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
                    continue;

                candidates.Add((udid, name, osVersion, state));
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(versionPattern != null
                ? $"No iPhone simulators found matching iOS version pattern '{versionPattern}'"
                : "No iPhone simulators found");

        var booted = candidates.Where(c => c.State == "Booted").ToList();
        if (booted.Count > 0)
        {
            var best = SelectBestDevice(booted);
            return (best.Udid, true);
        }

        var selected = SelectBestDevice(candidates);
        await RunProcessCheckedAsync("xcrun", $"simctl boot {selected.Udid}", timeoutSeconds: 60);
        return (selected.Udid, false);
    }

    static (string Udid, string Name, string Runtime, string State) SelectBestDevice(
        List<(string Udid, string Name, string Runtime, string State)> devices) =>
        devices
            .OrderByDescending(d => ExtractIPhoneModelNumber(d.Name))
            .ThenByDescending(d => d.Runtime)
            .First();

    static int ExtractIPhoneModelNumber(string name)
    {
        var match = Regex.Match(name, @"iPhone\s+(\d+)");
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

    static string FindSimulatorAppBundle()
    {
        var binDir = Path.Combine(GetSampleBuildOutputRoot(), "net10.0-ios", "iossimulator-arm64");

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
        RunProcessCheckedAsync("xcrun", $"simctl install {_simulatorUdid} \"{appBundlePath}\"", timeoutSeconds: 60);

    Task LaunchAppAsync()
    {
        var envVars = new Dictionary<string, string>
        {
            ["SIMCTL_CHILD_DEVFLOW_TEST_PORT"] = AgentPort.ToString()
        };

        return RunProcessCheckedAsync("xcrun",
            $"simctl launch {_simulatorUdid} {_appBundleId}",
            envVars: envVars,
            timeoutSeconds: 30);
    }
}
