# AIChat client/server sample

An experimental .NET 10 sample showing a separately launched MAUI chat client communicating with a
plain ASP.NET Core AG-UI server. The AppHost orchestrates the server only; it does not provision
Azure AI Foundry resources.

## Architecture

- **Shared** contains scenario identifiers, state and tool DTOs, and the source-generated JSON
  serializer context intended for both server and future client.
- **AgentServer** hosts protected AG-UI streaming endpoints, uses `DefaultAzureCredential`, and
  calls an existing Azure OpenAI / Foundry deployment.
- **AppHost** starts the server on `http://localhost:5018`, so the MAUI client can be launched
  independently and configured with that URL and its API key.
- **Client** renders exactly one externally-owned `AgentContext`, `AgentChatPresentation`, and
  `ChatComposerController` through both native and Blazor Hybrid `CopilotChatView` surfaces.

## Configuration and run

Store secrets outside source control (for example, as AppHost user secrets):

```sh
dotnet user-secrets set "AI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/" --project AIChat.ClientServer.Sample.AppHost
dotnet user-secrets set "Parameters:agui-api-key" "a-local-secret" --project AIChat.ClientServer.Sample.AppHost
dotnet run --project AIChat.ClientServer.Sample.AppHost
```

`Parameters:ai-model` defaults to `gpt-5.4-mini`. The server always requires `AGUI_API_KEY`;
`AI:Endpoint` is required outside `AI:Replay=true` test/replay configurations. It accepts a bearer key;
unauthenticated requests receive a non-descriptive `401`.
The AppHost first uses its own `AI:Endpoint` secret, then falls back to the existing
`ai-attributes-secrets` store so the direct and client/server samples can share a local endpoint
without putting it in configuration files.

`AI:Replay=true` starts the scenario catalog without an Azure endpoint and returns a deterministic
streaming response, so contract tests and local protocol experiments do not need Azure access.
Set `Parameters:ai-replay=true` when launching through the AppHost.

Run the server first, wait for `agentserver` to become healthy, then launch the MAUI client:

```sh
aspire start --isolated --apphost samples/AIChat.ClientServer.Sample/AIChat.ClientServer.Sample.AppHost/AIChat.ClientServer.Sample.AppHost.csproj
dotnet user-secrets set "AGUI:ApiKey" "a-local-secret" --project samples/AIChat.ClientServer.Sample/AIChat.ClientServer.Sample.Client
MAUI_AI_CHAT_MODE=Live AGUI__Endpoint=http://127.0.0.1:5018 \
  dotnet build samples/AIChat.ClientServer.Sample/AIChat.ClientServer.Sample.Client \
  -c Debug -f net10.0-maccatalyst -t:Run
```

The MAUI client defaults to synthetic, scenario-selected schema-v1 Replay. Set
`MAUI_AI_CHAT_MODE=Live` and configure an HTTPS `AGUI:Endpoint` for normal use. The server AppHost
uses local HTTP only for development. For Android development, prefer HTTPS or
`adb reverse tcp:5018 tcp:5018` with `http://127.0.0.1:5018`; `http://10.0.2.2:5018` is also
permitted only in Debug emulator builds. Release builds reject every HTTP bearer endpoint, and
the Debug Android network-security configuration permits cleartext only for `127.0.0.1` and
`10.0.2.2`. Apple manifests permit local networking so the same Debug loopback workflow works on
iOS simulators and Mac Catalyst; the managed Release guard still rejects HTTP. Prefer `adb reverse`
loopback or HTTPS. Never expose a bearer key over cleartext outside that narrowly scoped local setup.

`AGUI:ApiKey` is sent only as a bearer header and never logged. Both renderers share one
externally owned controller and its attachment/audio/live-speech services. Strict Record mode
uses JSON-only AG-UI request and state-event codecs; it records only behavior-relevant state
snapshots/deltas, never opaque provider data or protected reasoning. Set
`AI:Chat:FixtureDestination` to an explicit path outside the repository before selecting Record;
each scenario uses a distinct recording file, and the composition host exposes its manifest/save
outcome.

## Endpoints

`/` and `/health` are anonymous. All other endpoints require the API key:

`/agentic_chat`, `/backend_tool_rendering`, `/frontend_tools`, `/human_in_the_loop`,
`/tool_based_generative_ui`, `/agentic_generative_ui`, `/shared_state`, `/predictive_state`,
`/reasoning`, `/workflow`, and `/selective_approval`.

The reasoning endpoint uses the same Azure deployment through the Responses API and requests full
reasoning summaries. Foundry is optional as an existing endpoint only; this sample performs no
resource provisioning.
