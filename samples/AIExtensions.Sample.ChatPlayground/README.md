# Chat, Embeddings, and Images playground

`AIExtensions.Sample.ChatPlayground` is a .NET MAUI sample for the real
`IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`, and `IImageGenerator`
abstractions. Tabs switch between **Chat**, **Embeddings**, and **Images**.
Each page shares a responsive header, settings sidebar, and content/input layout;
settings become an overlay on narrow windows. The pages show info buttons beside
optional controls rather than introducing provider-specific service branches.

> **Keep Debug builds private.** Debug builds embed the shared local `secrets.json`
> so the device can use configured Azure services. Release builds do not embed
> user secrets. Do not commit, publish, or share a Debug app with embedded credentials.

## Chat: one current recording

Select a live `IChatClient`, enter a prompt, and send. Streaming and Structured
JSON are independent switches, covering text/JSON with both `GetResponseAsync`
and `GetStreamingResponseAsync`. The sidebar also has system instructions,
optional generation settings, tool modes, a local date/time tool, and a calculator.
Azure chat can offer image input, a reasoning summary, and a hosted image-generation
tool when their configured providers support them. The Apple client does not
offer image input. Unsupported options fail visibly rather than silently changing
providers or response modes. Tool calls, their results, reasoning summaries,
image bytes, and streaming updates appear in the transcript.

Completed live interactions are atomically autosaved to
`FileSystem.AppDataDirectory/chat-playground/current.json`. This is deliberately
**one current chat**, not a chat library or a chat-search index. **New** replaces
the current recording. The header's **More** menu imports a versioned recording from a file (replacing
the current chat only after validation), loads a bundled example, or exports a
snapshot to the system share sheet. The example is an already-captured, real
two-turn Azure chat with an attached image, an image-generation tool call and
generated image, and a streamed follow-up; replay needs no Azure credentials
or network access. Loading it replaces the current chat. Export before loading
it or starting a new chat if you want to keep your recording. Export writes to
cache; opening the share sheet does not guarantee the user saved a copy.

Select **Replay** in chat settings to replace the live composer with a
read-only playback bar. **Restart**, **Next turn**, **Play all**, and **Stop**
are in that bar. The autosaved chat replays when the
page first opens; after full replay, select a live client to continue. Partial
replay cannot accidentally become a live request. The same conversation
executor and response projector handle live and replayed messages. Replay
checks messages and streaming mode and reports the first mismatched JSON path.
Imports must use the current recording schema; no automatic legacy migration
or saved-chat catalog is included.

## Embeddings: imported documents

Use **Import** in the Embeddings header to choose a UTF-8 `.md`, `.markdown`, or
`.txt` file (up to 256 KB), or load the packaged sample document. Documents are
not chat messages. Select an `IEmbeddingGenerator` and optional dimensions in
settings, then press **Index documents**. Enter a query and press the round
**Search** button; it embeds the query with that same selected generator and
ranks the previously indexed document excerpts by cosine similarity. Search
never builds an index implicitly and has no fake text-search fallback. Without
an embedding generator you can still import documents, but cannot index or
search them. **Clear index** removes only the selected model/dimension index;
**Clear documents** deletes the imported files and all their indexes, not chat.

Imported text is stored separately under
`FileSystem.AppDataDirectory/embedding-playground/documents/`. Derived vectors
are persisted under `embedding-playground/indexes/`, keyed by a hash of the
generator's model identity and requested dimensions. In-memory caches avoid
reloading every index during a session. Importing another document does not
index it; a search reports that new or changed documents need explicit indexing.
Changing models, replacing a deployment behind the same name, or changing
dimensions requires a distinct index. Damaged indexes give an actionable
search error and can be rebuilt by **Index documents**; the imported text is
preserved. If a cloud embedding generator is selected, document text and search
queries leave the device on Index/Search and may incur charges. There is no
confirmation dialog in this sample; choose an on-device generator if text must
stay local.

## Images: one generated output

Select an `IImageGenerator` in image settings. The composer accepts a prompt
and, for edit-capable generators, an optional original image from the picker
or packaged sample. Its round send button generates or edits an image and
shows a single preview above; if multiple images are returned, it shows the
first and reports the count. Optional size and media type use provider defaults
when unset. The request is cancellable. Results are not added to chats or saved
by this page. No image model is simulated when none is registered.

## How the sample is organized

| Area | Responsibility |
| --- | --- |
| `App`, `MauiProgram`, `PlaygroundTabs` | Register real clients and generators, resolve the Chat, Embeddings, and Images pages through DI, and compose the tabbed root for each window. |
| `Features/Chat/{Views,ViewModels,Models,Services}/` | Chat page, settings, composer, transcript models, provider-agnostic conversation executor, and current-chat autosave. |
| `Features/Chat/Recording/` | Versioned, sample-contained record/replay protocol and `IChatClient` wrappers, independent of document indexing. |
| `Features/Embeddings/{Views,ViewModels,Models,Services}/` | Document page and settings, lazy document store, and explicit per-model index/search; portable services have no MAUI or chat dependency. |
| `Features/Images/{Views,ViewModels,Models,Services}/` | Image page and settings plus one portable generation call accepting the selected `IImageGenerator`. |
| `Shared/{Controls,Models,Services,Storage}/` | Reused page layout, info tip, menus, image input, and atomic file writing. |

Presentation descriptors are attached to each generator or chat client, not
passed into portable services. To add another embedding model, register its
generator and descriptor in `MauiProgram.AddEmbeddingGenerators`:

```csharp
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
    new DescribedEmbeddingGenerator(
        () => new MyEmbeddingGenerator(),
        new EmbeddingGeneratorDescriptor(
            "local-model-v2", "My local model",
            "Embeds document text on this device.",
            "my-local-model/english/v2")));
```

`IndexIdentity` must change whenever the underlying model, language, or
revision changes, even if its vectors have the same dimensions. IDs and model
identities must be unique across registrations. The built-in Apple
NaturalLanguage identity includes the implementation and OS version; Azure's
includes endpoint, deployment, and `AI:EmbeddingIndexRevision`. Register
additional image providers as `DescribedImageGenerator` instances. The
document-index and image-generation services receive only their respective
selected abstraction and request settings.

On supported iOS and Mac Catalyst versions, Apple Intelligence provides chat
and NaturalLanguage provides on-device English sentence embeddings; those
capabilities are independent. Android can use configured Azure providers.
Without a live chat provider, Chat starts in Replay; the other pages still
work with their own providers. Nothing falls back to a mock or remote provider.

## Configure and verify

The sample reuses user-secrets ID
`2727d4aa-a3a5-484b-9447-91604761972b`. Supply values locally, without
copying credentials into the repository:

```bash
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:Endpoint" "<OpenAI-compatible endpoint ending in /openai/v1/>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ApiKey" "<API key>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:DeploymentName" "<chat deployment>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:ImageDeploymentName" "<image deployment>"
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:EmbeddingDeploymentName" "<embedding deployment>"
# Change this identity if an Azure embedding deployment changes its model in place.
dotnet user-secrets --id 2727d4aa-a3a5-484b-9447-91604761972b set "AI:EmbeddingIndexRevision" "<model revision>"
```

Only the deployments you want to use need configuring. Cloud image generation
does not require a chat deployment; embedding generation does not require
either chat or image generation. When selected, Azure image input, image
generation, and embeddings may send content off-device.

```bash
./eng/common/dotnet.sh build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
./eng/common/dotnet.sh build -t:Run samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
./eng/common/dotnet.sh test tests/AIExtensions/Microsoft.Maui.AI.Chat.Tests/Microsoft.Maui.AI.Chat.Tests.csproj
maui devflow list
```

The portable xUnit tests cover replay of real captured text/structured,
streaming/non-streaming, tool, image, and reasoning conversations; current-chat
import, restart, autosave, and damage recovery; independent document
import/index/search/clear, model isolation, invalid vectors, and cancellation;
and image generation/edit behavior. They run without a device or credentials.

### Windows Copilot Runtime

On Windows 11 24H2+ the packaged sample offers **Phi Silica** in Chat and
**Windows Copilot Runtime** in Images without Azure credentials. Both the Images
page and the Chat image-generation tool use the same local image generator.
Model readiness is checked on first use; image input in Chat also requires the
Windows image-description model. An unavailable model or unsupported request
reports an error rather than silently switching providers. Leave image size at
**Provider default** for the Windows generator; it does not accept an explicit
size. The Windows App SDK version used here is experimental.

Run the Windows target as a **packaged MSIX** so it receives the `systemAIModels`
capability (an unpackaged launch returns `AccessDenied`). The sample includes
both Windows.Universal and Windows.Desktop device families and bundles the
experimental Windows App SDK runtime:

```powershell
dotnet run --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj -f net10.0-windows10.0.19041.0
```

See [Phi Silica integration notes](../../docs/ai/PHI-SILICA.md) for tool-calling,
streaming, model availability, and other Windows-specific limitations.
