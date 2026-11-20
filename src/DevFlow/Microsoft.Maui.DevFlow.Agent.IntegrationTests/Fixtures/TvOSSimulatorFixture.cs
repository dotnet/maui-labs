using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the tvOS DevFlow sample on an Apple TV simulator.
/// </summary>
/// <remarks>
/// MAUI has no tvOS backend of its own; the sample runs on the community Platform.Maui.TvOS
/// backend. Set <c>DEVFLOW_TEST_TVOS_BACKEND_PROJECT</c> to a mauiplatforms checkout's
/// <c>src/Platform.Maui.TvOS/Platform.Maui.TvOS.csproj</c> to build against it instead of the
/// nuget.org package (needed on tvOS 27, see the sample's project file).
/// </remarks>
public sealed class TvOSSimulatorFixture : AppFixtureBase
{
    const string TargetFramework = "net10.0-tvos";

    string? _simulatorUdid;
    bool _weBootedSimulator;
    string? _appBundleId;

    public override string Platform => "tvos";
    public override string? DeviceId => _simulatorUdid;
    public override string? AppIdentifier => _appBundleId;

    protected override async Task InitializePlatformAsync()
    {
        if (TestFramework.IsNative)
        {
            throw new InvalidOperationException(
                "DEVFLOW_TEST_PLATFORM=tvos only supports DEVFLOW_TEST_FRAMEWORK=maui.");
        }

        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The tvOS integration fixture requires macOS.");

        var (udid, alreadyBooted) = await FindOrBootSimulatorAsync();
        _simulatorUdid = udid;
        _weBootedSimulator = !alreadyBooted;

        await WithBuildLockAsync(async () =>
        {
            await BuildSampleAsync(
                GetSampleProjectPath(Platform),
                TargetFramework,
                CreateBuildArguments(
                    GetSimulatorRuntimeIdentifier(),
                    Environment.GetEnvironmentVariable("DEVFLOW_TEST_TVOS_BACKEND_PROJECT"),
                    Environment.GetEnvironmentVariable("DEVFLOW_TEST_TVOS_BACKEND_SOURCE")));

            var appBundle = FindSimulatorAppBundle();
            _appBundleId = await ReadBundleIdAsync(appBundle);

            await RunProcessCheckedAsync("xcrun", $"simctl install {_simulatorUdid} \"{appBundle}\"", timeoutSeconds: 180);
            await RunProcessCheckedAsync(
                "xcrun",
                $"simctl launch {_simulatorUdid} {_appBundleId}",
                envVars: new Dictionary<string, string>
                {
                    ["SIMCTL_CHILD_DEVFLOW_TEST_PORT"] = AgentPort.ToString()
                },
                timeoutSeconds: 90);
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
                Console.Error.WriteLine($"[tvOSFixture] Simulator shutdown failed: {ex.Message}");
            }
        }
    }

    internal static string CreateBuildArguments(string runtimeIdentifier, string? backendProject, string? backendSource = null)
    {
        var arguments = $"-p:RuntimeIdentifier={runtimeIdentifier}";
        if (string.IsNullOrWhiteSpace(backendProject))
        {
            // The backend package is not on the repo's feeds; the caller names a source that has it.
            return string.IsNullOrWhiteSpace(backendSource)
                ? arguments
                : arguments + $" \"-p:RestoreAdditionalProjectSources={backendSource}\"";
        }

        // The backend checkout defaults to its own (newer) .NET; build it for .NET 10 with the
        // matching MAUI. These must be global properties: restore ignores per-reference ones.
        return arguments
            + $" \"-p:PlatformMauiTvOSProject={Path.GetFullPath(backendProject)}\""
            + " -p:BaseTargetFramework=net10.0 -p:MauiVersion=10.0.120";
    }

    async Task<(string Udid, bool AlreadyBooted)> FindOrBootSimulatorAsync()
    {
        var json = await RunProcessCheckedAsync("xcrun", "simctl list devices --json");
        var candidates = ParseAppleTVCandidates(json);
        if (candidates.Count == 0)
            throw new InvalidOperationException("No Apple TV simulators found. Install a tvOS simulator runtime in Xcode.");

        var booted = candidates.Where(c => c.State == "Booted").ToList();
        var selected = SelectBestDevice(booted.Count > 0 ? booted : candidates);
        if (booted.Count == 0)
            await RunProcessCheckedAsync("xcrun", $"simctl boot {selected.Udid}", timeoutSeconds: 60);

        await RunProcessCheckedAsync("xcrun", $"simctl bootstatus {selected.Udid} -b", timeoutSeconds: 300);
        return (selected.Udid, booted.Count > 0);
    }

    /// <summary>
    /// Parses <c>xcrun simctl list devices --json</c> output into the available Apple TV simulators.
    /// </summary>
    internal static IReadOnlyList<SimulatorDeviceCandidate> ParseAppleTVCandidates(string simctlListDevicesJson)
    {
        using var document = JsonDocument.Parse(simctlListDevicesJson);
        var candidates = new List<SimulatorDeviceCandidate>();

        foreach (var runtime in document.RootElement.GetProperty("devices").EnumerateObject()
            .Where(runtime => runtime.Name.Contains("tvOS", StringComparison.OrdinalIgnoreCase)))
        {
            var osVersion = ExtractTvOSVersion(runtime.Name);
            var devices = runtime.Value.EnumerateArray()
                .Where(device => !device.TryGetProperty("isAvailable", out var available) || available.GetBoolean());
            foreach (var device in devices)
            {
                var deviceType = device.TryGetProperty("deviceTypeIdentifier", out var type) ? type.GetString() : null;
                candidates.Add(new SimulatorDeviceCandidate(
                    device.GetProperty("udid").GetString() ?? string.Empty,
                    device.GetProperty("name").GetString() ?? string.Empty,
                    osVersion ?? string.Empty,
                    device.GetProperty("state").GetString() ?? string.Empty,
                    deviceType,
                    runtime.Name));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Picks the newest runtime, then the 4K models over the HD ones.
    /// </summary>
    internal static SimulatorDeviceCandidate SelectBestDevice(IReadOnlyList<SimulatorDeviceCandidate> devices)
        => devices
            .OrderByDescending(device => Version.TryParse(device.Runtime, out var version) ? version : new Version())
            .ThenByDescending(device => device.Name.Contains("4K", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .First();

    static string? ExtractTvOSVersion(string runtimeId)
    {
        var match = Regex.Match(runtimeId, @"tvOS[- ](\d+)[-. ](\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups[1].Value}.{match.Groups[2].Value}";

        match = Regex.Match(runtimeId, @"tvOS[- ](\d+)", RegexOptions.IgnoreCase);
        return match.Success ? $"{match.Groups[1].Value}.0" : null;
    }

    static string GetSimulatorRuntimeIdentifier() =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "tvossimulator-arm64" : "tvossimulator-x64";

    string FindSimulatorAppBundle()
    {
        var binDir = Path.Combine(GetSampleBuildOutputRoot(Platform), TargetFramework, GetSimulatorRuntimeIdentifier());
        if (!Directory.Exists(binDir))
            throw new InvalidOperationException($"tvOS simulator build output not found at: {binDir}");

        return Directory.GetDirectories(binDir, "*.app", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException($"No .app bundle found under {binDir}");
    }

    static async Task<string> ReadBundleIdAsync(string appBundlePath)
    {
        var plistPath = Path.Combine(appBundlePath, "Info.plist");
        var bundleId = await RunProcessCheckedAsync(
            "/usr/libexec/PlistBuddy",
            $"-c \"Print :CFBundleIdentifier\" \"{plistPath}\"");
        return bundleId.Trim();
    }
}
