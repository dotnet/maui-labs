# Windows Copilot Runtime (Phi Silica) in Microsoft.Maui.Essentials.AI

How the Windows on-device AI models are mapped onto the `Microsoft.Extensions.AI` abstractions,
and which parts of the Windows App SDK surface are used to do it.

## Windows App SDK version

The Windows targets pin `Microsoft.WindowsAppSDK` to **2.2.2-experimental9**
(`MicrosoftWindowsAppSDKVersion` in `eng/Versions.props`). This is deliberate and constrained:

| | 2.3.1 (latest stable) | 2.2.2-experimental9 (used here) |
|---|---|---|
| `Microsoft.WindowsAppSDK.AI` | 2.3.4 | 2.2.6-experimental |
| Structured JSON output | `LanguageModel` | `LanguageModelExperimental` |
| `ImageGenerator` (text to image) | not present | present |
| `Microsoft.Windows.AI.Speech` | not present | present |
| `AppContentIndexer` semantic search | unavailable | available |

`Microsoft.WindowsAppSDK.Search`, which contains `AppContentIndexer`, declares an **exact** dependency
on `Microsoft.WindowsAppSDK.AI`. The only umbrella version whose AI package satisfies
`Microsoft.WindowsAppSDK.Search 2.2.6-experimental` is `2.2.2-experimental9`. Combining the search
package with the stable 2.3.x line produces `NU1608` and is not supported.

`Microsoft.WindowsAppSDK.Search` is only published on nuget.org, which is why `nuget.org` is listed as
a source in `NuGet.config`.

Because the SDK line is experimental, Windows projects set `SelfContained` and
`WindowsAppSDKSelfContained` so the runtime is bundled into the MSIX rather than resolved from a
framework package.

## Structured output

`PhiSilicaChatClient` honours `ChatOptions.ResponseFormat`. When a `ChatResponseFormatJson` carries a
schema, the request is routed to `LanguageModelExperimental.GenerateStructuredJsonResponseAsync`,
which constrains generation at the runtime level. Nothing is scraped out of free-form text and there
is no code-fence stripping.

Two consequences of the WinRT shape are worth knowing:

- There is no `LanguageModelContext` overload for structured generation, so the system prompt is
  prepended to the prompt text instead of being supplied as context.
- The API is on `LanguageModelExperimental`, not `LanguageModel`. Constructing it raises `CS8305`,
  which is acknowledged with a scoped `#pragma` at the call site.

Requests without a schema continue to use `LanguageModel.GenerateResponseAsync` with a real context.

## Tool calling

Windows App SDK exposes no function-calling API, so `PhiSilicaToolCallingClient`
(`samples/EssentialsAISample/Services/`) provides it as an `IChatClient` middleware.

It describes the available tools in the system prompt, then constrains the reply with a tool-call
JSON schema handed to the model through `ChatOptions.ResponseFormat` — the same native structured
output path described above. The reply is parsed into `FunctionCallContent` so the standard
`UseFunctionInvocation()` middleware can execute the call.

```jsonc
{ "type": "tool_call", "tool_name": "get_weather", "arguments": { "city": "Paris" }, "more_steps": false }
{ "type": "text", "text": "..." }
{ "type": "response", "response": { } }   // when the caller also supplied a schema
```

Because the schema is enforced by the runtime, the middleware only handles orchestration:

- **Enum narrowing.** `tool_name` is an `enum` of the actual tool names, so the model cannot invent one.
- **Chaining.** `more_steps` lets the model signal that another call is needed. On the follow-up round
  the schema drops the text escape hatch and already-called tools are removed from the enum, which
  prevents the model from looping on the same tool.
- **Streaming.** A partial tool call is not actionable, so requests with tools buffer the response and
  emit it once.

## Image generation

`PhiSilicaImageGenerator` implements `IImageGenerator` over `Microsoft.Windows.AI.Imaging.ImageGenerator`.
The number of images in `ImageGenerationRequest.OriginalImages` selects the operation:

| Images | Windows API | Behaviour |
|---|---|---|
| none | `GenerateImageFromTextPrompt` | text to image |
| one | `GenerateImageFromImageBuffer` | image to image, guided by the prompt |
| two | `GenerateImageFromImageBufferAndMask` | inpainting, second image is the mask |

`Creativity`, `MaxInferenceSteps` and `Seed` are read from `ImageGenerationOptions.AdditionalProperties`.
When several images are requested the seed is offset per image so the results differ.

`ImageGenerationOptions.ImageSize` and `ImageGenerationResponseFormat.Uri` throw — the model chooses
its own output size, and generation is on-device so there is no hosted URI to return.

The sample wires this into chat with `ChatClientBuilder.UseImageGeneration(...)`, so a
`HostedImageGenerationTool` in `ChatOptions.Tools` is handled automatically and asking the model to
draw something returns a real image inline.

## Image input

Phi Silica is text-only, so images cannot be passed to it the way a cloud multimodal model accepts
them. Instead `PhiSilicaChatClient` runs any image `DataContent` through the on-device
`ImageDescriptionGenerator` and splices the resulting caption into the prompt in place of the image:

```text
User: [Image: A photograph of a bridge over a river at dusk...]
User: What time of day was this taken?
```

The description model is created lazily, only when a request actually carries an image. Everything
runs locally; nothing is uploaded.

## Semantic search

`AppContentIndexerSearchService` in the sample implements `ISemanticSearchService` on
`Microsoft.Windows.Search.AppContentIndex.AppContentIndexer`. The OS owns embedding, chunking and
ranking, and the index is per-app and persistent.

This is not exposed as an `IEmbeddingGenerator` because the indexer never returns vectors — it is a
closed hybrid semantic and lexical index.

`LanguageModel.GenerateEmbeddingVectors` is **not** a substitute. It returns a list of
`EmbeddingVector` per prompt whose counterpart is `GenerateResponseFromEmbeddingsAsync`; these are
prompt and token embeddings used for soft-prompting, not pooled vectors suitable for similarity
search.

## Packaging requirements

Phi Silica requires the `systemAIModels` capability, which is only granted to packaged apps. Running
unpackaged makes `LanguageModel.GetReadyState()` return `AccessDenied`, so `WindowsPackageType=None`
must not be set. `Microsoft.Windows.SDK.BuildTools.WinApp` is referenced so `dotnet run` registers
the loose MSIX layout and activates the app by AUMID.

`AppxOSMinVersionReplaceManifestVersion` and `AppxOSMaxVersionTestedReplaceManifestVersion` are set to
`false` so MSBuild does not overwrite the `MaxVersionTested` needed for the capability.

The APIs require Windows 10.0.26100.0 at runtime (`[SupportedOSPlatform]`), while the target framework
stays at `19041` for build compatibility.

## Not available

- **Model identity.** `LanguageModel` exposes no name, version or capability metadata, so behaviour
  cannot be varied by model.
- **Semantic embeddings.** No `IEmbeddingGenerator` implementation; see above.
- **Speech.** `Microsoft.Windows.AI.Speech` is present in this SDK line and would support
  `ISpeechToTextClient`, but is not wired up yet.
