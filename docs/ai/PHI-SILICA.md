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
  which is acknowledged with a scoped `#pragma` at the call site. Disposing that wrapper also closes
  the underlying `LanguageModel`, so one instance is cached per client rather than created per
  request.
- Unlike `GenerateResponseAsync`, structured generation reports no incremental progress — the
  constrained JSON is only available from the completed result, so it is emitted as a single update.

Requests without a schema continue to use `LanguageModel.GenerateResponseAsync` with a real context.

## Tool calling

Windows App SDK exposes no function-calling API, so `PhiSilicaToolCallingClient`
(`samples/EssentialsAISample/Services/`) builds it on top of constrained decoding, in two phases:

1. **Selection.** One constrained call against a schema whose only property is a `tool_name` enum
   listing the available tools plus `none`.
2. **Arguments.** If a tool was chosen, a second constrained call against *that tool''s own*
   parameter schema. If `none` was chosen the request is passed through so the model answers
   normally, preserving any `ResponseFormat` the caller asked for.

The result is emitted as `FunctionCallContent`, so the standard `UseFunctionInvocation()` middleware
executes the call and re-invokes the client with the result in history. Chaining therefore falls out
of the same loop, and it can always terminate because `none` is always available.

### Why two phases

The obvious design is one combined schema: a single object carrying a `tool_call`/`text`
discriminator, the tool name, the arguments and the answer text. Probing the on-device model showed
that this is unreliable.

- It skipped prerequisite calls and invented placeholder arguments such as `"USER_ID"`.
- Part-way through a chain it gave up and asked the user for data it should have fetched.
- Wording had outsized effects. Adding "if you can answer, put the answer in the text property" was
  enough to make it *describe* a tool in prose instead of calling it.

Asking one small question at a time is both more accurate and faster — selection lands in about two
seconds — and giving the argument phase the tool''s real schema means `required` parameters are
actually filled in.

### Closed schemas

Every schema sent to the model sets `additionalProperties: false`, applied recursively. Without it
the model invents property names: it produced `body`, `response` and `message` in place of a declared
`text` property. Constrained decoding permits those, so the value silently reads back as null.

### Streaming

A partial tool call is not actionable, so requests carrying tools resolve the response fully and then
emit it, rather than streaming tokens.
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
