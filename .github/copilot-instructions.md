# Copilot Instructions for maui-labs

These instructions guide GitHub Copilot and other AI code generation tools when working with the maui-labs repository.

## Platform-Specific Code

DevFlow targets multiple platforms via multi-targeting. The pattern:

- **`Microsoft.Maui.DevFlow.Agent.Core`** targets `net10.0` — all platform-agnostic code lives here.
- **`Microsoft.Maui.DevFlow.Agent`** targets `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-macos`, `net10.0-windows10.0.19041.0` — platform-specific overrides.
- **`Microsoft.Maui.DevFlow.Agent.Gtk`** targets `net10.0` — Linux/GTK-specific code.

Use `#if` directives for platform code in multi-targeting projects:

```csharp
#if IOS || MACCATALYST
    // iOS and Mac Catalyst share UIKit
    return uiWindow.Screen.Scale;
#elif ANDROID
    return activity.Resources?.DisplayMetrics?.Density ?? 1.0;
#elif WINDOWS
    return xamlRoot.RasterizationScale;
#elif MACOS
    return nsWindow.BackingScaleFactor;
#endif
```

**Important**: Both `.ios.cs` and `.maccatalyst.cs` files compile for Mac Catalyst. Use `#if IOS || MACCATALYST` when code applies to both.

## Agent Architecture (Core/Platform Pattern)

When adding new features to the DevFlow agent:

1. **Add the virtual method in `Agent.Core/DevFlowAgentService.cs`** — this is the platform-agnostic base
2. **Override in `Agent/DevFlowAgentService.cs`** with `#if` directives for platform-specific behavior
3. **Override in `Agent.Gtk/GtkAgentService.cs`** for Linux/GTK if needed

Example:
```csharp
// In Agent.Core/DevFlowAgentService.cs
protected virtual Task<byte[]?> CaptureFullScreenAsync() => Task.FromResult<byte[]?>(null);

// In Agent/DevFlowAgentService.cs
#if IOS || MACCATALYST
protected override Task<byte[]?> CaptureFullScreenAsync()
    => DispatchAsync(() => CaptureAllWindowsComposited());
#elif MACOS
protected override async Task<byte[]?> CaptureFullScreenAsync() { /* AppKit capture */ }
#endif
```

## MCP Tool Conventions

MCP tools live in `src/DevFlow/Microsoft.Maui.DevFlow.CLI/Mcp/Tools/`. When creating a new tool:

1. Create a new file in the `Tools/` directory
2. Use `[McpServerToolType]` on the class, `[McpServerTool]` on the method
3. **Every parameter must have a `[Description]`** — this is what AI agents see
4. Tool names use the `maui_` prefix and snake_case: `maui_screenshot`, `maui_tap`
5. First parameter is always `McpAgentSession session`
6. Use `session.GetAgentClientAsync(agentPort)` for agent resolution
7. **Register the tool** in `Mcp/McpServerHost.cs`: `.WithTools<YourTool>()`

```csharp
[McpServerToolType]
public sealed class MyNewTool
{
    [McpServerTool(Name = "maui_my_action"),
     Description("Clear description of what this tool does and when to use it.")]
    public static async Task<string> MyAction(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Describe this parameter clearly")] string requiredParam,
        [Description("Describe this optional parameter")] string? optionalParam = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        // Use agent.* methods
        return "Result";
    }
}
```

## HTTP Endpoint Conventions

Agent HTTP endpoints are defined in `DevFlowAgentService.cs` (in Agent.Core). When adding new endpoints:

1. Register the route in the `ConfigureRoutes()` method: `_server.MapGet("/api/myendpoint", HandleMyEndpoint);`
2. Implement the handler as `protected virtual async Task<HttpResponse> HandleMyEndpoint(HttpRequest request)`
3. For POST endpoints that accept JSON bodies, define a DTO class at the bottom of the file
4. **Add a corresponding method in `AgentClient`** (in `Microsoft.Maui.DevFlow.Driver`) — this is the public API

```csharp
// In DevFlowAgentService.cs — handler
protected virtual async Task<HttpResponse> HandleMyEndpoint(HttpRequest request) { ... }

// In AgentClient.cs — public API (this is what NuGet consumers use)
public async Task<MyResult?> MyEndpointAsync(string param) { ... }
```

## CLI Command Conventions

CLI commands use **System.CommandLine** in `Program.cs`:

- Use `Option<T>` for named parameters, `Argument<T>` for positional
- Support `--json` / `--no-json` output modes via `OutputWriter`
- Use `SetHandler` with `InvocationContext` for commands with many options
- Post-action flags: `--and-screenshot`, `--and-tree`, `--and-tree-depth` for verification after mutations

## Driver Library (AgentClient)

`Microsoft.Maui.DevFlow.Driver/AgentClient.cs` is the **public API for NuGet consumers**. Changes to method signatures are:

- **Binary breaking** — existing compiled code stops working
- **Source breaking** — existing source code fails to compile

The repo is at 0.1.0-preview so breaking changes are acceptable, but:
- Document breaking changes in the PR description
- Add the `breaking-change` label to the PR
- Update parameter defaults so existing callers keep working where possible (add new params with defaults at the end)

## Test Patterns

- **Framework**: xUnit v2.9.3
- **Naming**: `MethodName_Condition_ExpectedResult` or descriptive `[Fact]` names
- **Location**: `src/DevFlow/Microsoft.Maui.DevFlow.Tests/`
- **Coverage**: coverlet.collector
- **Approach**: DevFlow tests use real Agent.Core code — they instantiate actual services and test behavior

## Arcade SDK Gotchas

- **Never modify `eng/common/`** — auto-generated by Arcade SDK, overwritten by Dependency Flow
- **Central Package Management**: specify package versions **only** in `Directory.Packages.props`, not in `.csproj` files
- **`IsShipping` and `IsPackable`**: default `false` in `Directory.Build.props`. Shipped projects explicitly set `true`.
- **Signing**: configured in `eng/Signing.props`. New third-party DLLs need a `3PartySHA2` entry.
- **Version**: defined in `eng/Versions.props` (`VersionPrefix` + `VersionSuffix`). Per-product overrides in `src/{Product}/Version.props`.

## Maui.Client Conventions (Future — Not Yet Present)

A Client product (`src/Client/`) is planned but not yet present in this repository. When added, it will use a DI-based architecture with provider interfaces:

- `IAndroidProvider` — discovers and installs Android SDK components
- `IJdkManager` — manages JDK installations
- `IDeviceManager` — lists and manages Android emulators
- `IDoctorService` — runs environment health checks

Define an interface first, implement it, register in `Program.Services`.
