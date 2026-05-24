---
applyTo: "src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/**"
---

# MCP Tool Development Guide

## Adding a New MCP Tool

### Step 1: Create the Tool File

Create `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/MyNewTool.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class MyNewTool
{
    [McpServerTool(Name = "maui_my_action"),
     Description("Clear, complete description of what this tool does. Mention when an AI agent should use it.")]
    public static async Task<string> MyAction(
        McpAgentSession session,
        [Description("Describe what this parameter controls")] string requiredParam,
        [Description("Describe this optional parameter and its default behavior")] bool optionalFlag = false,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        // Call agent.* methods to interact with the running app
        var result = await agent.SomeMethodAsync(requiredParam);
        return result ?? "No result";
    }
}
```

### Step 2: Register the Tool

Add to `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/McpServerHost.cs`:

```csharp
.WithTools<MyNewTool>()
```

### Step 3: Add the Corresponding AgentClient Method (if needed)

If the tool calls a new agent endpoint, add the client method in `Microsoft.Maui.DevFlow.Driver/AgentClient.cs`.

## Naming Conventions

- Tool names: `maui_` prefix + snake_case action: `maui_screenshot`, `maui_tap`, `maui_network`
- Class names: PascalCase with a `Tool` or `Tools` suffix: `ScreenshotTool`, `InteractionTools`, `NetworkTool`
- Use plural `*Tools` when grouping multiple related actions in one file (e.g., `InteractionTools` has tap, fill, clear)
- One file can contain multiple related tools (e.g., `InteractionTools` has tap, fill, clear)

## Parameter Rules

- **Every parameter must have `[Description]`** — AI agents only see the description to decide how to use the tool
- Descriptions should explain: what the parameter does, valid values, default behavior
- `McpAgentSession session` is always the first parameter (injected by the MCP framework)
- `int? agentPort = null` should be the second parameter for most tools (enables multi-app scenarios)
- Use nullable types with defaults for optional parameters

## Return Types

- `Task<string>` — simple text result (most tools)
- `Task<ContentBlock[]>` — when returning images (screenshot tools)
- Throw `McpException` for errors that should be reported to the AI agent

## Existing Tools Reference

| File | Tools | Pattern |
|------|-------|---------|
| `AgentTools.cs` | `maui_list_agents`, `maui_select_agent`, `maui_wait`, `maui_status`, `maui_capabilities` | Agent discovery |
| `AssertTool.cs` | `maui_assert` | Property assertions |
| `BatchTools.cs` | `maui_batch` | Batch actions |
| `CdpTools.cs` | `maui_cdp_evaluate`, `maui_cdp_screenshot`, `maui_cdp_source`, `maui_cdp_webviews` | Blazor WebView CDP |
| `FileTools.cs` | `maui_storage_roots`, `maui_files_list`, `maui_files_download`, `maui_files_upload`, `maui_files_delete` | File storage |
| `InteractionTools.cs` | `maui_tap`, `maui_fill`, `maui_clear`, `maui_key`, `maui_gesture`, `maui_scroll` | User interactions |
| `InvokeTools.cs` | `maui_list_actions`, `maui_invoke_action` | DevFlow Actions |
| `JobTools.cs` | `maui_jobs_list`, `maui_jobs_run` | Background jobs |
| `LogsTool.cs` | `maui_logs` | Log retrieval |
| `NavigationTools.cs` | `maui_navigate`, `maui_back`, `maui_focus`, `maui_resize` | Navigation & window |
| `NetworkTool.cs` | `maui_network`, `maui_network_detail`, `maui_network_clear` | Network inspection |
| `PlatformTools.cs` | `maui_app_info`, `maui_device_info`, `maui_display_info`, `maui_battery_info`, `maui_connectivity`, `maui_geolocation` | Device/app info |
| `PreferencesTools.cs` | `maui_preferences_list`, `maui_preferences_get`, `maui_preferences_set`, `maui_preferences_delete`, `maui_preferences_clear`, `maui_secure_storage_get`, `maui_secure_storage_set`, `maui_secure_storage_delete`, `maui_secure_storage_clear` | Storage |
| `PropertyTools.cs` | `maui_get_property`, `maui_set_property` | Property read/write |
| `QueryTools.cs` | `maui_query`, `maui_query_css`, `maui_element`, `maui_hittest` | Element search |
| `RecordingTools.cs` | `maui_recording_start`, `maui_recording_stop`, `maui_recording_status` | Screen recording |
| `ScreenshotTool.cs` | `maui_screenshot` | Image capture |
| `SensorTools.cs` | `maui_sensors_list`, `maui_sensors_start`, `maui_sensors_stop` | Device sensors |
| `TreeTool.cs` | `maui_tree` | Visual tree inspection |
