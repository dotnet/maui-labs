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
Chat-model setup now begins on the first request; image-model setup
also begins with the first generation request. The image adapter can initiate a
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
transforms the caller's schema for the native model: every declared property
(including nested object properties) becomes required, and objects disallow
additional properties. The common playground response type stays optional,
without `[JsonRequired]` attributes; other providers retain their own schema
handling. A JSON response format without a schema does not use constrained
decoding. Streaming uses native progress when provided and emits a final
response when only a completed result is available. Selecting streaming does
not force the native structured operation to send
partial JSON, so a result with no progress appears only when complete. A prompt
exceeding the native context window raises `InvalidOperationException`; the
adapter does not truncate history or expose a separate context-window exception.
The adapter starts model setup and native generation away from the caller's UI
thread; cancelling while a native entry point is blocked releases the waiting
caller and cancels the operation if it eventually becomes available.
This keeps the UI responsive; it cannot guarantee that a stalled native
generation operation will finish or that subsequent requests will work.
On Windows 25H2 build 26200.9448, a structured request was observed to stall
without progress, and a later plain-text request also stalled after cancellation.
Fresh packaged runs also completed a one-field schema and a four-field schema
with every field required in approximately 4 and 6 seconds, respectively.
Both recorded only one response update; the required four-field result
contained every field. Removing `required` from the same four-field schema
completed but omitted two requested fields. The stall is therefore not
consistently caused by `required`, and the adapter does not synthesize
streaming updates when the native operation provides only one result.
In a separate headless packaged run, the playground's four-field required
schema produced no update and timed out after 20 seconds despite a successful
plain-text request first. Subsequent fresh processes sometimes stalled even
on plain text. Requiring fields constrains completed responses; it does not
guarantee that the native operation completes or streams progress.

**Not supported:** direct image input, native tool calling, and selecting or
reporting exact model weights. Image data and nonempty `ChatOptions.Tools`
are rejected rather than guessed at or executed. `MaxOutputTokens` is
validated as positive but is not forwarded as a native output limit.
The playground accordingly offers no Windows Chat image attachment or tool
controls.

### Experimental function calling

`WindowsAIToolCallingClient` is a separate `DelegatingChatClient` above the
native text-only client. By default, constrained `{reason,decision}` selection
chooses one function or answers normally. Explicit
`AllowMultipleToolCalls=true` switches to constrained
`{reason,tool_names,more_tools_after_results}` planning: select independent
functions in one round, identify whether another planning round is needed
after their results, or answer directly after the selected batch. The
diagnostic reason and per-call evidence are attached to each emitted
`FunctionCallContent` as `windows_ai.reason` and `windows_ai.evidence` in
`AdditionalProperties`; neither field controls dispatch. The prompts include
ordinary function and parameter descriptions, types, and required state.

Every selected function uses the same constrained extraction response:
`{evidence: string[], arguments: object}`. The argument object uses the
function's ordinary JSON schema with only its top-level `required` list
removed so missing values can be reported. A zero-argument function returns an
empty argument object but must still cite text showing that the user actually
requested it. Argument extraction receives a focused synthetic request
containing labeled, JSON-encoded source data rather than the original role
history. The host requires each evidence entry to be an exact quote from an
original user message or a completed tool result. It also accepts an exact
labeled source, a JSON-encoded exact quote, or JSON evidence that is
structurally identical to a JSON tool result after removing decoder-inserted
control characters. Changed values or additional JSON remain invalid.

Arguments without valid evidence are not dispatched. The host validates
supplied arguments against the supported schema subset. If one selected
function lacks valid evidence or required arguments but other selected calls
are valid, it emits only the valid calls, records
`windows_ai.deferred_tools` and `windows_ai.deferred_reasons` diagnostics on
them, and replans after their results. Selected zero-argument functions without
request evidence are suppressed rather than retried; valid sibling calls carry
`windows_ai.suppressed_tools` and `windows_ai.suppressed_reasons`. If no
selected call is valid, missing values clarify, invalid evidence surfaces an
error, or a role-like/unsupported zero-argument selection falls back to a
normal answer. This lets independent calls run together while a dependent call
waits for the result that supplies its argument.

As a narrow prompt-injection mitigation, evidence containing a quote-opened
`Assistant:`, `System:`, `User:`, `<assistant>`, or `<|assistant|>` role prefix
is treated as data and cannot authorize dispatch. This does not make arbitrary
natural-language authorization reliable. The schema's enum, numeric bounds,
Unicode scalar length, and ECMA-compatible pattern constraints still apply.
The host does not prove that a model-derived value follows semantically from
its quote: `204` may yield `ORD-204` if the ordinary schema permits it, and a
schema-valid *wrong* inference can still be passed to a function. No custom
normalizer or per-tool resolver is required.

Place `UseFunctionInvocation()` outside the adapter to execute calls. Every
inner request clears `Tools`, `ToolMode`, and `AllowMultipleToolCalls`; direct
`WindowsAIChatClient` still rejects native tool options. One call per turn is
the default. The batch path permits at most three calls per current turn,
deduplicates selected tool names, exposes each function name at most once per
current user turn, and rejects exact historical call repeats. This means one
function cannot currently be called with two different argument sets in the
same turn. A complete batch with `more_tools_after_results=false` goes
straight to the final answer when all its call IDs have results. A batch that
requests another round, or whose invalid calls were deferred, selects again.
Each native phase, including the final answer, has a 30-second bound and no
automatic retry. After any interrupted native response,
that wrapper instance rejects later requests because native termination cannot
be confirmed; create a new client only after the platform has recovered. Once
calls are complete, the final answer receives ordinary
messages plus typed JSON tool-result data, not the raw `[Tool call]`/
`[Tool result]` transcript.

The Windows Chat Playground always enables this adapter and exposes only
harmless demonstration tools. Library consumers still opt in explicitly by
constructing `WindowsAIToolCallingClient`; wrapping a `WindowsAIChatClient`
does not enable Chat image input or image tools. Tool and parameter
descriptions are part of the model's decision input; use explicit descriptions
that state when a tool is required, when it must not run, and what constitutes
each argument.

This remains a research preview, not a reliable authorization boundary.
Constrained JSON controls shape, and evidence validation establishes textual
provenance, not semantic correctness or user authorization. On 2026-09-28,
the final adapter completed 10/10 frozen core turns (26 native requests) and
20/20 frozen held-out turns (50 native requests) with no dispatched
unnecessary calls, missed/wrong calls, or reported errors on one AMD Ryzen AI
7 PRO 350 test machine (Windows build 26200, KB5124881 component
1.2608.951.0). In both quoted-role held-out turns, the model still selected
weather; the host evidence veto prevented dispatch. These small deterministic
observations validate the tested scenarios on that machine, not general model
reliability. Do not expose consequential, privacy-sensitive, or side-effecting
tools without a separate application authorization step. The same final code
passed 109/109 scripted wrapper contracts both portably and through the
packaged MSIX runner.

The packaged `WindowsAIToolCallingEvidenceEvaluationTests` records every raw
decision, exact argument, final answer, error, native request duration, and
summary. It reuses one native model per run, stops on the first timeout, and
keeps uncertain native operations quarantined rather than retrying. Run each
set separately with no other packaged AI app:

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -p:TestingMode=XHarness -p:EnableWindowsAIToolEvaluation=true -p:DeviceRunnersDataTimeout=1200 --filter 'FullyQualifiedName~WindowsAIToolCallingEvidenceEvaluationTests.ProductionAdapter_CoreCases' --logger trx
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -p:TestingMode=XHarness -p:EnableWindowsAIToolEvaluation=true -p:DeviceRunnersDataTimeout=1200 --filter 'FullyQualifiedName~WindowsAIToolCallingEvidenceEvaluationTests.ProductionAdapter_HeldOutCases' --logger trx
```

Archive the generated TRX and
`tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\test-results\tcp-test-events.jsonl`
before another packaged run. Several rejected, longer planning variants timed
out after healthy plain-text preflights. A timed-out native operation did not
always confirm termination, and fresh `CreateContext` calls sometimes remained
blocked for roughly ten minutes. That interval is an observation, **not** a
recovery guarantee.

`WindowsAIToolCallingEvaluationTests` remains the A/B harness for the native
plain-label history and a test-only JSON-transcript representation. Neither
that alternative nor literal model-family tokens are a documented Windows
chat/tool contract.

Earlier packaged experiments compared host routing, one-pass constrained
generation, a model classifier and critic, and a typed workflow ledger. None
improved the final safety and accuracy boundary consistently. The selected
adapter keeps the model responsible for intent while treating schemas,
evidence provenance, duplicate detection, and call limits as host-enforced
invariants.

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

### Prompt framing experiment

The Windows `LanguageModel` API accepts plain strings and a separate
`CreateContext(systemPrompt)` argument; Microsoft does not document its
internal chat template or expose the active tokenizer. Phi Silica is derived
from Phi-3.5-mini, but the downloadable
[Phi-3.5](https://huggingface.co/microsoft/Phi-3.5-mini-instruct/raw/main/tokenizer_config.json)
and [Phi-4 mini](https://huggingface.co/microsoft/Phi-4-mini-instruct/raw/main/tokenizer_config.json)
templates do **not** establish that literal `<|user|>` or `<|system|>`
strings are special tokens when passed through the Windows API. Other model
families have different templates (for example,
[Qwen's ChatML](https://huggingface.co/Qwen/Qwen2.5-7B-Instruct/raw/main/tokenizer_config.json),
[Llama 3](https://github.com/meta-llama/llama-models/blob/main/models/llama3_3/prompt_format.md),
and [Gemma](https://ai.google.dev/gemma/docs/core/prompt-structure)).
Microsoft also [plans to replace Phi Silica with Aion Instruct](https://learn.microsoft.com/windows/ai/apis/phi-silica),
so hard-coding a Phi-family template into this OS-backed adapter would be
especially fragile.

An opt-in packaged benchmark compares the real adapter with these literal
templates, an assistant-label suffix, and XML-escaped role elements. It also
compares native system context against system instructions written into a
plain, Phi-style, or XML prompt. Both cases include a fictional play with
line-start `Assistant:` and role-like strings *inside the user's text*, plus
a direct conflict between the system and user instructions. The benchmark
records latency, required plot facts, system-prefix compliance, and raw
responses. A capacity probe compares repeated literal Phi markers to
same-length misspellings: a difference can suggest tokenizer special handling,
but the API reports **character positions**, not token IDs, and cannot prove
how Windows wraps messages. The model's own answer about its identity and
expected template is recorded as an **unverified self-report**. Run from the
repo root on a Windows AI-capable machine:

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 -p:TestingMode=XHarness -p:EnableWindowsAIPromptFormatBenchmark=true -p:DeviceRunnersDataTimeout=900 --filter WindowsAIPromptFormatBenchmarkTests --logger trx
```

Results are in the TRX and app-private
`windows-ai-prompt-formats-{history,system}.tsv` files. Template strings in
this experiment are **not** production recommendations. On this device,
2,048 repetitions of `<|assistant|>` fit the capacity probe while a
same-length misspelling fit only about 511; this is consistent with different
tokenization, **not** proof of chat-role behavior. In two runs of the
line-start three-act play, the adapter included four of six required plot
facts, literal Phi formats two of six, and XML-escaped messages six of six;
the XML responses were slower. These are substring checks over one synthetic
play, not a semantic accuracy score. Even the native system prompt did not
enforce its requested prefix when the user explicitly requested a conflicting
one. None of these observations identifies the Windows model's template,
guarantees behavior across model updates, or makes in-band delimiters a trust
boundary. Keep using the native system context and validate outputs when a
response format is required.

## Image generation

`WindowsAIImageGenerator` supports text-to-image (no source image) and
prompt-guided image-to-image (one source image). It returns encoded image
bytes, PNG by default, rather than a hosted URI. `Count` generates multiple
results; `Creativity`, `MaxInferenceSteps`, and `Seed` can be provided in
`AdditionalProperties`.

**Not supported:** multiple source images, masks, requested output size, or
hosted-URI responses. The second image in an `ImageGenerationRequest` is not
necessarily a mask, so the adapter rejects it instead of interpreting it as
one. The Windows AI image model is separate from the Chat language model and
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
