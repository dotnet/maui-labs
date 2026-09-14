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

## Configuration and run

Store secrets outside source control (for example, as AppHost user secrets):

```sh
dotnet user-secrets set "AI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/" --project AIChat.ClientServer.Sample.AppHost
dotnet user-secrets set "Parameters:agui-api-key" "a-local-secret" --project AIChat.ClientServer.Sample.AppHost
dotnet run --project AIChat.ClientServer.Sample.AppHost
```

`Parameters:ai-model` defaults to `gpt-5.4-mini`. The server requires `AGUI_API_KEY` and
`AI:Endpoint` outside `AI:Replay=true` test/replay configurations. It accepts a bearer key;
unauthenticated requests receive a non-descriptive `401`.

`AI:Replay=true` starts the scenario catalog without an Azure endpoint and returns a deterministic
streaming response, so contract tests and local protocol experiments do not need Azure access.

## Endpoints

`/` and `/health` are anonymous. All other endpoints require the API key:

`/agentic_chat`, `/backend_tool_rendering`, `/frontend_tools`, `/human_in_the_loop`,
`/tool_based_generative_ui`, `/agentic_generative_ui`, `/shared_state`, `/predictive_state`,
`/reasoning`, `/workflow`, and `/selective_approval`.

The reasoning endpoint uses the same Azure deployment through the Responses API and requests full
reasoning summaries. Foundry is optional as an existing endpoint only; this sample performs no
resource provisioning.
