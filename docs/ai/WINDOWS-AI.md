# Windows AI support

`Microsoft.Maui.Essentials.AI` provides two Windows implementations of
`Microsoft.Extensions.AI` interfaces: `WindowsAIChatClient` (`IChatClient`) and
`WindowsAIImageGenerator` (`IImageGenerator`). They use the Windows App SDK's
on-device `LanguageModel` and `ImageGenerator`, respectively. The OS selects
the underlying models; these APIs do not identify or select particular model
weights. The two features do not share a model.

## Requirements

- Windows 11 24H2 (build 26100) or later, with hardware supported by the
  respective Windows AI feature. Image generation requires a Copilot+ PC with
  an NPU; the image model is an optional, multi-gigabyte download.
- An **MSIX-packaged** desktop app declaring `systemAIModels`. The
  [playground manifest](../../samples/AIExtensions.Sample.ChatPlayground/Platforms/Windows/Package.appxmanifest)
  also declares `runFullTrust` and includes both `Windows.Universal` and
  `Windows.Desktop` target families with `MaxVersionTested=10.0.26226.0`.
  Do not set `WindowsPackageType=None`: unpackaged apps cannot use this
  capability. Keep the manifest version-replacement properties disabled so
  the target-family values survive the build.
- The library references `Microsoft.WindowsAppSDK.AI` **2.4.8-experimental**
  and `Microsoft.WindowsAppSDK.Foundation` **2.3.11-experimental**; the
  repo-wide `Microsoft.WindowsAppSDK` version is **2.4.1-experimental**. The
  image API used here is not available in the stable SDK. The playground
  bundles the experimental runtime (`SelfContained` and
  `WindowsAppSDKSelfContained`) and uses BuildTools.WinApp for packaged
  `dotnet run`. This is a development setup, not a Store distribution guide.

Both adapters check `GetReadyState()` and call `EnsureReadyAsync()` when the
model is not ready. A missing capability, unsupported hardware, or failed
setup surfaces as an error, rather than silently switching providers.
Chat-model setup begins when the client is constructed; image-model setup
begins with the first generation request. The image adapter can initiate a
large model download: a shipping app should obtain user consent before
requesting that model.

## Chat

`WindowsAIChatClient` supports text-only conversations, incremental streaming
updates, and non-streaming responses assembled from those updates. It maps
temperature, TopK, and TopP to native `LanguageModelOptions`. It flattens
conversation history into a text prompt and passes the leading system
instruction as model context for ordinary text generation.

For `ChatResponseFormatJson` **with a schema**, it calls the native
`GenerateStructuredJsonResponseAsync` API. That API lacks a context overload,
so the system instruction is prepended to the structured prompt. The adapter
closes object schemas with declared properties (`additionalProperties: false`)
to prevent valid-but-unexpected property names from leaving requested fields
empty. A JSON response format without a schema does not use constrained
decoding. Streaming uses native progress when provided and emits a final
response when only a completed result is available. A prompt exceeding the
native context window raises `InvalidOperationException`; the adapter does
not truncate history or expose a separate context-window exception.

**Not supported:** direct image input, native tool calling, and selecting or
reporting exact model weights. Image data and nonempty `ChatOptions.Tools`
are rejected rather than guessed at or executed. `MaxOutputTokens` is
validated as positive but is not forwarded as a native output limit.
The playground accordingly offers no Windows Chat image attachment or tool
controls.

### Conversation context experiment

The native `LanguageModelContext` retains prompts **and generated responses**
across calls, so a continuing conversation can send only the next user prompt.
Microsoft's [Phi Silica platform card](https://learn.microsoft.com/windows/ai/cards/phi-silica-platform-card#limitations)
describes an **approximately 3.5K-token context window shared by prompts,
replies, and instructions**. This is not a guaranteed per-call input or output
allowance. `GetUsablePromptLength` reports the position in a particular prompt
that fits the *remaining* window, in UTF-16 characters rather than tokens.
The current `IChatClient` adapter instead receives an entire caller-supplied
history and creates a fresh context for each response. Reusing one context
without checking that history would mix independent conversations or ignore
edited messages. The native API cannot insert arbitrary past assistant/tool
messages into a context, and structured JSON generation has no context
overload.

An opt-in packaged device test compares the current adapter, a fresh native
context with the same serialized history, and a reused native context sent
only the new prompt. Run it on a Windows AI-capable machine from the repo root:

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 -p:TestingMode=XHarness -p:EnableWindowsAIContextBenchmark=true -p:DeviceRunnersDataTimeout=1200 --filter WindowsAIContextBenchmarkTests --logger trx
```

Filter to `CompareSubstantial` for five-turn conversations with substantial
inputs and either brief or paragraph-length requested replies, or to
`CompareNearContextLimit` for a five-turn run sized by a native context-fit
probe with room reserved for replies. The probe is an estimate: native
conversation formatting and actual reply lengths may differ, so inspect the
observed fit and response lengths rather than treating the character position
as an exact token count. The tests record per-turn latency, input length,
actual response length in characters and words, first-chunk latency, and a
continuity marker in TRX output and app-private
`windows-ai-context-benchmark-<scenario>.tsv` files. They do not gate builds
on a hardware-dependent timing threshold.

## Image generation

`WindowsAIImageGenerator` supports text-to-image (no source image) and
prompt-guided image-to-image (one source image). It returns encoded image
bytes, PNG by default, rather than a hosted URI. `Count` generates multiple
results; `Creativity`, `MaxInferenceSteps`, and `Seed` can be provided in
`AdditionalProperties`.

**Not supported:** multiple source images, masks, requested output size, or
hosted-URI responses. The second image in an `ImageGenerationRequest` is not
necessarily a mask, so the adapter rejects it instead of interpreting it as
one. The Windows image model is separate from the Chat language model and
may not be installed even when Chat is ready.

## Outside this Windows surface

There is no Windows embedding generator in this package. The Windows
`AppContentIndexer` is a search/index API, not an `IEmbeddingGenerator`
that returns portable vectors for the playground's embedding workflow.
Image description, speech, and other Windows AI APIs are not wrapped here.

To run and compare providers, see the
[Chat Playground README](../../samples/AIExtensions.Sample.ChatPlayground/README.md).
For platform and model availability, see Microsoft's
[Windows AI API setup](https://learn.microsoft.com/windows/ai/apis/get-started)
and [image-generation requirements](https://learn.microsoft.com/windows/ai/apis/image-generation).
