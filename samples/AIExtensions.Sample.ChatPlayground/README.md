# AI Chat Playground

A .NET MAUI sample for comparing on-device Apple Intelligence with optional
Azure OpenAI providers. It runs on Android, iOS, Mac Catalyst, and Windows
(not native macOS); providers vary by platform.

## What you can try

| Tab | Features | Providers |
|-----|----------|-----------|
| Chat | Text, streaming, structured JSON, optional tools and image input | Apple Intelligence (iOS/Mac Catalyst), Azure OpenAI, offline Replay |
| Embeddings | Import documents, compare chunking policies, and inspect ranked documents and chunks | Apple NaturalLanguage (iOS/Mac Catalyst), Azure OpenAI |
| Images | Generate images or edit a source image | Azure OpenAI |

Apple image input requires OS 27 and a ready vision-capable model. Apple does
not generate images or fall back to Azure. Azure chat uses the Responses API
and supports image input, reasoning summaries, and an optional image-generation
tool. Structured responses stream only when the provider emits incremental
updates. Each tab has its own services.

Chat displays the model ID reported by the provider, including during replay.
It auto-saves the current recording locally. **New** replaces that recording;
**More** loads a bundled example or imports and exports recordings. Replay
works offline without credentials or a model. Select a live provider to
continue a conversation. Features are shown only when the selected provider
supports them. Uncheck unneeded tools or choose **None** to disable tool calls.
Switching providers preserves your Structured JSON preference, but providers
that opt out receive plain-text requests and their responses display as text.

The local, bounded diagnostics sidebar starts collapsed on Chat, Embeddings,
and Images. The header's **Logs** button toggles it without stopping diagnostic
capture while hidden. In narrower windows, tap outside the sidebar to dismiss it.
It groups built-in logs and completed spans by trace, including tool lifecycle logs from
MEAI and Apple. Apple's native tool callbacks retain the originating request's
trace/span. Chat and embeddings emit spans; the pinned MEAI version provides
image logging only. Model and usage data appear only when supplied by the provider.
Trace payload logging and sensitive telemetry capture are disabled, but
exceptions may still contain sensitive data. Diagnostics are not exported
automatically.

## Compare embeddings and chunking

The [native device experiments](../../tests/AI/EmbeddingQuality/README.md)
exercise this same chunker and sample documents. They show tradeoffs, not a
universally optimal chunk size: try two-sentence windows or compare 360/90 with
360/0, and inspect whether the top chunks actually retain the answer context.

In **Embeddings**, use **Import → Load sample documents** to add three synthetic,
multi-paragraph documents about harbor routes, developer troubleshooting/security,
and cafe bookings/food. Loading is explicit, local, additive, and never replaces
your documents or contacts a model. You can also import your own Markdown or
plain text files (UTF-8, up to 256 KB each).

In **Embedding settings**, choose a generator, optional dimensions, and chunking:

- **FixedCharacters** defaults to size **360**, overlap **0**, preserving the
  original policy. Size and overlap are UTF-16 units, not tokens. Splits prefer
  word boundaries; surrogate pairs are never split (a pair can exceed size 1).
  Overlap is at most the requested units after whitespace/boundary adjustment;
  even near-total overlap always advances.
- **Sentences** defaults to **1** sentence, overlap **0** sentences. This is a
  portable punctuation-based splitter, **not native NLTokenizer**: `.`, `!`, or
  `?` followed by whitespace/end ends a sentence. Dots inside identifiers and
  decimals are preserved. Abbreviations, closing quotes, and languages without
  whitespace are not handled linguistically.
- **WholeDocument** embeds the normalized document as one chunk with no size or
  overlap inputs. Provider input limits still apply.

Whitespace is normalized in every mode. Sizes must be 1–262144; overlap must be
non-negative and smaller than size. Changing modes restores their defaults.
Each document is limited to **4,096 chunks** and **4 Mi UTF-16 units of total
emitted chunk text**, including repeated overlap. The budget is checked before
allocating each chunk string. Excessive overlap or very small chunks fail with
an explicit error instead of expanding without bound; reduce overlap or increase
chunk size before indexing again.
Choose **Index documents** explicitly after changing settings. Each model,
dimension count, and chunking policy has its own saved index; older pre-chunking
indexes are not reused. Search never silently builds an index.

Search ranks documents on the left by their **maximum chunk cosine similarity**.
The first document is selected automatically; select another to see **all** its
chunks on the right, ranked by cosine with source chunk numbers and full text.
Ties use source order. Cosine is not confidence; there is no lexical reranking
or acceptance threshold. Narrow windows stack the document and chunk sections.
Changing settings, importing, or maintaining indexes clears stale result details.
**Clear index** targets only the selected configuration. **Clear documents**
removes imported text and all its indexes, never chat recordings.

Indexing with a cloud provider sends document chunks off-device; searching sends
the query. Loading the samples does neither. Keep sensitive text out of cloud
indexes unless you intend to share it with that provider.

## Run the sample

Use the repository's pinned .NET 10 SDK and matching MAUI workload.
Apple builds require Xcode 27. On macOS, from the repository root:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `net10.0-ios27.0` for iOS and select a device with `--device <device-id>`.
Use `--list-devices` to list available targets. Apple Intelligence text chat requires iOS or Mac
Catalyst 26+ on a supported device with the model enabled. Image input requires
27+ and a ready vision model. Apple sentence embeddings require the English
language asset, which may be absent on simulators.

On Windows:

```powershell
dotnet run --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj -f net10.0-windows10.0.19041.0
```

The Windows sample remains unpackaged and offers Azure-backed features and
offline Chat Replay, not on-device AI. Azure and Replay also run on Android.

With Java and the Android SDK installed:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-android --device <device-id>
```

## Configure Azure OpenAI

Azure is optional. On-device providers and Replay need no cloud credentials.
Configure an endpoint (typically ending in `/openai/v1/`), a key, and only the
deployments you want to use. The project uses user-secrets ID
`2727d4aa-a3a5-484b-9447-91604761972b`. From the repository root:

```powershell
$project = "samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj"
dotnet user-secrets set "AI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/" --project $project
dotnet user-secrets set "AI:ApiKey" "<key>" --project $project
dotnet user-secrets set "AI:DeploymentName" "<chat-deployment>" --project $project
dotnet user-secrets set "AI:ImageDeploymentName" "<image-deployment>" --project $project
dotnet user-secrets set "AI:EmbeddingDeploymentName" "<embedding-deployment>" --project $project
```

`AI:DeploymentName` enables Chat, `AI:EmbeddingDeploymentName` enables Embeddings,
and `AI:ImageDeploymentName` enables Images and Chat's image-generation tool for
providers that support tool calling. The endpoint and key are required only
when at least one Azure deployment is configured.

**Debug builds embed your local user secrets. Do not distribute these builds
or commit credentials.** Azure prompts, images, and indexed text may leave the
device and incur charges. Recordings may also contain sensitive data; review
them before sharing.
