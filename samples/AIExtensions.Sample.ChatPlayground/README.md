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
Xcode; Apple NaturalLanguage embeddings have broader Apple OS support.
Azure-backed features also run on Android and Windows.

From the repository root on macOS, build before running the Mac Catalyst target:

```sh
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -f net10.0-maccatalyst
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -t:Run -f net10.0-maccatalyst
```

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
```

Set `AI:ImageDeploymentName` for Azure Images or the Chat image-generation
tool, and `AI:EmbeddingDeploymentName` for Azure Embeddings. The endpoint and
key are required only when at least one Azure deployment name is configured.
User secrets are **embedded in Debug builds** for device testing: never
distribute those builds or commit keys. Azure prompts, images, and indexed
text may leave the device and incur charges.

## What to try

- **Chat:** Choose a provider, send text, and switch between streaming and
  single-response modes. Try schema-constrained JSON output. Apple and Azure
  support optional tools; Azure can accept images and can create images through
  a separately configured image-generation tool.
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

## Organization

`Chat/`, `Embeddings/`, and `Images/` each contain their own services, views,
and view models. Each feature registers its page and selected real abstractions
with dependency injection. `MainWindow` composes the three registered pages
as tabs. Reusable controls are in `Views/`, and shared configuration, image
input, and atomic storage are in `Services/`. Chat recording and document
indexing remain separate.
