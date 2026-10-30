# Windows AI language models

`WindowsAIChatClient` implements `Microsoft.Extensions.AI.IChatClient` with the
on-device Windows App SDK `LanguageModel`. This document covers Windows
packaging, runtime preparation, dependencies, and validation. Shared message,
tool, structured-output, streaming, and error behavior is defined in the
[chat provider implementation guide](local-chat-provider.md).

## Requirements

- Windows 11 24H2 (build 26100) or later.
- Supported Copilot+ hardware.
- An MSIX-packaged application declaring the `systemAIModels` capability.
- A compatible Windows App SDK runtime.
- For stable Phi Silica access, Microsoft's
  [Limited Access Feature approval and unlock token](https://learn.microsoft.com/windows/ai/apis/phi-silica).

The provider never silently switches to a cloud model.

## Packaging

Use the [Chat Playground manifest](../../samples/AIExtensions.Sample.ChatPlayground/Platforms/Windows/Package.appxmanifest)
as a packaging example. Keep the app packaged and preserve the manifest target
family versions:

```xml
<PropertyGroup>
  <WindowsPackageType>MSIX</WindowsPackageType>
  <AppxOSMinVersionReplaceManifestVersion>false</AppxOSMinVersionReplaceManifestVersion>
  <AppxOSMaxVersionTestedReplaceManifestVersion>false</AppxOSMaxVersionTestedReplaceManifestVersion>
</PropertyGroup>
```

The Playground and device tests bundle the native runtime with
`SelfContained` and `WindowsAppSDKSelfContained`. `BuildTools.WinApp` enables
packaged `dotnet run`.

## Dependencies

The library references the stable AI package. Windows sample and device-test
apps select the experimental umbrella SDK so their AI, Foundation, and
InteractiveExperiences components remain aligned:

| Package | Library | Windows samples and device tests |
| --- | --- | --- |
| `Microsoft.WindowsAppSDK` | Not referenced | `2.5.4-experimental` |
| `Microsoft.WindowsAppSDK.AI` | `2.4.4` | `2.5.4-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.Foundation` | `2.3.9` (transitive) | `2.3.13-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.InteractiveExperiences` | `2.1.3` (transitive) | `2.1.10-experimental` (transitive) |

The explicit app SDK selection avoids conflicts with MAUI's implicit Windows
App SDK graph. Experimental sample settings are not a production access grant.

## Registration

Register the provider on a supported Windows runtime:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
{
    builder.Services.AddSingleton<IChatClient>(new WindowsAIChatClient());
}
```

## Model status and readiness

```csharp
var status = await WindowsAIChatClient.GetModelStatusAsync();
if (status.Availability == ChatModelAvailability.NotReady)
    await WindowsAIChatClient.EnsureReadyAsync(cancellationToken);
else if (status.Availability != ChatModelAvailability.Available)
    Console.WriteLine(status.UnavailableReason);
```

Ordinary generation never prepares or downloads the model. Every request reads
the native ready state and fails with that state unless it is `Ready`.
`EnsureReadyAsync` is the only preparation/download entry point; it awaits
setup, validates the native result, and requires the final state to be `Ready`.
Native
ready states distinguish unsupported systems, user-disabled AI, missing package
capability, incompatible hardware, required OS updates, and a model that is
installed but not ready. Setup and download failures surface with their native
error details.

The client creates a fresh owned model for every request. Concurrent requests do
not share native model instances, and a failed or canceled creation does not
affect later requests. Each owned model is disposed only after its native
operation terminates. Caller-supplied models are reused and never disposed by the
client, but requests still require native readiness.

## Native API characteristics

- The native text API has one system-instruction string.
- Each text request uses a fresh `LanguageModelContext`.
- Schema-constrained output uses
  `GenerateStructuredJsonResponseAsync`; that API has no context overload.
- The current native model does not accept image input or register managed
  tools.
- Auto and None tool modes are accepted by MEAI type, including independently
  constructed/deserialized instances. Required and named Required are rejected.
- Tool calls and results can still be replayed as labelled text history.
- Native token usage metadata is not exposed, so `TokenUsage` is not advertised.
- Native streaming progress is forwarded as append-only MEAI updates.
- The final response must equal or extend the emitted progress. Only a missing
  suffix is appended; incompatible final text fails explicitly.
- Cancellation stops the waiting caller promptly and requests native cancellation.
  Request contexts and model leases remain alive until the native operation's
  terminal completion callback, even if a stalled operation retains them.
- `ConversationId`, `ContinuationToken`, and explicit background execution are
  rejected. Supply conversation history as messages.
- System/instructions-only requests are rejected; at least one User or Assistant
  turn is required. Explicitly blank turns retain their role-labelled blank-message
  behavior, including turns containing only unknown custom content.
- Text and correctly placed function-call/result history are supported. Standard
  unsupported MEAI media/representations are rejected rather than silently dropped;
  malformed or empty recognized payloads are invalid. Unknown custom `AIContent`
  items may be omitted without losing supported neighboring content.
- JSON requires an explicit schema. Schema-free `ChatResponseFormat.Json` and
  `GetResponseAsync<T>(..., useJsonSchemaResponseFormat: false)` are rejected before
  native work. Ordinary text is not parsed, repaired, or stripped of Markdown fences.

The response model ID `windows-ai-language-model` identifies the stable
provider surface, not particular installed weights.

## Build and validation

Build the Windows implementation on Windows:

```powershell
dotnet build src\AI\Microsoft.Maui.Essentials.AI\Microsoft.Maui.Essentials.AI.csproj `
  -f net10.0-windows10.0.19041.0
```

Run model-free device-test cases:

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj `
  -f net10.0-windows10.0.19041.0 `
  --filter "RequiresModel!=true" `
  --logger trx
```

Model-dependent cases use `RequiresModel=true` and require packaged execution
on compatible hardware. The Chat Playground is the interactive validation host.

## Known native constraints

- Prompts exceeding the context window fail; history is not silently truncated.
- Native generation can stall. Cancellation releases the waiting caller but
  does not guarantee process-level native recovery; a subsequent request can
  also stall even though the client creates a fresh `LanguageModel`:
  [WindowsAppSDK #6871](https://github.com/microsoft/WindowsAppSDK/issues/6871).
- Successful structured responses may arrive only as a final result without
  progress callbacks:
  [WindowsAppSDK #6872](https://github.com/microsoft/WindowsAppSDK/issues/6872).

Build and package Windows targets on Windows; the Windows packaging toolchain
does not run on macOS.
