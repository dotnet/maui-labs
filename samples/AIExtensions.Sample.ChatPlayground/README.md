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
  a separately configured image-generation tool. Apple's attachment button
  is enabled on iOS/Mac Catalyst 27+, but sending requires a ready
  vision-capable model; on 26, Apple text chat remains available while the
  button is disabled. Apple does not generate images or silently fall back
  to Azure.
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

### Manual Apple image-input validation (macOS 27)

This is a **human test on macOS 27**, not a verified vision-inference result.
Use a Mac with Apple Intelligence enabled and a downloaded, vision-capable
Foundation Models model. Install .NET SDK 10.0.401, its matching MAUI/Apple
workload, and Xcode 27. Build with Xcode 27 without changing the system-wide
Xcode selection:

```bash
DEVELOPER_DIR=/Applications/Xcode-27.0.0.app/Contents/Developer \
  dotnet build samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -c Debug -t:Run \
  -p:UseXcode27Preview=true -p:ValidateXcodeVersion=false
```

The Mac Catalyst scene manifest and delegate are required for the Apple 27
app lifecycle. Confirm the three tabs open and Chat reaches client selection.
If the playground fails to launch (for example in MAUI or libswiftObservation),
collect the crash report and distinguish that from model or image-inference
failure. Debug can embed local secrets; do not distribute Debug builds, exported
chats, or unredacted logs. When running alongside another playground build,
override `ApplicationId` with a unique test ID to isolate app-private data.

1. In **Chat** settings select **Apple Intelligence** and confirm the add-image
   button is enabled. Choose **Use sample image** from its menu, verify the
   thumbnail, and ask "What is in this picture?" The answer must describe
   visible pixels, not just the words of the prompt. Compare with a text-only
   prompt. For an image-only turn, first use **More > Export to file** if you
   want to preserve the current chat, then **New** before attaching and
   sending without text.
2. Ask a follow-up about that image without reattaching it; verify that the
   answer uses image-bearing history. Repeat with streaming both on and off.
   API callers can supply
   `new DataContent(pngBytes, "image/png") { RawRepresentation = cgImage }`
   (or `UIImage` on iOS); the Apple client uses the native handle while encoded
   bytes remain portable for recording.
3. After a completed turn auto-saves, choose **Replay** in Chat settings;
   its playback bar replaces the composer. Use **Restart**, **Next turn**,
   and **Play all** to verify the image turn and follow-up render offline.
   After full replay, select Apple to continue live. Use
   **More > Export to file** to open the share sheet (opening it does not save
   a file); actually save the JSON, then **New**,
   **More > Import from file**, and select that JSON to restore and replay it.
   Import replaces the sole current chat after validation; there is no chat
   library or Find chats popup.
4. Cancel an in-flight request, confirm cancellation is visible, then send a
   text request to verify recovery. If Azure chat is configured, separately
   verify its image input; it can send content off-device. Apple must not fall
   back to Azure or call Azure's image-generation tool. Images generated or
   edited on the **Images** tab do not prove Apple image generation. The
   **Embeddings** tab indexes imported documents, not chat messages, and is
   independent of this image test.
5. On macOS/Mac Catalyst 26, Apple text remains selectable while the
   add-image button is disabled with an explanation. Direct Apple image calls
   fail with a 27.0 requirement; remote `https://` `UriContent` fails with
   `NotSupportedException` instead of downloading or ignoring it. On 27
   without a ready vision-capable model, capture the explicit error rather
   than reporting a successful inference.

Record the OS build, Xcode version, SDK/workload, model readiness, client,
input type, prompt, answer or exception, and whether follow-up and offline
replay preserved the image. Capture only redacted UI/status and logs. Neither
an Xcode 27 build nor a simulator's advertised capability demonstrates live
macOS 27 vision inference.
