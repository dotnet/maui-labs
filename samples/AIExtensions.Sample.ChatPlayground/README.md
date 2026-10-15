# AI Chat Playground

A .NET MAUI app for comparing on-device AI, Azure OpenAI, and recorded responses.
Runs on Android, iOS, Mac Catalyst, and Windows.

| Tab | What to try |
| --- | --- |
| Chat | Switch providers, stream replies, attach images, and record/replay conversations |
| Embeddings | Import documents, build an index, and search with Apple or Azure embeddings |
| Images | Generate or edit images with a configured Azure provider |

Apple Intelligence is available on Apple platforms; Gemini Nano is available on
supported Android devices. Azure is optional, and Replay needs no credentials
or model. Provider requirements and API capabilities are in the
[package README](../../src/AI/Microsoft.Maui.Essentials.AI/README.md).

## Build and run

Install the repo's pinned SDK and matching MAUI workloads. From the repository
root, run Mac Catalyst with Xcode 27:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

For iOS, use `-f net10.0-ios27.0` and select the device with `--device`.
For Android, install Java 17+ and the Android SDK, then run:

```sh
dotnet run --project samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj \
  -f net10.0-android --device <device-id>
```

Emulators can exercise Azure and Replay, but cannot run Gemini inference.

## Using the playground

Choose a Chat provider in settings, enter a prompt, and select **Send**.
Unavailable features are hidden: Gemini has no tools or structured JSON.
For Apple and Azure tools, uncheck the ones you do not need or select **None**.

The **More** menu loads a bundled recording or imports/exports chats; **New**
clears the current conversation. **Logs** shows local diagnostics without
exporting them. Sensitive payload logging is disabled, but exceptions may
still contain sensitive data.

## Optional Azure configuration

To enable Azure, set an endpoint, key, and the deployment names you want to use.
From the repository root:

```sh
cd samples/AIExtensions.Sample.ChatPlayground
dotnet user-secrets set "AI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/"
dotnet user-secrets set "AI:ApiKey" "<key>"
dotnet user-secrets set "AI:DeploymentName" "<chat-deployment>"
dotnet user-secrets set "AI:ImageDeploymentName" "<image-deployment>"
dotnet user-secrets set "AI:EmbeddingDeploymentName" "<embedding-deployment>"
```

Chat uses `DeploymentName`, Images and Chat's image-generation tool use
`ImageDeploymentName`, and Embeddings uses `EmbeddingDeploymentName`.
Leave unused deployment names unset.

User secrets are **embedded in Debug builds**: do not distribute those builds or
commit keys. Azure requests may send text/images off-device and incur charges.
