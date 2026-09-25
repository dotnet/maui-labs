# AI Chat Playground

A .NET MAUI sample for comparing `Microsoft.Extensions.AI` providers. It has
Chat, Embeddings, and Images tabs. Windows and Apple use on-device models;
Azure OpenAI is optional. The app targets Windows (when built on Windows),
Android, iOS, and Mac Catalyst, **not native macOS**. Available providers vary
by platform; you can use saved Chat recordings offline.

## Build and run

Install the repo's pinned .NET 10 SDK and MAUI workload. For Windows on-device
AI, use Windows 11 24H2 (build 26100+) on supported hardware and run the app
**packaged**; the project includes the `systemAIModels` capability and
BuildTools.WinApp for MSIX `dotnet run`. Image generation requires a Copilot+
PC with an NPU and may download a large optional model. Apple Intelligence
Chat requires iOS or Mac Catalyst 26+ on a supported device with a compatible
Xcode; Apple embeddings use Natural Language separately. Azure-backed
features also run on Android.

From the repository root, for example:

```powershell
dotnet run --project samples\AIExtensions.Sample.ChatPlayground\AIExtensions.Sample.ChatPlayground.csproj -f net10.0-windows10.0.19041.0
```

On macOS, to run the Mac Catalyst target:

```sh
dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj -t:Run -f net10.0-maccatalyst
```

Do not add `WindowsPackageType=None` to make the Windows build easier: an
unpackaged app cannot access the Windows AI models. See
[Windows AI support](../../docs/ai/WINDOWS-AI.md) for SDK versions, packaging,
and feature limitations.

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
  single-response modes. Try schema-constrained JSON output. Apple and Azure
  support optional tools; Azure can accept images and can create images through
  a separately configured image-generation tool. Windows Chat supports text
  and JSON, but not tool calling or image input.
- **Embeddings:** Import documents, create an index, and search it. Apple
  on-device and configured Azure providers are available; Windows has no
  embedding provider. Imported content and indexes are separate from chats.
- **Images:** Generate from text or edit a single source image and inspect the
  result. Windows on-device and configured Azure providers are offered; there
  is no Apple or Android on-device image provider. The Images tab does not save
  generated results.

The app saves one Chat recording locally for replay. Use the Chat **More**
menu to load a bundled example without credentials, or import/export a
recording; **New** replaces the current chat. Replay is read-only and returns
successive recorded turns without calling a model or checking new prompts.
Switching from a chat with images to a text-only provider requires clearing
that chat first.
