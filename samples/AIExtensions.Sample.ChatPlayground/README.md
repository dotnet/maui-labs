# AI Chat Playground

A .NET MAUI sample for comparing `Microsoft.Extensions.AI` providers. It has
Chat, Embeddings, and Images tabs. `Microsoft.Maui.Essentials.AI` provides
`AppleIntelligenceChatClient` for on-device chat and `NLEmbeddingGenerator`
for on-device embeddings; Azure OpenAI is optional. The app targets Android,
iOS, and Mac Catalyst, plus a Windows target with Azure and offline Replay
only. It does **not** target native macOS. Available providers vary by
platform; saved Chat recordings can be replayed offline.

## Build and run

Install the repo's pinned .NET 10 SDK and MAUI workload. Apple Intelligence
Chat requires iOS or Mac Catalyst 26+ on a supported device with a compatible
Xcode; Apple NaturalLanguage embeddings have broader Apple OS support but appear
only when the English sentence-embedding asset is installed (it may be absent
on simulators).
Azure-backed features also run on Android and Windows.

From the repository root on macOS, build before running the Mac Catalyst target:

```sh
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -t:Run -f net10.0-maccatalyst
```

For Xcode 27 preview builds, pass `-p:UseXcode27Preview=true` and select
`-f net10.0-ios27.0` or `-f net10.0-maccatalyst27.0`. Normal builds retain
the stable Apple targets. Both Apple apps register MAUI scene delegates.

## Optional Azure configuration

No cloud credentials are needed for the on-device providers or Replay. To
enable Azure, set an Azure OpenAI endpoint (typically ending in
`/openai/v1/`), an API key, and at least one deployment name in user secrets.
The project uses user-secrets ID `2727d4aa-a3a5-484b-9447-91604761972b`.
From the repository root, for example:

```powershell
dotnet user-secrets set "AI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:ApiKey" "<key>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:DeploymentName" "<chat-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:ImageDeploymentName" "<image-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
dotnet user-secrets set "AI:EmbeddingDeploymentName" "<embedding-deployment>" --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj
```

Only set the deployment names you intend to use: `AI:DeploymentName` enables
Chat, `AI:ImageDeploymentName` enables Images and Chat's image-generation tool,
and `AI:EmbeddingDeploymentName` enables Embeddings. The endpoint and key are
required only when at least one Azure deployment name is configured.
User secrets are **embedded in Debug builds** for device testing: never
distribute those builds or commit keys. Azure prompts, images, and indexed
text may leave the device and incur charges.

## What to try

- **Chat:** Choose a provider, send text, and switch between streaming and
  single-response modes. The multiline prompt uses Enter/Return for a new line;
  select **Send** to submit it. Structured JSON requests a summary, key points,
  a free-form category, and a constrained sentiment. The common .NET schema
  leaves fields optional for other providers, while Apple and Azure OpenAI
  require all four in their native structured-output schemas; key points may
  be empty. The transcript displays the provider's actual JSON, including extra
  fields, without rewriting it through the request schema. Restoring an
  auto-saved chat keeps the selected live client; without one configured,
  Replay remains the offline default. Apple and Azure support optional tools.
  Date/time, calculator, and connection status start checked; connection
  status returns JSON with network access and connection types, not SSIDs or
  IP addresses. **None** disables tool calls even when tools remain checked;
  **Auto** makes checked tools available, and a model may call them for
  unrelated prompts. Uncheck unneeded tools to prevent those calls. Azure
  can accept images and create them through a separately configured
  image-generation tool.
- **Embeddings:** Import documents, create an index, and search it. Apple
  on-device and configured Azure providers are available. Imported content
  and indexes are separate from chats.
- **Images:** Generate from text or edit a single source image and inspect the
  result. Configured Azure providers are offered; there is no on-device image
  provider in this branch. The Images tab does not save generated results.

The app saves one Chat recording locally for replay. Use the Chat **More**
menu to load a bundled example without credentials, or import/export a
recording; **New** replaces the current chat. Replay is read-only and returns
successive recorded turns without calling a model or checking new prompts.
Switching from a chat with images to a text-only provider requires clearing
that chat first.

## Hybrid chat experiment

On iOS or Mac Catalyst 26+, a separate **Hybrid (local + cloud)** client option
uses `HybridChatClient`, an ordinary `IChatClient` registered in `MauiProgram.cs`,
built on the released `RoutingChatClient` and `FailoverChatClient` APIs and
wrapping dedicated `AppleIntelligenceChatClient` and Azure OpenAI clients. The existing direct
Apple, Azure, and Replay options remain available. Hybrid needs an enabled,
available Apple Intelligence model; Android and Windows have no local chat
backend and do not offer Hybrid.

For each turn with cloud configured, the local model reads the text conversation and system
instructions and returns a constrained routing decision. Greetings such as
`Hi`, small rewrites, and short summaries are candidates for local execution;
complex tasks are candidates for cloud execution. These are model decisions,
not deterministic rules or a guarantee that the chosen model can answer.
Routing adds a local model call before the answer.
Apple's context and generation limits still apply to routing and local answers.
Start a new chat if the local model reports a context-window limit; the demo
does not silently truncate history or send it to cloud to bypass that error.

Hybrid settings choose the cloud payload:

- **Local summary** (default): the local routing call also prepares a
  self-contained task and relevant context, attempting to remove or replace
  personal information. The conversation handoff contains only this summary,
  not the original history or system instructions. Generation settings and
  response formats are forwarded unchanged, including any schema descriptions
  and values; do not put personal data in those options. **Redaction is best-effort, not a privacy
  guarantee.** Do not use secrets or data that must never leave the device.
- **Original conversation**: Azure receives the full text conversation and
  current system instructions. This explicitly bypasses the summary/redaction
  step.

Selecting Hybrid opts into automatically sending cloud-routed payloads to the
configured Azure deployment; there is no per-turn approval dialog. The direct
Azure option always sends the original conversation regardless of the Hybrid
setting. Responses retain the actual leaf provider's standard `ModelId`,
displayed by the ordinary chat UI and preserved in recordings and replay.
There are no custom routing tags, provider colors, or per-response provenance classes.

The payload setting travels with the request, rather than changing shared
client state:

```csharp
var options = new ChatOptions
{
    AdditionalProperties = new()
    {
        [HybridChatClient.OriginalCloudPayloadOption] = false, // true sends original text
    },
};
var response = await hybridClient.GetResponseAsync(messages, options, cancellationToken);
```

`GetStreamingResponseAsync` applies the same policy. The router owns its
dedicated clients; do not pass it clients whose lifetime is managed elsewhere.

Without an Azure chat deployment, Hybrid answers locally without classification.
There are no internet/reachability checks in this path: with cloud configured,
the local classifier still runs offline, and a cloud-routed turn attempts Azure.
Eligible cloud transport errors, timeouts, throttling, and service errors fall
back to the original local conversation only before a response starts. Once
a streaming update has been emitted, failures are reported rather than
combining partial cloud and local answers. Authentication errors, invalid
routing output, and local-model failures are surfaced; none cause a silent
handoff of original text to cloud. Caller cancellation never triggers fallback,
and local fallback failures propagate without another cloud attempt. The nested
failover allows at most two answer attempts and owns the streaming commitment
boundary; classifier and summary-preparation failures occur outside it.

This initial experiment is **text-only, with no tool calling or images**.
Start a new chat before selecting Hybrid if the current conversation contains
tools or images. This avoids forwarding opaque provider state or replaying
tool actions during fallback. Local preprocessing and the cloud request are
one recorded interaction; preprocessing is not added as an assistant turn.
Recordings still contain the **original local conversation**, so exported
recordings are not anonymized by the cloud-payload setting.
Provider state, raw representations, extra properties, reasoning settings, tools,
and images are not forwarded. Summary-cloud requests also omit raw instructions
and stop sequences, while retaining supported generation knobs and response format.

Only this sample and `Microsoft.Maui.AI.Chat.Tests` override the repository's AI
pins: `Microsoft.Extensions.AI` 10.10.0, Abstractions and OpenAI adapter 10.10.1,
and OpenAI 2.14.0, with the minimum required runtime dependency updates. The
routing/failover APIs remain experimental (`MEAI001`); unrelated products retain
their existing versions.

## Organization

`Chat/`, `Embeddings/`, and `Images/` each contain their own services, views,
and view models. Each feature registers its page and supporting services
with dependency injection. `MauiProgram.cs` registers all Chat providers as `IChatClient`,
including Hybrid; the conversation pipeline has no Hybrid-specific execution
path. Hybrid selects request-bound clients, leaving dispatch and streaming/failover
mechanics to the library. `MainWindow` composes the three registered pages
as tabs. Reusable controls are in `Views/`, and shared configuration, image
input, and atomic storage are in `Services/`. Chat recording and document
indexing remain separate.
