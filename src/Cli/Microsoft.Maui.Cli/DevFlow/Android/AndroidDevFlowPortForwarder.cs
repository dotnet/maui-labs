using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.DevFlow.Android;

internal sealed class AndroidDevFlowPortForwarder
{
    public const int DefaultBrokerPort = BrokerServer.DefaultPort;

    static readonly TimeSpan AdbCallTimeout = TimeSpan.FromSeconds(15);

    readonly IAndroidProvider _androidProvider;
    readonly string? _adbPath;
    readonly AdbRunner? _adbRunner;

    public AndroidDevFlowPortForwarder(
        IAndroidProvider androidProvider,
        string? adbPath,
        AdbRunner? adbRunner)
    {
        if (adbPath != null && adbRunner == null)
            throw new ArgumentNullException(nameof(adbRunner), "adbRunner is required when adbPath is provided");

        _androidProvider = androidProvider;
        _adbPath = adbPath;
        _adbRunner = adbRunner;
    }

    public static AndroidDevFlowPortForwarder CreateDefault()
    {
        var provider = Program.AndroidProvider;
        var environment = AndroidEnvironment.BuildEnvironmentVariables(provider.SdkPath, provider.JdkPath);
        var adb = new Adb(() => provider.SdkPath, environment);

        return new AndroidDevFlowPortForwarder(provider, adb.AdbPath, adb.Runner);
    }

    /// <summary>
    /// Cheap, side-effect-free probe: returns true only when the Android SDK paths
    /// resolve to an existing <c>adb</c> binary. Used to short-circuit forwarding work
    /// on machines that do not have the Android SDK installed.
    /// </summary>
    public static bool IsAdbLikelyAvailable()
    {
        try
        {
            var provider = Program.AndroidProvider;
            var environment = AndroidEnvironment.BuildEnvironmentVariables(provider.SdkPath, provider.JdkPath);
            var adb = new Adb(() => provider.SdkPath, environment);
            return !string.IsNullOrWhiteSpace(adb.AdbPath) && File.Exists(adb.AdbPath);
        }
        catch
        {
            return false;
        }
    }

    public async Task<AndroidDevFlowForwardingReport> EnsureAsync(
        AndroidDevFlowForwardingRequest request,
        CancellationToken cancellationToken = default)
    {
        var brokerPort = request.BrokerPort > 0 ? request.BrokerPort : DefaultBrokerPort;

        var report = new AndroidDevFlowForwardingReport
        {
            AdbAvailable = !string.IsNullOrWhiteSpace(_adbPath),
            AdbPath = _adbPath,
            RequestedSerial = request.DeviceSerial ?? Environment.GetEnvironmentVariable("ANDROID_SERIAL"),
            BrokerPort = brokerPort,
            AgentPorts = request.AgentPorts.Distinct().OrderBy(static port => port).ToArray(),
            RepairRequested = request.Repair
        };

        if (string.IsNullOrWhiteSpace(_adbPath))
        {
            return report with
            {
                Status = AndroidDevFlowForwardingStatus.AdbNotFound,
                Message = "ADB was not found. Install Android platform-tools or set ANDROID_HOME.",
                Suggestions = ["maui android sdk install platform-tools"]
            };
        }

        List<Device> devices;
        try
        {
            devices = await _androidProvider.GetDevicesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return report with
            {
                Status = AndroidDevFlowForwardingStatus.Error,
                Message = $"Failed to list Android devices: {ex.Message}",
                Suggestions = ["adb devices"]
            };
        }

        report = report with
        {
            Devices = devices
                .Where(IsAndroidDevice)
                .Select(AndroidDevFlowDevice.FromDevice)
                .ToArray()
        };

        var onlineDevices = report.Devices
            .Where(static d => d.IsOnline)
            .ToArray();

        var selected = SelectDevice(onlineDevices, report.RequestedSerial);
        if (selected.Status != AndroidDevFlowForwardingStatus.Ok)
        {
            return report with
            {
                Status = selected.Status,
                Message = selected.Message,
                Suggestions = selected.Suggestions
            };
        }

        report = report with { SelectedSerial = selected.Device!.Serial };

        var reverseBefore = AndroidPortMappingList.Ok([]);
        if (request.EnsureBrokerReverse)
        {
            reverseBefore = await ListMappingsAsync(report.SelectedSerial, reverse: true, cancellationToken);
            if (!reverseBefore.Success)
                return report with { Status = AndroidDevFlowForwardingStatus.Error, Message = reverseBefore.Error, Suggestions = ["adb reverse --list"] };
        }

        var forwardBefore = await ListMappingsAsync(report.SelectedSerial, reverse: false, cancellationToken);
        if (!forwardBefore.Success)
            return report with { Status = AndroidDevFlowForwardingStatus.Error, Message = forwardBefore.Error, Suggestions = ["adb forward --list"] };

        var errors = new List<string>();
        var brokerReverseBefore = request.EnsureBrokerReverse
            && ContainsMapping(reverseBefore.Mappings, brokerPort);
        var brokerReverseAdded = false;

        if (request.EnsureBrokerReverse && !brokerReverseBefore && request.Repair)
        {
            try
            {
                var spec = new AdbPortSpec(AdbProtocol.Tcp, brokerPort);
                using var timeoutCts = CreateAdbCallTimeoutSource(cancellationToken);
                await _adbRunner!.ReversePortAsync(report.SelectedSerial, spec, spec, timeoutCts.Token);
                brokerReverseAdded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller-initiated cancellation (Ctrl+C, etc.) must propagate. Cancellation
                // originating from the internal per-call timeout below falls through to the
                // general catch so a single slow adb invocation is reported as an error.
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"adb reverse tcp:{brokerPort} tcp:{brokerPort} failed: {ex.Message}");
            }
        }

        var agentForwards = new List<AndroidDevFlowPortForward>();
        foreach (var port in report.AgentPorts)
        {
            var presentBefore = ContainsMapping(forwardBefore.Mappings, port);
            var added = false;
            if (!presentBefore && request.Repair)
            {
                try
                {
                    var spec = new AdbPortSpec(AdbProtocol.Tcp, port);
                    using var timeoutCts = CreateAdbCallTimeoutSource(cancellationToken);
                    await _adbRunner!.ForwardPortAsync(report.SelectedSerial, spec, spec, timeoutCts.Token);
                    added = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"adb forward tcp:{port} tcp:{port} failed: {ex.Message}");
                }
            }

            agentForwards.Add(new AndroidDevFlowPortForward
            {
                Port = port,
                PresentBefore = presentBefore,
                Added = added
            });
        }

        var reverseAfter = AndroidPortMappingList.Ok([]);
        var brokerReverseChecked = false;
        if (request.EnsureBrokerReverse)
        {
            reverseAfter = await ListMappingsAsync(report.SelectedSerial, reverse: true, cancellationToken);
            if (reverseAfter.Success)
                brokerReverseChecked = true;
            else
                errors.Add(reverseAfter.Error ?? "Failed to verify adb reverse mappings.");
        }

        var forwardAfter = await ListMappingsAsync(report.SelectedSerial, reverse: false, cancellationToken);
        if (!forwardAfter.Success)
            errors.Add(forwardAfter.Error ?? "Failed to verify adb forward mappings.");

        var brokerReversePresent = request.EnsureBrokerReverse
            && reverseAfter.Success
            && ContainsMapping(reverseAfter.Mappings, brokerPort);

        agentForwards = agentForwards
            .Select(f => f with
            {
                PresentAfter = forwardAfter.Success && ContainsMapping(forwardAfter.Mappings, f.Port)
            })
            .ToList();

        var missingPorts = agentForwards
            .Where(static f => !f.PresentAfter)
            .Select(static f => f.Port)
            .ToArray();

        if (errors.Count > 0)
        {
            return report with
            {
                Status = AndroidDevFlowForwardingStatus.Error,
                BrokerReverseChecked = brokerReverseChecked,
                BrokerReversePresent = brokerReversePresent,
                BrokerReverseAdded = brokerReverseAdded,
                AgentForwards = agentForwards.ToArray(),
                Message = string.Join(Environment.NewLine, errors),
                Suggestions = BuildMappingSuggestions(report.SelectedSerial, request.EnsureBrokerReverse && !brokerReversePresent, brokerPort, missingPorts)
            };
        }

        var repaired = brokerReverseAdded || agentForwards.Any(static f => f.Added);
        var brokerReverseMissing = request.EnsureBrokerReverse && !brokerReversePresent;
        var missing = !brokerReverseMissing && missingPorts.Length == 0
            ? Array.Empty<string>()
            : BuildMappingSuggestions(report.SelectedSerial, brokerReverseMissing, brokerPort, missingPorts);

        var status = missing.Length > 0
            ? AndroidDevFlowForwardingStatus.Missing
            : repaired
                ? AndroidDevFlowForwardingStatus.Repaired
                : AndroidDevFlowForwardingStatus.Ok;

        return report with
        {
            Status = status,
            BrokerReverseChecked = brokerReverseChecked,
            BrokerReversePresent = brokerReversePresent,
            BrokerReverseAdded = brokerReverseAdded,
            AgentForwards = agentForwards.ToArray(),
            Message = status switch
            {
                AndroidDevFlowForwardingStatus.Repaired => "Android DevFlow ADB forwarding was repaired.",
                AndroidDevFlowForwardingStatus.Missing => "Android DevFlow ADB forwarding is incomplete.",
                _ => "Android DevFlow ADB forwarding is ready."
            },
            Suggestions = missing
        };
    }

    public async Task<AndroidDevFlowDeviceResolution> ResolveDeviceForForwardedPortAsync(
        int agentPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_adbPath) || _adbRunner is null)
            return AndroidDevFlowDeviceResolution.Failed("ADB was not found. Install Android platform-tools or set ANDROID_HOME.");

        List<Device> devices;
        try
        {
            devices = await _androidProvider.GetDevicesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AndroidDevFlowDeviceResolution.Failed($"Failed to list Android devices: {ex.Message}");
        }

        var onlineDevices = devices
            .Where(IsAndroidDevice)
            .Select(AndroidDevFlowDevice.FromDevice)
            .Where(static device => device.IsOnline)
            .ToArray();
        if (onlineDevices.Length == 0)
            return AndroidDevFlowDeviceResolution.Failed("No online Android devices or emulators were found.");

        var matches = new List<string>();
        var errors = new List<string>();
        foreach (var device in onlineDevices)
        {
            var mappings = await ListMappingsAsync(device.Serial, reverse: false, cancellationToken);
            if (!mappings.Success)
            {
                errors.Add($"{device.Serial}: {mappings.Error}");
                continue;
            }

            if (ContainsMapping(mappings.Mappings, agentPort))
                matches.Add(device.Serial);
        }

        if (matches.Count > 1)
            return AndroidDevFlowDeviceResolution.Failed(
                $"Agent port {agentPort} is forwarded by multiple Android devices ({string.Join(", ", matches)}). Remove stale ADB forwards and reconnect.");
        if (errors.Count > 0)
            return AndroidDevFlowDeviceResolution.Failed(
                $"Could not identify the Android device for agent port {agentPort}: {string.Join("; ", errors)}");
        if (matches.Count == 1)
            return AndroidDevFlowDeviceResolution.Resolved(matches[0], _adbPath);

        return AndroidDevFlowDeviceResolution.Failed(
            $"No online Android device owns the ADB forward for agent port {agentPort}. Reconnect the app or run 'maui devflow diagnose'.");
    }

    static bool IsAndroidDevice(Device device)
        => device.Platforms.Any(static p => p.Equals("android", StringComparison.OrdinalIgnoreCase));

    static AndroidDeviceSelection SelectDevice(AndroidDevFlowDevice[] onlineDevices, string? requestedSerial)
    {
        if (!string.IsNullOrWhiteSpace(requestedSerial))
        {
            var match = onlineDevices.FirstOrDefault(d => d.Serial.Equals(requestedSerial, StringComparison.OrdinalIgnoreCase));
            return match is not null
                ? AndroidDeviceSelection.Ok(match)
                : AndroidDeviceSelection.Failed(
                    AndroidDevFlowForwardingStatus.DeviceNotFound,
                    $"Android device '{requestedSerial}' is not connected and online.",
                    ["adb devices"]);
        }

        return onlineDevices.Length switch
        {
            0 => AndroidDeviceSelection.Failed(
                AndroidDevFlowForwardingStatus.NoDevice,
                "No online Android devices or emulators were found.",
                ["adb devices"]),
            1 => AndroidDeviceSelection.Ok(onlineDevices[0]),
            _ => AndroidDeviceSelection.Failed(
                AndroidDevFlowForwardingStatus.MultipleDevices,
                "Multiple online Android devices or emulators were found. Specify --device or ANDROID_SERIAL.",
                onlineDevices.Select(static d => $"--device {d.Serial}").ToArray())
        };
    }

    async Task<AndroidPortMappingList> ListMappingsAsync(string serial, bool reverse, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CreateAdbCallTimeoutSource(cancellationToken);
            var rules = reverse
                ? await _adbRunner!.ListReversePortsAsync(serial, timeoutCts.Token)
                : await _adbRunner!.ListForwardPortsAsync(serial, timeoutCts.Token);
            return AndroidPortMappingList.Ok(rules);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var command = reverse ? "adb reverse --list" : "adb forward --list";
            return AndroidPortMappingList.Failed($"{command} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a linked <see cref="CancellationTokenSource"/> that caps a single
    /// <c>adb</c> invocation at <see cref="AdbCallTimeout"/>, while still observing the
    /// caller's <paramref name="cancellationToken"/>. Callers distinguish the two by
    /// checking <c>cancellationToken.IsCancellationRequested</c> in their catch block:
    /// only the caller's own token should cause the whole operation to abort.
    /// </summary>
    static CancellationTokenSource CreateAdbCallTimeoutSource(CancellationToken cancellationToken)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AdbCallTimeout);
        return timeoutCts;
    }

    static bool ContainsMapping(IEnumerable<AdbPortRule> mappings, int port)
        => mappings.Any(m =>
            m.Local.Protocol == AdbProtocol.Tcp && m.Local.Port == port
            && m.Remote.Protocol == AdbProtocol.Tcp && m.Remote.Port == port);

    static string[] BuildMappingSuggestions(string serial, bool brokerReverseMissing, int brokerPort, int[] missingAgentForwards)
    {
        var suggestions = new List<string>();
        if (brokerReverseMissing)
            suggestions.Add($"adb -s {serial} reverse tcp:{brokerPort} tcp:{brokerPort}");
        foreach (var port in missingAgentForwards)
            suggestions.Add($"adb -s {serial} forward tcp:{port} tcp:{port}");
        return suggestions.ToArray();
    }

    sealed record AndroidDeviceSelection(AndroidDevFlowForwardingStatus Status, AndroidDevFlowDevice? Device, string? Message, string[] Suggestions)
    {
        public static AndroidDeviceSelection Ok(AndroidDevFlowDevice device) => new(AndroidDevFlowForwardingStatus.Ok, device, null, []);

        public static AndroidDeviceSelection Failed(AndroidDevFlowForwardingStatus status, string message, string[] suggestions)
            => new(status, null, message, suggestions);
    }

    sealed record AndroidPortMappingList(bool Success, IReadOnlyList<AdbPortRule> Mappings, string? Error)
    {
        public static AndroidPortMappingList Ok(IReadOnlyList<AdbPortRule> mappings) => new(true, mappings, null);

        public static AndroidPortMappingList Failed(string error) => new(false, [], error);
    }
}

internal sealed record AndroidDevFlowForwardingRequest
{
    public int[] AgentPorts { get; init; } = [];

    public bool EnsureBrokerReverse { get; init; }

    /// <summary>
    /// Broker port that the agent inside the emulator should reach via <c>adb reverse</c>.
    /// Defaults to <see cref="BrokerServer.DefaultPort"/> when zero, so callers that know
    /// the resolved broker port (for example via <c>BrokerClient.ReadBrokerPortPublic()</c>)
    /// can override it for non-default broker deployments.
    /// </summary>
    public int BrokerPort { get; init; }

    public bool Repair { get; init; }

    public string? DeviceSerial { get; init; }
}

internal sealed record AndroidDevFlowDeviceResolution(string? Serial, string? AdbPath, string? Error)
{
    public bool IsResolved => !string.IsNullOrWhiteSpace(Serial) && string.IsNullOrWhiteSpace(Error);

    public static AndroidDevFlowDeviceResolution Resolved(string serial, string? adbPath = null) => new(serial, adbPath, null);

    public static AndroidDevFlowDeviceResolution Failed(string error) => new(null, null, error);
}

[JsonConverter(typeof(JsonStringEnumConverter<AndroidDevFlowForwardingStatus>))]
internal enum AndroidDevFlowForwardingStatus
{
    Ok,
    Repaired,
    Missing,
    AdbNotFound,
    NoDevice,
    MultipleDevices,
    DeviceNotFound,
    Error
}

internal sealed record AndroidDevFlowForwardingReport
{
    [JsonPropertyName("status")]
    public AndroidDevFlowForwardingStatus Status { get; init; } = AndroidDevFlowForwardingStatus.Ok;

    [JsonPropertyName("adb_available")]
    public bool AdbAvailable { get; init; }

    [JsonPropertyName("adb_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdbPath { get; init; }

    [JsonPropertyName("requested_serial")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestedSerial { get; init; }

    [JsonPropertyName("selected_serial")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedSerial { get; init; }

    [JsonPropertyName("devices")]
    public AndroidDevFlowDevice[] Devices { get; init; } = [];

    [JsonPropertyName("broker_port")]
    public int BrokerPort { get; init; }

    [JsonPropertyName("broker_reverse_present")]
    public bool BrokerReversePresent { get; init; }

    [JsonPropertyName("broker_reverse_checked")]
    public bool BrokerReverseChecked { get; init; }

    [JsonPropertyName("broker_reverse_added")]
    public bool BrokerReverseAdded { get; init; }

    [JsonPropertyName("agent_ports")]
    public int[] AgentPorts { get; init; } = [];

    [JsonPropertyName("agent_forwards")]
    public AndroidDevFlowPortForward[] AgentForwards { get; init; } = [];

    [JsonPropertyName("repair_requested")]
    public bool RepairRequested { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonPropertyName("suggestions")]
    public string[] Suggestions { get; init; } = [];

    [JsonIgnore]
    public bool IsReady => Status is AndroidDevFlowForwardingStatus.Ok or AndroidDevFlowForwardingStatus.Repaired;
}

internal sealed record AndroidDevFlowDevice
{
    [JsonPropertyName("serial")]
    public required string Serial { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter<DeviceState>))]
    public DeviceState State { get; init; }

    [JsonPropertyName("is_emulator")]
    public bool IsEmulator { get; init; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; init; }

    public static AndroidDevFlowDevice FromDevice(Device device)
        => new()
        {
            Serial = device.Id,
            Name = device.Name,
            State = device.State,
            IsEmulator = device.IsEmulator,
            IsOnline = device.State is DeviceState.Connected or DeviceState.Booted
        };
}

internal sealed record AndroidDevFlowPortForward
{
    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("present_before")]
    public bool PresentBefore { get; init; }

    [JsonPropertyName("added")]
    public bool Added { get; init; }

    [JsonPropertyName("present_after")]
    public bool PresentAfter { get; init; }
}
