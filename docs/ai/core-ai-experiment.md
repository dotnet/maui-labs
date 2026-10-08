# Local Core AI experiment (phase 1)

This is an unpublished experiment, not a new shipping provider/package.
It requires Xcode 27 and an OS27 Apple target. Ordinary builds still select
the System framework, with its existing OS26 deployment minimum.
No model, tokenizer or Core AI package dependency is added to NuGet.

## Build inputs and model ownership

Pass **both** `UseXcode27Preview=true` and `EnableCoreAI=true`. The latter:

- Selects only `EssentialsAICoreAI` in the existing Xcode project, rather than
  linking it alongside `EssentialsAI` (both declare the same Objective-C classes).
- Includes the experimental `CoreAIChatClient` facade.
- Isolates native/managed/app outputs under `Debug/coreai/` (or `Release/coreai/`)
  to avoid mixing variants.
- Disables shipping/packing and rejects CI/official builds.

Validation apps additionally require **`CoreAIModelDirectory`**, an explicit,
complete local resource folder. The sample and device tests import the same
`src/AI/CoreAIModelResources.targets`. They stage its files as app-owned
`CoreAIModel/` bundle resources, preserving the directory hierarchy:

```text
CoreAIModel/
  metadata.json
  <assets.main>.aimodel/
    ... exported files ...
  tokenizer/
    tokenizer.json
    tokenizer_config.json
    chat_template.jinja       # when required by the tokenizer
```

At runtime both apps resolve
`Path.Combine(NSBundle.MainBundle.ResourcePath, "CoreAIModel")`.
The library receives this directory through its constructor, not through
`ChatOptions.ModelId`. There are no ordinary-build downloads or hidden
System/cloud fallback. Explicitly enabled missing fixtures fail the build;
invalid/incomplete bundles fail the request.

```bash
MODEL="$PWD/artifacts/coreai-validation/models/qwen3_1_7b_4bit_ctx4096_dynamic"

dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true -p:EnableCoreAI=true \
  -p:CoreAIModelDirectory="$MODEL"
```

Add `-t:Build,Run` to launch the sample locally. Choose **Core AI (experimental)**.
The first request loads the native configuration/tokenizer asynchronously and
loads the engine when generation begins. Provider status reports loading,
ready/model identity or an explicit error. It does not use System availability.
The existing recording, logging, telemetry and function middleware remain in
the pipeline. Never distribute Debug sample bundles: they can embed local secrets.

## Pinned source and reproducible fixture

The Xcode project pins Apple's
[`coreai-models`](https://github.com/apple/coreai-models) revision
`1953c4f90ba0214c1abc7bebcb9be5107e329a46`. Commit its `Package.resolved`
alongside the project. It records the entire resolved runtime graph, including
Transformers, XGrammar/CXGrammar and C++ dependencies.
Swift Collections is constrained to **1.2.1**: newer resolved versions require
experimental compiler features that the .NET SDK's archive task drops.

The experiment overrides that archive task **only** when enabled, retaining
the required `Lifetimes` feature. It copies the pinned SwiftPM Hub/Crypto
resource bundles into the framework's resource directory before creating the
XCFramework. They must survive into the .NET-built app; a standalone Swift
build alone is not integration proof.

Primary fixture:

| Input | Pin/value |
|---|---|
| Checkpoint | `Qwen/Qwen3-1.7B` (Apache-2.0) |
| Checkpoint revision | `70d244cc86ccca08cf5af4e1e306ecf908b1ad5e` |
| Exporter | Apple revision above, frozen `uv.lock` environment |
| Recipe | macOS, 4-bit, float16, dynamic 4096-token context |
| Bundle name | `qwen3_1_7b_4bit_ctx4096_dynamic` |
| Prepared size | 979,929,057 bytes (0.913 GiB), seven files |

Keep the checkpoint, export and caches under an ignored local directory
(for example `artifacts/coreai-validation`), never in Git/library resources.
Use the pinned [Qwen3 export recipe](https://github.com/apple/coreai-models/blob/1953c4f90ba0214c1abc7bebcb9be5107e329a46/models/qwen3/README.md):

1. Check out the Apple revision and run `uv sync --frozen --no-default-groups`.
2. Download the **exact checkpoint revision**, including its matching tokenizer
   and chat template, into an isolated local Hub cache.
3. Verify the cache's `main` alias matches that revision, then export offline.
   The pinned exporter accepts a Hub model ID but has no revision option; its
   memory-efficient loader does not accept a local snapshot path. The check
   below must succeed before export, so a moved upstream checkpoint is an
   explicit failure rather than an unpinned model.

```bash
# Run from the pinned coreai-models checkout. Use ignored absolute directories;
# this is an explicit preparation step, never an app build step.
export HF_HOME="/absolute/path/to/ignored/coreai-hf-cache"
OUTPUT="/absolute/path/to/ignored/exported-models"
REVISION="70d244cc86ccca08cf5af4e1e306ecf908b1ad5e"

uv run --frozen --no-default-groups hf download Qwen/Qwen3-1.7B \
  --revision "$REVISION" --cache-dir "$HF_HOME/hub" --quiet
uv run --frozen --no-default-groups hf download Qwen/Qwen3-1.7B config.json \
  --revision main --cache-dir "$HF_HOME/hub" --quiet
test "$(cat "$HF_HOME/hub/models--Qwen--Qwen3-1.7B/refs/main")" = "$REVISION" \
  || { echo "Checkpoint changed; do not export an unpinned cache." >&2; exit 1; }

HF_HUB_OFFLINE=1 TRANSFORMERS_OFFLINE=1 \
  uv run --frozen --no-default-groups coreai.llm.export Qwen/Qwen3-1.7B \
  --platform macOS --compression 4bit \
  --max-context-length 4096 --output-dir "$OUTPUT" \
  --output-name qwen3_1_7b_4bit_ctx4096_dynamic
```

4. Verify the exporter saved the matching `tokenizer.json`,
   `tokenizer_config.json` and `chat_template.jinja` under `tokenizer/`, with
   `language.embedded_tokenizer=true` in metadata. Record exporter/checkpoint
   revisions and exported size with the validation results.

The supplied export is a **macOS recipe**. Do not claim an iOS execution result
for it; prepare an appropriate iOS recipe and use a real OS27 device separately.

## Client behavior and limits

`AppleIntelligenceChatClient` retains its constructors and image helpers and
composes the internal `FoundationModelsChatClient`. `CoreAIChatClient` uses the
same conversion, native tools, callback context, cumulative-answer chunkers and
JSON-schema handling. Its constructor only records an explicit directory.
Model metadata becomes available after loading; response/update model IDs come
from the local bundle's actual `name`, never a caller's requested `ModelId`.

Requests sharing the canonical model resource path are serialized across
facades at the native resource boundary, including awaited tools. Histories are
fresh per request. Ownership is retained through native completion/callbacks;
disposing a facade cannot unload a borrowed engine. The last owner releases
its references, without calling `unload()` during generation. Stream disposal
cancels both native work and the managed tool's linked cancellation token.
Tools must cooperate with cancellation.

Interrupted generations discard partial engine state through the adapter's
resource lifecycle after the executor borrow returns, before another facade
can acquire the path. Recovery tests exercise a schema request immediately
after cancelled or early-disposed plain/schema streams, rather than only
another plain response.

The wrapper validates chat-template rendering before **every native
generation**, including post-tool turns. A missing/unrenderable local template
fails explicitly instead of allowing the upstream adapter's plain-text fallback
to silently lose tool semantics.

Supported phase-1 options are instructions, JSON schema, `Temperature`,
`MaxOutputTokens` and Auto/None tool selection. CoreAILM's adapter honors
temperature and the output limit, but does not forward standard TopK/TopP/Seed.
The client explicitly rejects those, penalties, stop sequences, forced/limited
tool calls and `ReasoningOptions`. It defaults Core AI temperature to **0.6**
rather than greedy Qwen thinking. Other models/exports are not validated by
this experiment.

Core AI is text-only. System image paths are unchanged. Reasoning controls stay
disabled: native thinking may occur, but no reasoning events/tokens/signatures
are fabricated or transported. For focused Qwen phase-1 answer/tool tests,
`/no_think` can be placed explicitly in the **prompt**; this is not a managed
reasoning API.

On OS27 the bridge maps the native response's supplied input/output/total
counts to `UsageDetails` and a final streaming `UsageContent`. It preserves
zero/absent values; OS26 still has no usage. Counts are the provider's counts,
not necessarily just visible answer tokens. No cached/reasoning split is
invented. Native tool calls are informational, so
`UseFunctionInvocation` does not execute them a second time.

## Validation commands

Host-only suites do not need the model or opt-in property:

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.UnitTests
dotnet test tests/AIExtensions/Microsoft.Maui.AI.Chat.Tests
```

Compile the real device tests with the same app resource input:

```bash
dotnet build tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true -p:EnableCoreAI=true \
  -p:CoreAIModelDirectory="$MODEL"
```

Run the local Mac Catalyst runner, selecting only Core AI classes:

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true -p:EnableCoreAI=true \
  -p:CoreAIModelDirectory="$MODEL" \
  -p:DeviceRunnersDataTimeout=240 \
  --filter 'FullyQualifiedName~CoreAI' --logger trx
```

For an explicitly chosen iOS27 device, use `-f net10.0-ios27.0` and
`-p:DeviceRunnersDevice=<device-udid>`, with an iOS-compatible exported fixture.
Do not rely on implicit device selection.

Tests cover real identity/usage, ordinary/history responses, concurrent facade
isolation, native collected-answer versus deltas **from the same request**,
exactly-once tools and call IDs, awaited trace/span logging, schema as a separate
mode, cancellation/next-request recovery, stream disposal, disposal during a
tool, oversized context and invalid bundles/options/content.
They do not fabricate tool events or retry unsuccessful model choices.
The parameterless configured adapter exists only in the device tests.
The explicit result-channel timeout allows a long recovery assertion to finish
without the runner mistaking a quiet test for a lost connection.

Check the standard build separately, **without** `EnableCoreAI`:

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Record actual app/device results separately from build results. Successful
linking is not a model-response proof, a macOS recipe is not iOS proof, and an
OS26 deployment-target build is not an OS26 runtime regression test.
For offline runtime proof, use a complete app-local fixture and an app/process-
scoped network restriction; do not disrupt shared-machine networking.

### Implementation build/host checks

On the implementation host (macOS27.0.1 arm64, .NET10.0.401, Xcode27.0 /
Swift6.4), the phase-1 source wiring passed:

| Check | Result |
|---|---|
| Normal System Mac Catalyst27 binding | Build passed; Mach-O minimum remains **26.0**, SDK27, both arm64/x86_64 |
| Opt-in Core AI Mac Catalyst27 and macOS27 bindings | Builds passed |
| Opt-in Mac Catalyst playground and device-test apps | Builds and strict/deep code-signature verification passed |
| App resource boundary | Exactly one EssentialsAI framework variant; Hub/Crypto bundles present; model has seven files / 979,929,057 bytes under app resources |
| Essentials.AI host suite | **361 passed**, including supplied/absent usage tests; timing-dependent tests unchanged |
| Chat Playground host suite | **154 passed**, including phase-1 reasoning/image descriptor checks |
| Missing fixture / official-build gates | Fail explicitly as intended |
| Experimental package properties | `IsPackable=false`, `IsShipping=false`; normal package properties remain true |

The SDK reports legacy Foundation Models `GenerationError` deprecation
warnings in experimental native builds.

### Runtime results

The final Mac Catalyst27 test application ran the real staged Qwen fixture:
**36 passed, zero failed/skipped**. This includes matching streamed callback
IDs against the same request's collected native transcript, managed history
replay without another tool execution, post-tool template failures and repeated
plain/schema interruption followed immediately by another facade's schema
request. Interrupted engine state is discarded before path reuse.

The playground also produced a real streamed `CORE AI LOCAL` answer, displayed
the bundle model ID and supplied usage, and auto-saved the interaction. Native
Foundation Models plain/stream/tool/schema requests additionally passed in a
process with networking denied; this is a native offline proof, not a claim
that the .NET playground was network-isolated.

A System-provider runtime selection passed **55 of 56** assertions. The
unchanged `GetStreamingResponseAsync_WithToolCalling_NoNullTextBeforeToolCalls`
test rejected newline-only answer deltas *after* the tools; the unchanged
plain-text chunker deliberately preserves those formatting deltas. No
production whitespace filtering or unrelated test change was made.

OS26 runtime and actual iOS-device execution remain unverified. The normal
framework's OS26 deployment minimum is a build result, not those runtime proofs.
