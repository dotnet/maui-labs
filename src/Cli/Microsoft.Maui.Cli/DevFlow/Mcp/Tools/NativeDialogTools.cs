using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class NativeDialogTools
{
    [McpServerTool(Name = "maui_native_dialog_detect"), Description(
        "Detect an app-owned or macOS system permission dialog using Accessibility APIs without changing focus or sending input. " +
        "If a dialog is returned, pause and ask the user which exact button to press before calling maui_native_dialog_respond.")]
    public static async Task<string> Detect(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent is connected)")] int? agentPort = null)
    {
        using var driver = await CreateMacDriverAsync(session, agentPort);
        if (!driver.IsAccessibilityAuthorized())
        {
            return Serialize(new JsonObject
            {
                ["detected"] = false,
                ["canRespond"] = false,
                ["userActionRequired"] = true,
                ["reason"] = "accessibility-permission-required",
                ["instruction"] = "Pause and ask the user to grant macOS Accessibility access to the terminal or host running maui. Do not use mouse, keyboard, focus, osascript, or CGEvent fallbacks."
            });
        }

        var dialog = await driver.DetectAlertAsync();
        if (dialog is null)
        {
            return Serialize(new JsonObject
            {
                ["detected"] = false,
                ["canRespond"] = false,
                ["userActionRequired"] = false
            });
        }

        var result = CreateDialogJson(dialog);
        result["detected"] = true;
        result["userActionRequired"] = true;
        result["instruction"] = dialog.CanRespond
            ? "Pause and ask the user which exact button to press. After they answer, call maui_native_dialog_respond with this promptId, the exact button label, and confirmedByUser=true."
            : "This system dialog could not be safely attributed to the connected app. Pause and ask the user to respond manually. Do not call maui_native_dialog_respond.";
        return Serialize(result);
    }

    [McpServerTool(Name = "maui_native_dialog_respond"), Description(
        "Invoke AXPress on an exact button of the exact native dialog returned by maui_native_dialog_detect. " +
        "Call only after the user explicitly confirms the button choice. Never infer consent for permission prompts.")]
    public static async Task<string> Respond(
        McpAgentSession session,
        [Description("Prompt fingerprint returned by maui_native_dialog_detect")] string promptId,
        [Description("Exact visible button label chosen by the user, such as Allow or Don't Allow")] string buttonLabel,
        [Description("Must be true only after the user explicitly confirmed this exact button choice")] bool confirmedByUser,
        [Description("Agent HTTP port (optional if only one agent is connected)")] int? agentPort = null)
    {
        if (!confirmedByUser)
        {
            return Serialize(new JsonObject
            {
                ["success"] = false,
                ["userActionRequired"] = true,
                ["instruction"] = "Pause and ask the user to confirm the exact button. Do not press a permission button without confirmation."
            });
        }

        using var driver = await CreateMacDriverAsync(session, agentPort);
        if (!driver.IsAccessibilityAuthorized())
        {
            return Serialize(new JsonObject
            {
                ["success"] = false,
                ["userActionRequired"] = true,
                ["reason"] = "accessibility-permission-required",
                ["instruction"] = "Pause and ask the user to grant macOS Accessibility access to the terminal or host running maui."
            });
        }

        var action = await driver.PressAlertButtonAsync(promptId, buttonLabel);
        var result = new JsonObject
        {
            ["success"] = action.Success,
            ["userActionRequired"] = action.UserActionRequired,
            ["message"] = action.Message
        };
        if (action.Dialog is not null)
            result["dialog"] = CreateDialogJson(action.Dialog);
        return Serialize(result);
    }

    private static JsonObject CreateDialogJson(AlertInfo dialog)
        => new()
        {
            ["promptId"] = dialog.PromptId,
            ["title"] = dialog.Title,
            ["text"] = new JsonArray(dialog.Text.Select(text => (JsonNode?)text).ToArray()),
            ["buttons"] = new JsonArray(dialog.Buttons.Select(button => (JsonNode?)button.Label).ToArray()),
            ["sourceProcessId"] = dialog.SourceProcessId,
            ["sourceProcessName"] = dialog.SourceProcessName,
            ["isSystemDialog"] = dialog.IsSystemDialog,
            ["canRespond"] = dialog.CanRespond
        };

    private static string Serialize(JsonObject value)
        => CliJson.SerializeUntyped(value, indented: false);

    private static async Task<MacCatalystAppDriver> CreateMacDriverAsync(
        McpAgentSession session,
        int? agentPort)
    {
        if (!OperatingSystem.IsMacOS())
            throw new McpException("Native macOS dialog interaction is only available on macOS.");

        using var agent = await session.GetAgentClientAsync(agentPort);
        var status = await agent.GetStatusAsync()
            ?? throw new McpException("Unable to read agent status.");
        var platform = status.Platform ?? string.Empty;
        if (!platform.Contains("mac", StringComparison.OrdinalIgnoreCase))
            throw new McpException($"Native macOS dialog interaction is unavailable for platform '{platform}'.");

        var processId = status.App?.ProcessId ?? FindProcessId(status.AppName);
        if (!processId.HasValue)
            throw new McpException("Unable to determine the connected macOS app process.");

        return new MacCatalystAppDriver
        {
            ProcessId = processId,
            AppName = status.App?.Name ?? status.AppName
        };
    }

    private static int? FindProcessId(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return null;

        var candidates = new List<(int ProcessId, string ProcessName)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                        process.ProcessName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add((process.Id, process.ProcessName));
                    }
                }
                catch
                {
                    // A process may exit or deny metadata access while enumerating.
                }
            }
        }

        var exactMatches = candidates
            .Where(candidate => candidate.ProcessName.Equals(appName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactMatches.Length == 1)
            return exactMatches[0].ProcessId;

        return candidates.Count == 1 ? candidates[0].ProcessId : null;
    }
}
