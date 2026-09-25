# Windows AI APIs in Microsoft.Maui.Essentials.AI

The Windows AI API family (`Microsoft.Windows.AI.*`) is part of Microsoft Foundry on Windows.
`LanguageModel` selects a model provided by the OS; its current implementation may use Phi Silica,
but this is not a model-selection API or a guarantee about future models. The image generator and
image describer are separate models, not Phi Silica.

## Windows App SDK version

The Windows targets pin `Microsoft.WindowsAppSDK` to **2.4.1-experimental**
(`MicrosoftWindowsAppSDKVersion` in `eng/Versions.props`):

| | 2.5.1 (stable) | 2.4.1-experimental (used here) |
|---|---|---|
| `Microsoft.WindowsAppSDK.AI` | 2.5.5 | 2.4.8-experimental |
| Structured JSON output | `LanguageModel` | `LanguageModel` |
| `ImageGenerator` (text to image) | not available | available |

The library references `Microsoft.WindowsAppSDK.AI` directly at **2.4.8-experimental** and its
Foundation dependency at **2.3.11-experimental**; its package graph does not include Search.
`Microsoft.WindowsAppSDK` remains centrally pinned to 2.4.1-experimental for Windows MAUI app
compatibility, and an app that references that umbrella package can still bring in Search. Stable
2.5.1 provides structured output but not this image-generation API. The experimental pin is not
suitable for a Microsoft Store release. Local development may require packages not yet mirrored
to the repository feeds.

See the [Windows App SDK release notes](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/)
for API promotion and experimental-channel limitations.

Because the SDK line is experimental, Windows projects set `SelfContained` and
`WindowsAppSDKSelfContained` so the runtime is bundled into the MSIX rather than resolved from a
framework package.

## Structured output

`WindowsAIChatClient` honors `ChatOptions.ResponseFormat`. When a `ChatResponseFormatJson` carries a
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

`WindowsAIChatClient` closes every schema with `additionalProperties: false`, applied recursively,
before constraining generation for any structured-output request.

Constrained decoding only forbids what the schema forbids, and schemas generated from a type by
`ChatResponseFormat.ForJsonSchema<T>()` are open — no `required`, no `additionalProperties`. Given an
open schema the model answers under a property name of its own choosing: asked to fill in a declared
`text` property it produced `body`, `response` and `message` instead. Those replies satisfy the
schema, so nothing fails and no status is reported, but the declared property is missing and
deserializing the result yields null. Closing the schema also measurably reduced
`ResponseInvalidJson` failures, since the decoder has less room to wander.

## Tool calling

The public Windows App SDK `LanguageModel` API does not expose function calling.
`WindowsAIChatClient` is the **native-only** public client: it rejects nonempty
`ChatOptions.Tools` and tool-only options with `NotSupportedException`. This guard remains in
place even when the experimental adapter is present. Without the adapter, the playground hides
Windows AI's tool controls; Azure and Apple retain their existing tool support.

`WindowsAIToolCallingClient` is a separate, **experimental** `IChatClient` middleware that
emulates function calling with schema-constrained JSON generation. It selects a `tool_name` from
the available functions or `none`, then fills the selected function's arguments using its JSON
schema. It emits `FunctionCallContent` for the standard function-invocation middleware to execute.
Each request forwarded to the native client removes `Tools`, `ToolMode`, and
`AllowMultipleToolCalls`; it never enables native function calling. `ChatToolMode.None` skips
selection, and `RequireAny` forces an initial selection before permitting a final answer.

The chat playground and EssentialsAISample enable this layer **only** with
`AI:EnablePhiToolCalling=true` in their local configuration (default: off). Do not set the flag
in a distributed build without explicitly accepting the research behavior. The playground also
offers its on-device image generator as an inline tool only under this opt-in; the standalone
**Images** page is available regardless of the flag. Direct image input remains unsupported;
the playground separately offers on-device captions with
`AI:EnableWindowsImageDescriptions=true` (also default: off). The function-invocation middleware
must wrap the adapter so it can execute its calls:

```csharp
new WindowsAIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Use(inner => new WindowsAIToolCallingClient(inner))
    .Build();
```

**Tool selection is not reliable.** In earlier device experiments, `Auto` selected and invoked
the date, calculator, and image tools for a simple "hello". The two-phase adapter, deterministic
selection settings, repeat guard, and per-turn call limit do not solve this accuracy problem.
Disable the tool checkboxes or select `ChatToolMode.None` when tools should not run; do not
depend on `Auto` for safety, cost control, or correct intent detection. Selection and argument
generation finish before a tool call is emitted, then final text or structured JSON streams
through the native client. This is a draft research layer, not a claim of dependable tool use.

## Context window

The context window is shared by the system prompt, the accumulated history and the new prompt, and
the API does not truncate automatically, so a long conversation can outgrow it.

When Windows reports `PromptLargerThanContext`, the client throws `InvalidOperationException` with
the underlying error and actionable instructions to shorten the history or start a new chat.
The underlying WinRT `LanguageModel.GetUsablePromptLength` remains available to apps that need
their own prompt-fitting policy; the `IChatClient` wrapper does not expose another fitting API.

## Streaming

Text and structured JSON updates use the Windows AI progress callback and can arrive
incrementally without a tool-selection phase. The number of updates depends on the installed
model.

## Image generation

`WindowsAIImageGenerator` implements `IImageGenerator` over `Microsoft.Windows.AI.Imaging.ImageGenerator`.
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
`IImageGenerator` use; the playground's standalone **Images** page invokes it with or without
the experimental flag. When opted in, the playground's chat client uses the image-generation
middleware to offer a `HostedImageGenerationTool` to the adapter; the native language model
itself still cannot call tools. Function invocation must wrap the image middleware and the Windows AI
adapter in that order so function calls and generated images reach the conversation.

The image model is prepared on first use, not when the chat client is created. Cancelling a request
while Windows prepares the model stops waiting for it; Windows may continue preparing the model in
the background. An unavailable model reports its readiness failure rather than a generated image.
When resuming an older chat containing image-generation tool activity, Windows AI preserves that
activity as text in its prompt; the text-only model cannot inspect generated pixels without a
separate image-description request.

## Image input

The native `LanguageModel` does not accept images. `WindowsAIChatClient` rejects image `DataContent`
instead of silently substituting captions for true multimodal inference. The chat playground has
an **opt-in sample-only** decorator: configure `AI:EnableWindowsImageDescriptions=true` to use
`ImageDescriptionGenerator` to replace image inputs with text captions before calling the chat
client. Its recording wrapper preserves the original attached image:

```text
User: [Image: A photograph of a bridge over a river at dusk...]
User: What time of day was this taken?
```

The description model is created lazily, only when a request carries an image. The language model
sees the caption and question as text, **not the pixels and question jointly**. Both models run
on-device. With the option off (the default), the playground hides image attachments for Windows
AI Chat; Azure's image-input behavior is unaffected.

## Semantic search

`AppContentIndexer` (Windows Search) is not in this baseline; its existing sample integration is
deferred to #523. The indexer does not expose vectors, so it cannot implement
`IEmbeddingGenerator<string, Embedding<float>>`.

`LanguageModel.GenerateEmbeddingVectors` is **not** a substitute. It returns a list of
`EmbeddingVector` per prompt whose counterpart is `GenerateResponseFromEmbeddingsAsync`; these are
prompt and token embeddings used for soft-prompting, not pooled vectors suitable for similarity
search.

## Packaging requirements

Windows AI models require the `systemAIModels` and `runFullTrust` capabilities in an MSIX package.
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

- **Model identity.** `LanguageModel` exposes no name, version or capability metadata, so behavior
  cannot be varied by model.
- **Semantic embeddings.** No `IEmbeddingGenerator` implementation; see above.
- **Native tool calling.** The published `LanguageModel` API has no function-call interface;
  the opt-in adapter only emulates it and does not change the native client API.
- **Direct multimodal chat.** The public `LanguageModel` has no image-input overload.

## Other Microsoft.Extensions.AI abstractions

The installed `Microsoft.Extensions.AI.Abstractions` **10.4.1** also includes
`ISpeechToTextClient`, `ITextToSpeechClient`, `IRealtimeClient`, `IHostedFileClient`, and
`IChatReducer` in addition to chat, image generation, and embeddings. Windows AI
[SpeechRecognitionModel](https://learn.microsoft.com/windows/ai/apis/speech-recognition) is a
plausible basis for a future speech-to-text adapter, with its own model download, consent, audio,
and streaming requirements. Windows also has separate speech-synthesis APIs, but neither speech
interface is wired here. `IRealtimeClient` describes a duplex session rather than a one-way
language-model stream; `IHostedFileClient` is for hosted files; `IChatReducer` is middleware for
history reduction. None is an equivalent for direct multimodal chat, semantic vectors, or native
tool calling.
