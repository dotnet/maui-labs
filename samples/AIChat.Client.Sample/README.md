# Direct AI Chat MAUI client

Experimental .NET 10 MAUI Razor sample for `Microsoft.Maui.AI.Chat`. It renders one externally
owned `AgentContext`, `AgentChatPresentation`, and `ChatComposerController` in both native
`CopilotChatView` and a Razor `CopilotChatView`.

## Modes and credentials

The checked-in default is safe offline `Replay`, using a **synthetic** schema-v1 fixture. Select
`Live`, `Record`, or `Replay` through `AI:Chat:Mode`; `MAUI_AI_CHAT_MODE` takes precedence.

```sh
dotnet user-secrets set "AI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/" --id ai-attributes-secrets
dotnet user-secrets set "AI:ApiKey" "YOUR-KEY" --id ai-attributes-secrets
dotnet user-secrets set "AI:DeploymentName" "gpt-5.4-mini" --id ai-attributes-secrets
dotnet user-secrets set "AI:ImageDeploymentName" "YOUR-IMAGE-DEPLOYMENT" --id ai-attributes-secrets
MAUI_AI_CHAT_MODE=Live AI__DeploymentName=gpt-5.4-mini \
  dotnet build samples/AIChat.Client.Sample -c Debug -f net10.0-maccatalyst -t:Run
```

This local developer sample uses `ApiKeyCredential`; never embed or distribute its credentials.
`AI:DeploymentName` defaults to `gpt-5.4-mini`; `AI:ImageDeploymentName` is optional. Record
mode additionally requires an explicit `AI:Chat:FixtureDestination` outside the source tree. The
destination can be a directory or filename; every selected scenario writes a distinct
`{scenario}.recording.json` artifact. Direct Azure responses are semantically normalized before
recording, so opaque provider raw payloads (including response raw data) are never serialized.
The strict recording sanitizer manifest and save outcome are exposed by the composition host.
Environment variables are registered after user secrets, so explicit launch-time overrides win.

Both native and Razor surfaces share one externally owned composer controller, including its
attachment, audio-recording, and live-speech services. The predictive-document card does not
commit immediately: Accept or Reject invokes the pending manual UI action exactly once, then
commits or clears the proposal.

Debug builds enable DevFlow and Blazor developer tools. Use `maui devflow mcp`, then
`maui_cdp_webviews`, `maui_cdp_source`, and `maui_cdp_screenshot` to inspect the Razor surface.
