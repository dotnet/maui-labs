using System.Text.Json;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class HostOperations(Func<Task<AgentStatus?>> getStatus)
{
    internal async Task<AlertInfo?> DetectAlertAsync(string? udid, int? pid)
    {
        using var driver = await CreateAlertDriverAsync(udid, pid);

        return await ((IAlertDriver)driver).DetectAlertAsync();
    }

    internal async Task<AlertInfo?> DismissAlertAsync(string? udid, int? pid, string? buttonLabel)
    {
        using var driver = await CreateAlertDriverAsync(udid, pid);

        if (driver is MacCatalystAppDriver && string.IsNullOrWhiteSpace(buttonLabel))
        {
            throw new ArgumentException(
                "An exact button label is required when dismissing a macOS alert. Run 'maui devflow ui alert detect' and pass one of its visible button labels.",
                nameof(buttonLabel));
        }

        return await ((IAlertDriver)driver).HandleAlertIfPresentAsync(buttonLabel);
    }

    internal async Task<string> GetAlertTreeAsync(string? udid, int? pid)
    {
        using var driver = await CreateAlertDriverAsync(udid, pid);

        return driver switch
        {
            MacCatalystAppDriver mac => await mac.GetAccessibilityTreeAsync(),
            AndroidAppDriver android => await android.GetAccessibilityTreeAsync(),
            WindowsAppDriver windows => await windows.GetAccessibilityTreeAsync(),
            iOSSimulatorAppDriver simulator => await simulator.GetAccessibilityTreeAsync(),
            _ => throw new NotSupportedException("The selected driver does not support accessibility trees.")
        };
    }

    internal async Task<(bool Success, string? Error)> SetPermissionAsync(
        IAppleProvider appleProvider, string action, string? udid, string? bundleId, string service)
    {
        var status = string.IsNullOrWhiteSpace(udid) ? await getStatus() : null;
        var resolved = await ResolveUdidAsync(udid, status);

        if (!SimulatorEnumParsing.TryParsePrivacyPermission(service, out var permission))
            return (false, $"Unknown permission service '{service}'. Valid services: {SimulatorEnumParsing.PrivacyPermissionNames}.");

        return appleProvider.SetPrivacy(action, resolved, permission, bundleId)
            ? (true, null) : (false, $"simctl privacy {action} failed for service '{service}'.");
    }

    private async Task<IAppDriver> CreateAlertDriverAsync(string? udid, int? pid)
    {
        var platform = await ResolveAlertPlatformAsync(udid, pid);

        return platform switch
        {
            "maccatalyst" => new MacCatalystAppDriver { ProcessId = await ResolveMacCatalystPidAsync(pid) },
            "android" => new AndroidAppDriver { Serial = udid },
            "windows" => new WindowsAppDriver { ProcessId = await ResolveWindowsPidAsync(pid) },
            _ => new iOSSimulatorAppDriver { DeviceUdid = await ResolveUdidAsync(udid) }
        };
    }

    internal async Task<string> ResolveAlertPlatformAsync(string? udid, int? pid)
    {
        if (!string.IsNullOrEmpty(udid))
            return "ios-simulator";

        if (pid.HasValue)
            return OperatingSystem.IsWindows() ? "windows" : "maccatalyst";

        try
        {
            var status = await getStatus();
            if (status?.Platform != null)
            {
                var platform = status.Platform.ToLowerInvariant();
                if (platform.Contains("tvos"))
                    throw new NotSupportedException("Native tvOS dialog automation requires remote focus input, which is not supported. Use agent UI actions for app controls.");
                if (platform.Contains("ios")) return "ios-simulator";
                if (platform.Contains("android")) return "android";
                if (platform.Contains("windows")) return "windows";
                if (platform.Contains("linux") || platform.Contains("gtk")) return "linux";
                // Prefer a booted iOS simulator over the desktop fallback for dialog detection.
            }
        }
        catch (NotSupportedException) { throw; }
        catch { }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            try
            {
                var result = await ProcessRunner.RunAsync("xcrun", ["simctl", "list", "devices", "booted", "-j"]);

                if (!result.Success || result.ExitCode != 0)
                    throw new InvalidOperationException($"xcrun simctl failed with exit code {result.ExitCode}: {result.StandardError}");

                if (string.IsNullOrWhiteSpace(result.StandardOutput))
                    throw new InvalidOperationException("xcrun simctl returned no output.");

                using var doc = JsonDocument.Parse(result.StandardOutput);
                if (doc.RootElement.TryGetProperty("devices", out var devices))
                {
                    foreach (var runtime in devices.EnumerateObject())
                    {
                        foreach (var device in runtime.Value.EnumerateArray())
                        {
                            if (device.TryGetProperty("state", out var state) && state.GetString() == "Booted")
                                return "ios-simulator";
                        }
                    }
                }
            }
            catch { }
        }

        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        return "maccatalyst";
    }

    internal async Task<int> ResolveMacCatalystPidAsync(int? pid)
    {
        if (pid.HasValue)
            return pid.Value;

        try
        {
            var status = await getStatus();
            if (status?.App?.ProcessId is int processId)
                return processId;

            var appName = status?.App?.Name ?? status?.AppName;
            if (!string.IsNullOrWhiteSpace(appName))
            {
                var matchedProcessId = ProcessNameResolver.FindUniqueProcessId(appName);
                if (matchedProcessId.HasValue)
                    return matchedProcessId.Value;
            }
        }
        catch { }

        throw new InvalidOperationException("Cannot determine Mac Catalyst app PID. Specify --pid.");
    }

    private async Task<int> ResolveWindowsPidAsync(int? pid)
    {
        if (pid.HasValue)
            return pid.Value;

        try
        {
            var status = await getStatus();
            var appName = status?.App?.Name ?? status?.AppName;
            if (!string.IsNullOrWhiteSpace(appName))
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(appName);
                if (processes.Length > 0)
                    return processes[0].Id;

                var match = System.Diagnostics.Process.GetProcesses()
                    .FirstOrDefault(p =>
                    {
                        try { return p.ProcessName.Contains(appName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });
                if (match != null)
                    return match.Id;
            }
        }
        catch { }

        throw new InvalidOperationException("Cannot determine Windows app PID. Specify --pid.");
    }

    internal static bool IsAppleSimulatorPlatform(string? platform)
        => platform?.Equals("ios", StringComparison.OrdinalIgnoreCase) == true
            || platform?.Equals("iossimulator", StringComparison.OrdinalIgnoreCase) == true
            || platform?.Equals("tvos", StringComparison.OrdinalIgnoreCase) == true
            || platform?.Equals("tvossimulator", StringComparison.OrdinalIgnoreCase) == true;

    internal static async Task<string> ResolveUdidAsync(string? udid, AgentStatus? status = null)
    {
        if (!string.IsNullOrWhiteSpace(udid))
            return udid;

        if (status != null && IsAppleSimulatorPlatform(status.Platform))
        {
            if (!string.Equals(status.DeviceType, "Virtual", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected agent is not running on an Apple simulator. Specify a simulator UDID.");

            if (!string.IsNullOrWhiteSpace(status.Device?.Id))
                return status.Device.Id;
        }

        var result = await ProcessRunner.RunAsync("xcrun", ["simctl", "list", "devices", "booted", "-j"]);
        if (!result.Success || result.ExitCode != 0)
        {
            var message = $"Failed to resolve simulator UDID. 'xcrun simctl list devices booted -j' exited with code {result.ExitCode}.";
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? message : message + " Error: " + result.StandardError.Trim());
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new InvalidOperationException("Failed to resolve simulator UDID: no output received from 'xcrun simctl list devices booted -j'.");

        return SelectBootedSimulator(result.StandardOutput, status?.Platform);
    }

    internal static string SelectBootedSimulator(string json, string? platform = null)
    {
        using var doc = JsonDocument.Parse(json);
        var candidates = new List<string>();
        if (doc.RootElement.TryGetProperty("devices", out var devices))
        {
            foreach (var runtime in devices.EnumerateObject())
            {
                var runtimePlatform = platform?.StartsWith("tvos", StringComparison.OrdinalIgnoreCase) == true
                    ? "tvOS" : platform?.StartsWith("ios", StringComparison.OrdinalIgnoreCase) == true ? "iOS" : null;
                if (runtimePlatform != null && !runtime.Name.Contains($".{runtimePlatform}-", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var device in runtime.Value.EnumerateArray())
                {
                    if (device.TryGetProperty("state", out var state) && state.GetString() == "Booted"
                        && (!device.TryGetProperty("isAvailable", out var available) || available.GetBoolean()))
                    {
                        candidates.Add(device.GetProperty("udid").GetString()!);
                    }
                }
            }
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException("No matching booted simulator found. Specify --udid or boot a simulator."),
            _ => throw new InvalidOperationException("Multiple matching simulators are booted. Specify --udid to choose one.")
        };
    }
}
