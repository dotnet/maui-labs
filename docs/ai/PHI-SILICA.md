# Windows Copilot Runtime (Phi Silica) in Microsoft.Maui.Essentials.AI

How the Windows on-device AI models are mapped onto the `Microsoft.Extensions.AI` abstractions,
and which parts of the Windows App SDK surface are used to do it.

## Windows App SDK version

The Windows targets pin `Microsoft.WindowsAppSDK` to **2.4.1-experimental**
(`MicrosoftWindowsAppSDKVersion` in `eng/Versions.props`):

| | 2.5.1 (stable) | 2.4.1-experimental (used here) |
|---|---|---|
| `Microsoft.WindowsAppSDK.AI` | 2.5.5 | 2.4.8-experimental |
| `Microsoft.WindowsAppSDK.Search` | 2.5.5 | 2.4.8-experimental |
| Structured JSON output | `LanguageModel` | `LanguageModel` |
| `ImageGenerator` (text to image) | not available | available |
| `AppContentIndexer` | limited-access features | experimental features |

The umbrella package brings in compatible AI and Search packages; referencing an older Search
package directly creates an exact-version conflict with its AI dependency. The stable SDK now
supports structured output, but image generation is still experimental, as are the general
content-indexing features used by the existing sample. This pin is therefore not suitable for a
Microsoft Store release. Use nuget.org for local development until these versions are mirrored
to the repository feeds.

See the [Windows App SDK release notes](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/)
for API promotion and experimental-channel limitations.

Because the SDK line is experimental, Windows projects set `SelfContained` and
`WindowsAppSDKSelfContained` so the runtime is bundled into the MSIX rather than resolved from a
framework package.

## Structured output

`PhiSilicaChatClient` honours `ChatOptions.ResponseFormat`. When a `ChatResponseFormatJson` carries a
schema, the request is routed to `LanguageModel.GenerateStructuredJsonResponseAsync`,
which constrains generation at the runtime level. Nothing is scraped out of free-form text and there
is no code-fence stripping.

Two consequences of the WinRT shape are worth knowing:

- There is no `LanguageModelContext` overload for structured generation, so the system prompt is
  prepended to the prompt text instead of being supplied as context.
- Structured output became a stable `LanguageModel` API in Windows App SDK 2.3.1, so no
  `LanguageModelExperimental` wrapper or experimental options conversion is required.
- The client handles both incremental progress and a completed-only response, whichever the
  installed Windows AI model reports.

Requests without a schema continue to use `LanguageModel.GenerateResponseAsync` with a real context.

### Closed schemas

`PhiSilicaChatClient` closes every schema with `additionalProperties: false`, applied recursively,
before constraining generation for any structured-output request.

Constrained decoding only forbids what the schema forbids, and schemas generated from a type by
`ChatResponseFormat.ForJsonSchema<T>()` are open — no `required`, no `additionalProperties`. Given an
open schema the model answers under a property name of its own choosing: asked to fill in a declared
`text` property it produced `body`, `response` and `message` instead. Those replies satisfy the
schema, so nothing fails and no status is reported, but the declared property is missing and
deserializing the result yields null. Closing the schema also measurably reduced
`ResponseInvalidJson` failures, since the decoder has less room to wander.

## Tool calling

The public Windows App SDK `LanguageModel` API does not expose function calling. This baseline
does not emulate it: `PhiSilicaChatClient` rejects nonempty `ChatOptions.Tools` or tool-only
options with `NotSupportedException`, rather than ignoring them or calling unrelated tools.
The playground hides tool controls when Phi Silica is selected; Azure and Apple retain their
own tool support. The experimental constrained-JSON tool adapter is deferred to a separate PR.
The standalone Images page still invokes the native on-device image generator directly.

## Context window

The context window is shared by the system prompt, the accumulated history and the new prompt, and
the API does not truncate automatically, so a long conversation can outgrow it.

`PhiSilicaChatClient.GetPromptFitAsync` reports this before a request is sent, wrapping
`LanguageModel.GetUsablePromptLength`, which returns the character index at which the prompt stops
fitting. When a request is sent anyway and the model reports `PromptLargerThanContext`, the client
throws `PhiSilicaContextWindowException` so it can be told apart from an ordinary failure. Recover by
trimming, summarizing the history, or starting a new conversation.

## Streaming

Text and structured JSON updates use the Windows AI progress callback and can arrive
incrementally without a tool-selection phase. The number of updates depends on the installed
model.

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

Both EssentialsAISample and the chat playground register the generator for direct
`IImageGenerator` use; the playground's standalone **Images** page invokes it. Phi Silica Chat
does not offer an image-generation tool, since the native language model cannot call tools.

The image model is prepared on first use, not when the chat client is created. Cancelling a request
while Windows prepares the model stops waiting for it; Windows may continue preparing the model in
the background. An unavailable model reports its readiness failure rather than a generated image.
When resuming an older chat containing image-generation tool activity, Phi Silica preserves that
activity as text in its prompt; the text-only model cannot inspect generated pixels without a
separate image-description request.

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

`AppContentIndexerSearchService` in EssentialsAISample implements `ISemanticSearchService` on
`Microsoft.Windows.Search.AppContentIndex.AppContentIndexer`. The OS owns embedding, chunking and
ranking, and the index is per-app and persistent.

This is not exposed as an `IEmbeddingGenerator` because the indexer never returns vectors — it is a
closed hybrid semantic and lexical index.

`LanguageModel.GenerateEmbeddingVectors` is **not** a substitute. It returns a list of
`EmbeddingVector` per prompt whose counterpart is `GenerateResponseFromEmbeddingsAsync`; these are
prompt and token embeddings used for soft-prompting, not pooled vectors suitable for similarity
search.

## Packaging requirements

Phi Silica requires the `systemAIModels` and `runFullTrust` capabilities in an MSIX package.
The playground manifest must target both `Windows.Universal` and `Windows.Desktop`:
targeting only `Windows.Desktop` caused `LanguageModel.GetReadyState()` to return
`CapabilityMissing` on a device where the test app (targeting both families) could generate
responses. Running unpackaged also prevents access, so `WindowsPackageType=None` must not be
set. `Microsoft.Windows.SDK.BuildTools.WinApp` is referenced so `dotnet run` registers
the loose MSIX layout and activates the app by AUMID.

`AppxOSMinVersionReplaceManifestVersion` and `AppxOSMaxVersionTestedReplaceManifestVersion` are set to
`false` so MSBuild does not overwrite the `MaxVersionTested` needed for the capability.

The APIs require Windows 10.0.26100.0 at runtime (`[SupportedOSPlatform]`), while the target framework
stays at `19041` for build compatibility.

## Not available

- **Model identity.** `LanguageModel` exposes no name, version or capability metadata, so behaviour
  cannot be varied by model.
- **Semantic embeddings.** No `IEmbeddingGenerator` implementation; see above.
- **Native tool calling.** The published `LanguageModel` API has no function-call interface.
