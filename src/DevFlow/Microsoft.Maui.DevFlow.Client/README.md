# Microsoft.Maui.DevFlow.Client

Portable client for the [Microsoft.Maui.DevFlow](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow)
agent wire protocol: `AgentClient`, the element and protocol DTOs, and their serialization.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

The package targets `netstandard2.0` as well as modern .NET, so a .NET Framework test harness (for
example Visual Studio Apex tests) drives a running app through exactly the same protocol code as a
.NET 10 `dotnet watch` or VS Code test run. The DTOs are defined once, here, so the harnesses cannot
drift in how they serialize or interpret the protocol.

Use [`Microsoft.Maui.DevFlow.Driver`](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Driver/)
instead if you also need platform driver functionality — app process management, UI Automation,
screenshot processing, and recording. It builds on this package and re-exports its types.

## Install

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Client" Version="0.1.0-preview" />
```

## Quick start

The app under test must be running the DevFlow agent (see `Microsoft.Maui.DevFlow.Agent` or
`Microsoft.Maui.DevFlow.Agent.Native`).

```csharp
using Microsoft.Maui.DevFlow.Driver;

using var client = new AgentClient("localhost", 9223);

var status = await client.GetStatusAsync();
Console.WriteLine($"{status?.AppName} on {status?.Platform}");

// Find an element and interact with it
var buttons = await client.QueryAsync(type: "Button", automationId: "SubmitButton");
await client.TapAsync(buttons[0].Id);

await client.FillAsync("NameEntry", "Ada Lovelace");

// Inspect the visual tree
var tree = await client.GetTreeAsync(maxDepth: 3);
```

## Platform identity

Agents report the platform spelling native to their stack — `iOS`, `MacCatalyst`, `WinUI`,
`Tizen`. `DevFlowPlatform` maps those onto canonical identifiers (`ios`, `maccatalyst`,
`windows`, `tizen`, …) so client code compares one value instead of a family of spellings:

```csharp
var status = await client.GetStatusAsync();

if (status?.PlatformId == DevFlowPlatform.Tizen)
{
    // status.Platform is still exactly what the agent reported ("Tizen").
}
```

`DevFlowPlatform.Normalize` passes an identifier it does not recognize through unchanged rather
than coercing it onto another platform, so this client keeps working against an agent on a newer
platform. Use `DevFlowPlatform.IsKnown` to tell the two cases apart, and
`DevFlowPlatform.GetDisplayName` for user-facing output.

Agents that live outside this repository — such as the Tizen backend in
[Redth/Maui.Tizen](https://github.com/Redth/Maui.Tizen) — are first-class here: see
[Platform identity](https://github.com/dotnet/maui-labs/blob/main/docs/DevFlow/spec/README.md#platform-identity)
in the protocol spec.

## Requirements

- A consumer targeting `netstandard2.0` or later (including .NET Framework 4.6.2+)
- An app running a DevFlow agent, reachable over HTTP

## Links

- [DevFlow documentation](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow)
- [dotnet/maui-labs](https://github.com/dotnet/maui-labs)
