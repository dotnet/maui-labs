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

The library references stable AI. Windows sample/test apps select the experimental
umbrella SDK, which supplies matching AI, Foundation, and InteractiveExperiences
dependencies:

| Package | Library | Windows samples and device tests |
|---------|---------|-----------------------------|
| `Microsoft.WindowsAppSDK` | Not referenced | `2.5.4-experimental` |
| `Microsoft.WindowsAppSDK.AI` | `2.4.4` | `2.5.4-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.Foundation` | `2.3.9` (transitive) | `2.3.13-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.InteractiveExperiences` | `2.1.3` (transitive) | `2.1.10-experimental` (transitive) |

The library baseline supports its chat APIs without preventing experimental app
overrides. All three Windows app heads select SDK `2.5.4-experimental` explicitly:
MAUI 10's implicit 1.8 graph conflicts with the newer AI packages.
There is no global SDK pin.

The Playground and device tests bundle the native runtime with `SelfContained`
and `WindowsAppSDKSelfContained`; BuildTools.WinApp enables their packaged
`dotnet run`. The older Essentials AI sample has only a compile-compatibility
update; its unpackaged setup and cloud/tool workflows are unchanged.
Experimental sample settings are not a production access grant.

## Behavior

The first request checks readiness and downloads the model if needed.
Unsupported hardware, missing capabilities, and setup failures are errors;
the client does not switch providers.

Text requests use a fresh native context, serialized conversation history,
and the leading system instruction. Temperature, TopK, and TopP map to native options.
The response model ID, `windows-ai-language-model`, identifies the API, not
particular model weights.
Native policy, moderation, language, and generation failures throw exceptions.

For `ChatResponseFormatJson` with a schema, the client calls
`GenerateStructuredJsonResponseAsync`. The native schema requires all declared
fields, including nested fields, and no additional properties; the caller's
schema is unchanged. System instructions are included in the prompt because
this API has no context overload.
A JSON format without a schema uses ordinary generation.

Streaming forwards native progress, or the final result if no progress arrives.
The API documents partial JSON output, but long successful responses have
returned no callbacks: [WindowsAppSDK #6872](https://github.com/microsoft/WindowsAppSDK/issues/6872).

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
