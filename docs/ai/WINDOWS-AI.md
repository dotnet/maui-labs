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

The shipping library uses stable APIs. The Playground and device-test apps
explicitly select an experimental runtime for development:

| Package | Library | Playground and device tests |
|---------|---------|-----------------------------|
| `Microsoft.WindowsAppSDK` | Not referenced | `2.5.4-experimental` |
| `Microsoft.WindowsAppSDK.AI` | `2.4.4` | `2.5.4-experimental` |
| `Microsoft.WindowsAppSDK.Foundation` | `2.3.12` | `2.3.13-experimental` |

The stable AI baseline supports every chat API used by the library and lets
development apps select the experimental package without a NuGet downgrade.
The apps bundle the native runtime with `SelfContained` and
`WindowsAppSDKSelfContained`; BuildTools.WinApp enables packaged `dotnet run`.
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

For `ChatResponseFormatJson` with a schema, the client calls
`GenerateStructuredJsonResponseAsync`. It requires every declared property,
including nested properties, and disallows additional properties. The system
instruction is included in the prompt because this API has no context overload.
A JSON format without a schema uses ordinary generation.

Streaming forwards native progress. If the native API returns only a completed
result, the client emits that result rather than inventing partial updates.
Structured JSON may therefore arrive all at once even when streaming is selected.

## Limitations

- No image input, native tool calling, image generation, or Windows embeddings.
- `MaxOutputTokens` is checked for a positive value but is not a native output limit.
- Prompts exceeding the context window fail; history is not silently truncated.
- Native generation can stall. Cancellation releases the waiting caller but
  does not guarantee recovery of the native model.

## Device tests

The device-test project uses DeviceRunners with `dotnet test`. For example,
run the schema transformation tests without making model requests:

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~WindowsAIChatClientJsonSchemaTests.GetConstraintSchema" --logger trx
```

The remaining Windows chat tests require an available on-device model.
For interactive testing and setup, see the
[Chat Playground README](../../samples/AIExtensions.Sample.ChatPlayground/README.md).
