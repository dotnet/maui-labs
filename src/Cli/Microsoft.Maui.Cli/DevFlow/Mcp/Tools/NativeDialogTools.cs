using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class NativeDialogTools
{
    private static readonly ConcurrentDictionary<string, DialogLease> DialogLeases = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TargetLocks = new();
    private static readonly TimeSpan DialogLeaseLifetime = TimeSpan.FromMinutes(2);

    [McpServerTool(Name = "maui_native_dialog_detect"), Description(
        "Detect an app-owned or system permission dialog without changing host focus or sending host keyboard/pointer input. " +
        "Supports macOS AppKit/Mac Catalyst, Windows, Android, iOS Simulator, and Linux when their safe platform automation is available. " +
        "If a dialog is returned, pause and ask the user which exact button to press before calling maui_native_dialog_respond.")]
    public static async Task<string> Detect(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent is connected)")] int? agentPort = null,
        [Description("Android adb device/emulator serial to validate against the device mapped to this agent. Usually omitted.")] string? androidDevice = null,
        [Description("iOS Simulator UDID to validate against the simulator reported by this agent. Usually omitted.")] string? simulatorUdid = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.GetStatusAsync()
            ?? throw new McpException("Unable to read agent status.");
        var target = await CreateTargetAsync(session, agent, status, androidDevice, simulatorUdid);
        if (!target.CanDetect)
            return SerializeManualAction(target.UnsupportedReason!);

        var targetLock = TargetLocks.GetOrAdd(target.TargetIdentity, static _ => new SemaphoreSlim(1, 1));
        await targetLock.WaitAsync();
        try
        {
            return await DetectForTargetAsync(session, target);
        }
        finally
        {
            targetLock.Release();
        }
    }

    private static async Task<string> DetectForTargetAsync(McpAgentSession session, DialogTarget target)
    {
        if (target.Kind == DialogPlatformKind.MacOS && !OperatingSystem.IsMacOS())
            return SerializeManualAction("macOS native dialog automation requires running maui on macOS.");

        if (target.Kind == DialogPlatformKind.MacOS)
        {
            using var macDriver = CreateMacDriver(target);
            if (!macDriver.IsAccessibilityAuthorized())
            {
                return SerializeManualAction(
                    "Grant macOS Accessibility access to the terminal or host running maui, then retry.",
                    "accessibility-permission-required");
            }
        }

        var dialog = await DetectDialogAsync(target);
        if (dialog is null)
        {
            return Serialize(new JsonObject
            {
                ["detected"] = false,
                ["canRespond"] = false,
                ["userActionRequired"] = false,
                ["platform"] = target.Platform
            });
        }
        if (target.Kind == DialogPlatformKind.Android)
        {
            using var androidDriver = new AndroidAppDriver { Serial = target.AndroidDevice };
            var attributedToTarget = await androidDriver.IsDialogAttributedToTargetAsync(
                dialog, target.PackageId ?? string.Empty, target.AppName ?? string.Empty);
            if (!attributedToTarget)
            {
                return SerializeManualAction(
                    "The visible Android dialog could not be safely attributed to the connected app.",
                    "dialog-attribution-failed");
            }
        }

        var platformPromptId = dialog.PromptId;
        var canRespond = dialog.CanRespond || target.Kind != DialogPlatformKind.MacOS;
        dialog = dialog with
        {
            CanRespond = canRespond,
            RequiresUserConfirmation = true
        };

        if (canRespond)
        {
            var promptId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var fingerprint = NativeDialogIdentity.CreateFingerprint(
                target.Platform,
                target.TargetIdentity,
                dialog);
            var lease = new DialogLease(
                session,
                platformPromptId,
                target,
                fingerprint,
                dialog,
                DateTimeOffset.UtcNow.Add(DialogLeaseLifetime),
                platformPromptId is null
                    ? null
                    : () => MacCatalystAppDriver.CancelAlertResponse(platformPromptId));
            RemoveMatchingDialogLeases(target.TargetIdentity, fingerprint);
            if (!DialogLeases.TryAdd(promptId, lease))
                throw new McpException("Unable to create a unique native-dialog response token.");
            _ = ExpireDialogLeaseAsync(promptId);
            dialog = dialog with { PromptId = promptId };
        }

        var result = CreateDialogJson(dialog, target);
        result["detected"] = true;
        result["userActionRequired"] = true;
        result["instruction"] = canRespond
            ? "Call maui_native_dialog_respond with this promptId and the exact button label. The MCP host must present a separate confirmation and receive the user's approval before any action."
            : "This dialog cannot be safely automated on the current platform. Pause and ask the user to respond manually.";
        return Serialize(result);
    }

    [McpServerTool(Name = "maui_native_dialog_respond"), Description(
        "Invoke an exact button on the exact native dialog returned by maui_native_dialog_detect, using the safest platform primitive available. " +
        "Uses AXPress on macOS, UI Automation Invoke on Windows, app-agent actions on Linux, and device/simulator-scoped input on Android/iOS Simulator. " +
        "Requests confirmation through the MCP host for this exact prompt and button; callers cannot assert consent. " +
        "Hosts without form elicitation support require manual interaction.")]
    public static Task<string> Respond(
        McpAgentSession session,
        [Description("One-time prompt token returned by maui_native_dialog_detect")] string promptId,
        [Description("Exact visible button label chosen by the user, such as Allow or Don't Allow")] string buttonLabel,
        [Description("Agent HTTP port (optional if only one agent is connected)")] int? agentPort = null,
        [Description("MCP server supplied by the host, not a caller argument")] McpServer? server = null,
        [Description("Cancellation token supplied by the host")] CancellationToken cancellationToken = default)
        => RespondCore(
            session, promptId, buttonLabel, agentPort,
            server?.ClientCapabilities?.Elicitation?.Form != null
                ? async (request, token) => await server.ElicitAsync(request, token) : null,
            cancellationToken);

    internal static async Task<string> RespondCore(
        McpAgentSession session,
        string promptId,
        string buttonLabel,
        int? agentPort = null,
        Func<ElicitRequestParams, CancellationToken, Task<ElicitResult>>? requestConfirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptId) || string.IsNullOrWhiteSpace(buttonLabel))
        {
            return Serialize(new JsonObject
            {
                ["success"] = false,
                ["userActionRequired"] = true,
                ["instruction"] = "Provide the one-time promptId and an exact visible button label from maui_native_dialog_detect."
            });
        }

        PurgeExpiredDialogLeases();
        if (!DialogLeases.TryGetValue(promptId, out var candidateLease)
            || !ReferenceEquals(candidateLease.Owner, session))
            return SerializeExpiredPrompt();

        var targetLock = TargetLocks.GetOrAdd(
            candidateLease.Target.TargetIdentity,
            static _ => new SemaphoreSlim(1, 1));
        await targetLock.WaitAsync(cancellationToken);
        try
        {
            if (!DialogLeases.TryRemove(promptId, out var lease))
                return SerializeExpiredPrompt();

            using (lease)
            {
                if (lease.ExpiresAt <= DateTimeOffset.UtcNow)
                    return SerializeExpiredPrompt();

                RemoveTargetDialogLeases(lease.Target.TargetIdentity);

                var matchingButtons = lease.Dialog.Buttons.Count(button =>
                    lease.Target.Kind == DialogPlatformKind.MacOS
                        ? MacCatalystAppDriver.ButtonLabelsMatch(button.Label, buttonLabel)
                        : button.Label.Equals(buttonLabel, StringComparison.Ordinal));
                if (matchingButtons != 1)
                {
                    return SerializeResponseManualAction(
                        "Choose one unambiguous exact label from the reviewed dialog before requesting confirmation.",
                        "dialog-button-not-found-or-ambiguous");
                }

                if (requestConfirmation == null)
                {
                    return SerializeResponseManualAction(
                        "This MCP host does not support native-dialog confirmation through form elicitation. Respond to the dialog manually.",
                        "host-confirmation-unavailable");
                }

                using var confirmationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                confirmationTimeout.CancelAfter(TimeSpan.FromMilliseconds(
                    Math.Max(0, (lease.ExpiresAt - DateTimeOffset.UtcNow).TotalMilliseconds)));
                ElicitResult confirmation;
                try
                {
                    confirmation = await requestConfirmation(new ElicitRequestParams
                    {
                        Message = $"Approve pressing '{buttonLabel}' on the {lease.Target.Platform} dialog for '{lease.Target.AppName}'? " +
                            $"Prompt: {lease.Dialog.Title}\n{string.Join("\n", lease.Dialog.Text)}\n" +
                            $"Reviewed prompt token: {promptId}. This may change app permissions.",
                        RequestedSchema = new()
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                            {
                                ["confirm"] = new ElicitRequestParams.BooleanSchema
                                {
                                    Title = $"Press '{buttonLabel}'",
                                    Description = "Approve this exact action on the reviewed dialog.",
                                    Default = false
                                }
                            },
                            Required = ["confirm"]
                        }
                    }, confirmationTimeout.Token);
                }
                catch (OperationCanceledException) when (confirmationTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return SerializeExpiredPrompt();
                }

                if (!confirmation.IsAccepted || confirmation.Content == null
                    || !confirmation.Content.TryGetValue("confirm", out var approved)
                    || approved.ValueKind != JsonValueKind.True)
                {
                    return SerializeResponseManualAction(
                        "The host did not receive approval for this exact dialog action. No button was pressed.",
                        "host-confirmation-declined");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (lease.ExpiresAt <= DateTimeOffset.UtcNow)
                    return SerializeExpiredPrompt();

                using var agent = await session.GetAgentClientAsync(agentPort);
                var status = await agent.GetStatusAsync()
                    ?? throw new McpException("Unable to read agent status.");
                var currentIdentity = CreateTargetIdentity(agent.BaseUrl, status);
                if (!currentIdentity.Equals(lease.Target.TargetIdentity, StringComparison.Ordinal))
                {
                    return Serialize(new JsonObject
                    {
                        ["success"] = false,
                        ["userActionRequired"] = true,
                        ["instruction"] = "The connected app changed after the prompt was reviewed. Detect the current prompt again."
                    });
                }
                if (lease.Target.Kind == DialogPlatformKind.Android)
                {
                    var currentSerial = session.GetAndroidDeviceSerial(new Uri(agent.BaseUrl).Port);
                    if (!string.Equals(currentSerial, lease.Target.AndroidDevice, StringComparison.Ordinal))
                    {
                        return SerializeResponseManualAction(
                            "The Android device mapping changed after the prompt was reviewed. Detect the prompt again.",
                            "target-device-changed");
                    }
                }

                var mutationLease = await agent.ControlMutationLeaseAsync("claim");
                if (!mutationLease.YouHold)
                {
                    return SerializeResponseManualAction(
                        "Another DevFlow client controls this app. Wait for it to release control, then detect the prompt again.",
                        "mutation-lease-held-by-other");
                }

                AlertActionResult action;
                if (lease.ExpiresAt <= DateTimeOffset.UtcNow)
                    return SerializeExpiredPrompt();

                if (lease.Target.Kind == DialogPlatformKind.MacOS)
                {
                    using var driver = CreateMacDriver(lease.Target);
                    if (!driver.IsAccessibilityAuthorized())
                    {
                        return SerializeResponseManualAction(
                            "Grant macOS Accessibility access to the terminal or host running maui, then detect the prompt again.",
                            "accessibility-permission-required");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    action = await driver.PressAlertButtonAsync(
                        lease.PlatformPromptId
                            ?? throw new McpException("The macOS dialog response token is invalid."),
                        buttonLabel);
                }
                else
                {
                    var currentDialog = await DetectDialogAsync(lease.Target);
                    if (currentDialog is null)
                    {
                        return Serialize(new JsonObject
                        {
                            ["success"] = false,
                            ["userActionRequired"] = true,
                            ["instruction"] = "The reviewed prompt is no longer visible. Detect again before acting."
                        });
                    }

                    var currentFingerprint = NativeDialogIdentity.CreateFingerprint(
                        lease.Target.Platform,
                        lease.Target.TargetIdentity,
                        currentDialog);
                    if (!currentFingerprint.Equals(lease.Fingerprint, StringComparison.Ordinal))
                    {
                        return Serialize(new JsonObject
                        {
                            ["success"] = false,
                            ["userActionRequired"] = true,
                            ["instruction"] = "The visible prompt changed after it was reviewed. Detect it again and ask the user what to do.",
                            ["dialog"] = CreateDialogJson(currentDialog, lease.Target)
                        });
                    }

                    if (lease.ExpiresAt <= DateTimeOffset.UtcNow)
                        return SerializeExpiredPrompt();

                    cancellationToken.ThrowIfCancellationRequested();
                    action = await PressDialogButtonAsync(lease.Target, currentDialog, buttonLabel, agent, cancellationToken);
                }

                var result = new JsonObject
                {
                    ["success"] = action.Success,
                    ["userActionRequired"] = action.UserActionRequired,
                    ["message"] = action.Message,
                    ["platform"] = lease.Target.Platform,
                    ["interactionMethod"] = lease.Target.InteractionMethod,
                    ["confirmationMethod"] = "mcp-form-elicitation",
                    ["hostInputIsolated"] = true
                };
                if (action.Dialog is not null)
                    result["dialog"] = CreateDialogJson(action.Dialog, lease.Target);
                return Serialize(result);
            }
        }
        finally
        {
            targetLock.Release();
        }
    }

    private static async Task<DialogTarget> CreateTargetAsync(
        McpAgentSession session,
        AgentClient agent,
        AgentStatus status,
        string? androidDevice,
        string? simulatorUdid)
    {
        var platform = status.Platform ?? string.Empty;
        var identity = CreateTargetIdentity(agent.BaseUrl, status);
        if (!new Uri(agent.BaseUrl).IsLoopback
            && !platform.Contains("linux", StringComparison.OrdinalIgnoreCase))
        {
            return DialogTarget.Unsupported(
                platform,
                identity,
                "Host native-dialog automation requires a local agent; a remote process or device cannot be safely targeted.");
        }

        if (platform.Contains("mac", StringComparison.OrdinalIgnoreCase))
        {
            var processId = status.App?.ProcessId ?? ProcessNameResolver.FindUniqueProcessId(status.AppName);
            return processId.HasValue
                ? new DialogTarget(DialogPlatformKind.MacOS, platform, identity, "ax-press", processId, status.AppName, status.App?.PackageId, null, null, agent.BaseUrl)
                : DialogTarget.Unsupported(platform, identity, "Unable to determine the connected macOS app process.");
        }

        if (platform.Contains("android", StringComparison.OrdinalIgnoreCase))
        {
            var agentPort = new Uri(agent.BaseUrl).Port;
            var selectedDevice = session.GetAndroidDeviceSerial(agentPort);
            if (string.IsNullOrWhiteSpace(selectedDevice))
                return DialogTarget.Unsupported(platform, identity, "The Android device associated with the connected agent could not be determined safely.");
            if (!string.IsNullOrWhiteSpace(androidDevice)
                && !selectedDevice.Equals(androidDevice, StringComparison.Ordinal))
            {
                return DialogTarget.Unsupported(platform, identity, "The requested Android device does not match the device associated with the connected agent.");
            }
            if (string.IsNullOrWhiteSpace(status.App?.PackageId))
                return DialogTarget.Unsupported(platform, identity, "The connected Android agent did not report its package identifier.");

            return new DialogTarget(
                DialogPlatformKind.Android,
                platform,
                identity,
                "adb-device-input",
                null,
                status.AppName,
                status.App.PackageId,
                selectedDevice,
                null,
                agent.BaseUrl);
        }

        if (platform.Contains("windows", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("winui", StringComparison.OrdinalIgnoreCase))
        {
            var processId = status.App?.ProcessId ?? ProcessNameResolver.FindUniqueProcessId(status.AppName);
            return processId.HasValue
                ? new DialogTarget(DialogPlatformKind.Windows, platform, identity, "uia-invoke", processId, status.AppName, status.App?.PackageId, null, null, agent.BaseUrl)
                : DialogTarget.Unsupported(platform, identity, "Unable to determine the connected Windows app process.");
        }

        if (platform.Contains("linux", StringComparison.OrdinalIgnoreCase))
            return new DialogTarget(DialogPlatformKind.Linux, platform, identity, "agent-action", status.App?.ProcessId, status.AppName, status.App?.PackageId, null, null, agent.BaseUrl);

        if (platform.Contains("ios", StringComparison.OrdinalIgnoreCase))
        {
            var isSimulator = status.DeviceType?.Equals("Virtual", StringComparison.OrdinalIgnoreCase) == true;
            if (!isSimulator)
                return DialogTarget.Unsupported(platform, identity, "Physical iOS native dialogs are not safely automatable by the current host driver.");
            if (string.IsNullOrWhiteSpace(status.Device?.Id))
                return DialogTarget.Unsupported(platform, identity, "The connected iOS Simulator agent did not report its simulator UDID.");
            if (!string.IsNullOrWhiteSpace(simulatorUdid)
                && !status.Device.Id.Equals(simulatorUdid, StringComparison.Ordinal))
            {
                return DialogTarget.Unsupported(platform, identity, "The requested simulator does not match the simulator associated with the connected agent.");
            }
            if (string.IsNullOrWhiteSpace(status.AppName))
                return DialogTarget.Unsupported(platform, identity, "The connected iOS Simulator agent did not report its app name.");

            return new DialogTarget(
                DialogPlatformKind.IosSimulator,
                platform,
                identity,
                "simulator-hid",
                null,
                status.AppName,
                status.App?.PackageId,
                null,
                status.Device.Id,
                agent.BaseUrl);
        }

        return DialogTarget.Unsupported(platform, identity, $"Native dialog automation is not available for platform '{platform}'.");
    }

    private static async Task<AlertInfo?> DetectDialogAsync(DialogTarget target)
    {
        switch (target.Kind)
        {
            case DialogPlatformKind.MacOS:
                using (var driver = CreateMacDriver(target))
                    return await driver.DetectAlertAsync();
            case DialogPlatformKind.Android:
                using (var driver = new AndroidAppDriver { Serial = target.AndroidDevice })
                    return await driver.DetectAlertAsync();
            case DialogPlatformKind.IosSimulator:
                using (var driver = new iOSSimulatorAppDriver
                {
                    DeviceUdid = target.SimulatorUdid,
                    BundleId = target.PackageId,
                    ExpectedAppName = target.AppName
                })
                    return await driver.DetectAlertAsync();
            case DialogPlatformKind.Windows:
                using (var driver = new WindowsAppDriver { ProcessId = target.ProcessId })
                    return await driver.DetectAlertAsync();
            case DialogPlatformKind.Linux:
                using (var driver = new LinuxAppDriver { ProcessId = target.ProcessId, AppName = target.AppName })
                {
                    var endpoint = new Uri(target.AgentBaseUrl);
                    await driver.ConnectAsync(endpoint.Host, endpoint.Port);
                    return await driver.DetectAlertAsync();
                }
            default:
                return null;
        }
    }

    private static async Task<AlertActionResult> PressDialogButtonAsync(
        DialogTarget target,
        AlertInfo dialog,
        string buttonLabel,
        AgentClient agent,
        CancellationToken cancellationToken)
    {
        switch (target.Kind)
        {
            case DialogPlatformKind.Android:
                using (var driver = new AndroidAppDriver { Serial = target.AndroidDevice })
                    return await driver.PressAlertButtonSafelyAsync(
                        dialog,
                        buttonLabel,
                        target.PackageId
                            ?? throw new McpException("The Android dialog target has no package identifier."),
                        target.AppName
                            ?? throw new McpException("The Android dialog target has no app name."),
                        cancellationToken);
            case DialogPlatformKind.IosSimulator:
                using (var driver = new iOSSimulatorAppDriver
                {
                    DeviceUdid = target.SimulatorUdid,
                    BundleId = target.PackageId,
                    ExpectedAppName = target.AppName
                })
                    return await driver.PressAlertButtonSafelyAsync(dialog, buttonLabel);
            case DialogPlatformKind.Windows:
                using (var driver = new WindowsAppDriver { ProcessId = target.ProcessId })
                    return await driver.PressAlertButtonSafelyAsync(dialog, buttonLabel);
            case DialogPlatformKind.Linux:
                using (var driver = new LinuxAppDriver { ProcessId = target.ProcessId, AppName = target.AppName })
                {
                    var endpoint = new Uri(target.AgentBaseUrl);
                    await driver.ConnectAsync(endpoint.Host, endpoint.Port);
                    using var mutationLease = driver.UseMutationLease(agent);
                    return await driver.PressAlertButtonSafelyAsync(dialog, buttonLabel);
                }
            default:
                return new AlertActionResult(false, true, "This platform cannot safely respond to native dialogs.", dialog);
        }
    }

    private static MacCatalystAppDriver CreateMacDriver(DialogTarget target)
        => new()
        {
            ProcessId = target.ProcessId,
            AppName = target.AppName
        };

    private static string CreateTargetIdentity(string baseUrl, AgentStatus status)
        => $"{baseUrl}|{status.Platform}|{status.Device?.Id}|{status.App?.ProcessId}|{status.App?.PackageId}|{status.AppName}";

    private static JsonObject CreateDialogJson(AlertInfo dialog, DialogTarget target)
        => new()
        {
            ["promptId"] = dialog.PromptId,
            ["title"] = dialog.Title,
            ["text"] = new JsonArray(dialog.Text.Select(text => (JsonNode?)text).ToArray()),
            ["buttons"] = new JsonArray(dialog.Buttons.Select(button => (JsonNode?)button.Label).ToArray()),
            ["sourceProcessId"] = dialog.SourceProcessId,
            ["sourceProcessName"] = dialog.SourceProcessName,
            ["isSystemDialog"] = dialog.IsSystemDialog,
            ["canRespond"] = dialog.CanRespond,
            ["platform"] = target.Platform,
            ["interactionMethod"] = target.InteractionMethod,
            ["confirmationMethod"] = "mcp-form-elicitation",
            ["hostInputIsolated"] = true
        };

    private static string SerializeManualAction(string instruction, string reason = "platform-automation-unavailable")
        => Serialize(new JsonObject
        {
            ["detected"] = false,
            ["canRespond"] = false,
            ["userActionRequired"] = true,
            ["reason"] = reason,
            ["instruction"] = instruction
        });

    private static string SerializeResponseManualAction(string instruction, string reason)
        => Serialize(new JsonObject
        {
            ["success"] = false,
            ["userActionRequired"] = true,
            ["reason"] = reason,
            ["instruction"] = instruction
        });

    private static string SerializeExpiredPrompt()
        => Serialize(new JsonObject
        {
            ["success"] = false,
            ["userActionRequired"] = true,
            ["instruction"] = "The reviewed prompt expired or was already used. Detect it again and ask the user what to do."
        });

    private static string Serialize(JsonObject value)
        => CliJson.SerializeUntyped(value, indented: false);

    private static async Task ExpireDialogLeaseAsync(string promptId)
    {
        await Task.Delay(DialogLeaseLifetime).ConfigureAwait(false);
        if (DialogLeases.TryRemove(promptId, out var expired))
            expired.Dispose();
    }

    private static void PurgeExpiredDialogLeases()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in DialogLeases)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                if (DialogLeases.TryRemove(entry.Key, out var expired))
                    expired.Dispose();
            }
        }
    }

    private static void RemoveMatchingDialogLeases(string targetIdentity, string fingerprint)
    {
        foreach (var entry in DialogLeases)
        {
            if (entry.Value.Target.TargetIdentity.Equals(targetIdentity, StringComparison.Ordinal)
                && entry.Value.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
            {
                if (DialogLeases.TryRemove(entry.Key, out var replaced))
                    replaced.Dispose();
            }
        }
    }

    private static void RemoveTargetDialogLeases(string targetIdentity)
    {
        foreach (var entry in DialogLeases)
        {
            if (entry.Value.Target.TargetIdentity.Equals(targetIdentity, StringComparison.Ordinal))
            {
                if (DialogLeases.TryRemove(entry.Key, out var sibling))
                    sibling.Dispose();
            }
        }
    }

    private enum DialogPlatformKind
    {
        Unsupported,
        MacOS,
        Android,
        IosSimulator,
        Windows,
        Linux
    }

    private sealed record DialogTarget(
        DialogPlatformKind Kind,
        string Platform,
        string TargetIdentity,
        string InteractionMethod,
        int? ProcessId,
        string? AppName,
        string? PackageId,
        string? AndroidDevice,
        string? SimulatorUdid,
        string AgentBaseUrl,
        string? UnsupportedReason = null)
    {
        public bool CanDetect => Kind != DialogPlatformKind.Unsupported;

        public static DialogTarget Unsupported(string platform, string identity, string reason)
            => new(DialogPlatformKind.Unsupported, platform, identity, "manual", null, null, null, null, null, string.Empty, reason);
    }

    private sealed class DialogLease(
        McpAgentSession owner,
        string? platformPromptId,
        DialogTarget target,
        string fingerprint,
        AlertInfo dialog,
        DateTimeOffset expiresAt,
        Action? cleanup) : IDisposable
    {
        public McpAgentSession Owner { get; } = owner;
        public string? PlatformPromptId { get; } = platformPromptId;
        public DialogTarget Target { get; } = target;
        public string Fingerprint { get; } = fingerprint;
        public AlertInfo Dialog { get; } = dialog;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public void Dispose() => cleanup?.Invoke();
    }
}
