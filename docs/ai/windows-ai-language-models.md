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

The default request path can also make the model ready on first use. Native
ready states distinguish unsupported systems, user-disabled AI, missing package
capability, incompatible hardware, required OS updates, and a model that is
installed but not ready. Setup and download failures surface with their native
error details.

## Native API characteristics

- The native text API has one system-instruction string.
- Each text request uses a fresh `LanguageModelContext`.
- Schema-constrained output uses
  `GenerateStructuredJsonResponseAsync`; that API has no context overload.
- The current native model does not accept image input or register managed
  tools.
- Tool calls and results can still be replayed as labelled text history.
- Native token usage metadata is not exposed, so `TokenUsage` is not advertised.
- Native streaming progress is forwarded as append-only MEAI updates.

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
  does not guarantee native-model recovery:
  [WindowsAppSDK #6871](https://github.com/microsoft/WindowsAppSDK/issues/6871).
- Successful structured responses may arrive only as a final result without
  progress callbacks:
  [WindowsAppSDK #6872](https://github.com/microsoft/WindowsAppSDK/issues/6872).

Build and package Windows targets on Windows; the Windows packaging toolchain
does not run on macOS.
