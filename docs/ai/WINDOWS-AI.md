# Windows AI chat

`WindowsAIChatClient` implements `Microsoft.Extensions.AI.IChatClient` using
the on-device Windows App SDK `LanguageModel`. It supports text conversations,
streaming, and schema-constrained JSON. Windows selects the installed model.

## Requirements

- Windows 11 24H2 (build 26100) or later on a supported Copilot+ PC.
- An MSIX-packaged app with the `systemAIModels` capability.
- For the stable Phi Silica runtime, Microsoft's
  [Limited Access Feature approval and unlock token](https://learn.microsoft.com/windows/ai/apis/phi-silica).

Use the [Playground manifest](../../samples/AIExtensions.Sample.ChatPlayground/Platforms/Windows/Package.appxmanifest)
as a packaging example. Keep `WindowsPackageType` packaged and preserve the
manifest's target-family versions with
`AppxOSMinVersionReplaceManifestVersion=false` and
`AppxOSMaxVersionTestedReplaceManifestVersion=false`.
Build Windows targets on Windows; the SDK's packaging tools cannot run on macOS.

## Library and development-app dependencies

The library directly references only the stable AI package. The Windows sample
and device-test apps override only the umbrella SDK; NuGet resolves its newer AI,
Foundation, and InteractiveExperiences dependencies automatically:

| Package | Library | Windows samples and device tests |
|---------|---------|-----------------------------|
| `Microsoft.WindowsAppSDK` | Not referenced | `2.5.4-experimental` |
| `Microsoft.WindowsAppSDK.AI` | `2.4.4` | `2.5.4-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.Foundation` | `2.3.9` (transitive) | `2.3.13-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.InteractiveExperiences` | `2.1.3` (transitive) | `2.1.10-experimental` (transitive) |

The stable AI baseline supports every chat API used by the library and lets
development apps select the experimental package without a NuGet downgrade.
Select a compatible umbrella SDK explicitly in the Windows app: MAUI 10's
default 1.8 SDK has exact dependencies that conflict with these newer AI packages.
All three Windows app heads select `2.5.4-experimental`. The library does not pin
the SDK globally.

The Playground and device tests bundle the native runtime with `SelfContained`
and `WindowsAppSDKSelfContained`; BuildTools.WinApp enables their packaged
`dotnet run`. The older Essentials AI sample only selects the compatible SDK
so it compiles. Its unpackaged runtime setup, cloud services, and tool-based
workflows are unchanged; native Windows AI integration is deferred.
These sample settings are not a production access grant.

## Behavior

Model setup is lazy: the first request checks readiness and downloads the
model if needed. Unsupported hardware, missing capabilities, and setup
failures surface as errors; the client does not switch providers.

The client creates a fresh native context for each response, serializes the
supplied conversation history, and uses the leading system instruction as
context. It maps temperature, TopK, and TopP to native options.
The response model ID, `windows-ai-language-model`, identifies the API, not
particular model weights.
Native policy, moderation, language, and generation failures are reported as
exceptions rather than successful empty responses.

For `ChatResponseFormatJson` with a schema, the client calls
`GenerateStructuredJsonResponseAsync`. It requires every declared property,
including nested properties, and disallows additional properties. The system
instruction is included in the prompt because this API has no context overload.
A JSON format without a schema uses ordinary generation.

Streaming forwards native progress. If the native API returns only a completed
result, the client emits that result rather than inventing partial updates.
The Windows API documents partial JSON progress, but the current runtime has
also returned long, valid JSON with no progress callbacks. This is tracked in
[WindowsAppSDK #6872](https://github.com/microsoft/WindowsAppSDK/issues/6872);
it is not an intentional UI buffer or simulated streaming.

## Limitations

- No image input, native tool calling, image generation, or Windows embeddings.
- `MaxOutputTokens` is checked for a positive value but is not a native output limit.
- Prompts exceeding the context window fail; history is not silently truncated.
- Native generation can stall. Cancellation releases the waiting caller but
  does not guarantee recovery of the native model. See
  [WindowsAppSDK #6871](https://github.com/microsoft/WindowsAppSDK/issues/6871).

## Device tests

The device-test project uses DeviceRunners with `dotnet test`. For example,
run the schema transformation tests without making model requests:

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~WindowsAIChatClientJsonSchemaTests.GetConstraintSchema" --logger trx
```

Model-dependent cases are tagged `RequiresModel=true`. To exclude them, pass
`--filter "RequiresModel!=true"`. The streaming/schema cases verify valid
assembled responses; they do not guarantee multiple native progress callbacks.
For interactive testing and setup, see the
[Chat Playground README](../../samples/AIExtensions.Sample.ChatPlayground/README.md).
