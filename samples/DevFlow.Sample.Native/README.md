# DevFlow native samples

Four plain .NET app heads — **no MAUI reference anywhere** — that host the DevFlow agent through
[`Microsoft.Maui.DevFlow.Agent.Native`](../../src/DevFlow/Microsoft.Maui.DevFlow.Agent.Native).

| Head | Target | UI framework |
|---|---|---|
| [`Android`](Android) | `net10.0-android` | `Android.Widget` views |
| [`iOS`](iOS) | `net10.0-ios` | UIKit |
| [`MacCatalyst`](MacCatalyst) | `net10.0-maccatalyst` | UIKit |
| [`MacOS`](MacOS) | `net10.0-macos` | AppKit |

## Layout

```
DevFlow.Sample.Native/
  Shared/SampleModel.cs        behaviour shared by all four heads
  Apple/                       UIKit head, linked into both iOS and MacCatalyst
  Android/MainActivity.cs      Android.Widget head
  MacOS/AppDelegate.cs         AppKit head
```

`Shared/` and `Apple/` are linked into the heads with `<Compile Include="..\Shared\**\*.cs" />`,
so behaviour lives in exactly one place while each head stays an idiomatic plain .NET app.

## Starting the agent

The agent never starts itself — each head bootstraps it explicitly.

```csharp
// Android — MainActivity.OnCreate
this.StartDevFlowAgent();

// iOS / Mac Catalyst — AppDelegate.FinishedLaunching
// macOS — AppDelegate.DidFinishLaunching
DevFlowAgent.Start();
```

## Shared automation ids

Every head builds the same logical screen using the same identifiers as
[`samples/DevFlow.Sample`](../DevFlow.Sample), so integration assertions are shared:

`HeaderLabel`, `CountLabel`, `StatusLabel`, `NewTodoEntry`, `NewDescriptionEntry`, `AddButton`,
`TodoList`, `TodoCheckBox`, `DeleteButton`, `TestButton`, `TestSwitch`, `GetPostsButton`.

Ids come from the platform's own identity mechanism — `View.Tag` on Android,
`AccessibilityIdentifier` on UIKit, `NSView.Identifier` on AppKit — which is what the matching
DevFlow backend reads first.

`GetPostsButton` issues a real outbound `HttpClient` request so network capture has traffic to
record.

## Running

```bash
dotnet build samples/DevFlow.Sample.Native/Android/DevFlow.Sample.Native.Android.csproj
dotnet build samples/DevFlow.Sample.Native/iOS/DevFlow.Sample.Native.iOS.csproj
dotnet build samples/DevFlow.Sample.Native/MacCatalyst/DevFlow.Sample.Native.MacCatalyst.csproj
dotnet build samples/DevFlow.Sample.Native/MacOS/DevFlow.Sample.Native.MacOS.csproj
```

Then connect with the CLI:

```bash
maui devflow agents
maui devflow tree
```

## What degrades

The native agent implements the UI surface (tree, query, hit-test, tap/fill/clear/focus/scroll,
get/set property, screenshot) plus everything already framework-neutral (logs, network, profiler,
actions, extensions). Device, storage, sensor, theme and background-job endpoints answer `501` with
`{ "error": "not_supported", "capability": …, "reason": … }` until the optional Essentials add-on
is referenced.
