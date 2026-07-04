# GenerativeUI.Sample.Garden (MAUI client)

A minimal .NET MAUI chat app for the Generative UI experiment. **This slice has no generative UI
yet** — just a plain chat. Its purpose is to prove the loop end to end: you talk to the model, and
the model discovers and calls the Garden REST API through the generic OpenAPI tools.

The model is given only the generic server-API tools (`list_endpoints`, `describe_endpoint`,
`describe_model`, `read_api`, `write_api` — from `Microsoft.Maui.AI.GenerativeUI.OpenApi`). It does
**not** know the Garden endpoints ahead of time; it discovers them at runtime from the server's
OpenAPI document, then calls them. Writes (create/update/delete, checkout) require in-app approval.

## Run it

1. **Start the server** (separate terminal):

   ```bash
   dotnet run --project samples/GenerativeUI.Sample.Garden.Server
   ```

   Note the URL it binds (e.g. `http://localhost:5225`).

2. **Configure AI credentials** (shared with the other AIExtensions samples):

   ```bash
   dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-azure-openai-endpoint>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
   ```

3. **Point the app at the server** if it isn't on the default `http://localhost:5225`:

   ```bash
   dotnet user-secrets --id ai-attributes-secrets set "Api:BaseAddress" "http://localhost:5225"
   ```

   > Android emulators reach the host machine through `http://10.0.2.2:<port>`, not `localhost`.

4. **Run the app** (Mac Catalyst / Windows for the quickest loop):

   ```bash
   dotnet build samples/GenerativeUI.Sample.Garden -f net10.0-maccatalyst -t:Run
   ```

## Try

- "What products do you have?"
- "Show me the basil seeds."
- "Add two packs of tomato seeds to my cart." *(approve the write)*
- "What's in my cart?"
- "Delete the potting soil." *(approve)*

## Drive it with DevFlow

The app registers the [DevFlow](../../src/DevFlow) agent (`AddMauiDevFlowAgent()`), so you can
drive and inspect it from the `maui devflow` CLI (or MCP tools) — handy for iterating on the loop
without clicking. With the app running:

```bash
maui devflow list                                   # find the agent + its --agent-port
maui devflow ui screenshot --agent-port <port>      # see the current screen
maui devflow ui tree --agent-port <port>            # inspect the visual tree
maui devflow ui fill <entryId> "what are the products?" --agent-port <port>
maui devflow ui tap --text Send --agent-port <port>
maui devflow ui tap --text Approve --agent-port <port>   # accept a write_api prompt
maui devflow network --agent-port <port>            # observe the REST round trips
```

> If several DevFlow apps are running they each get a distinct `--agent-port` (assigned by the
> broker); pass the one shown for **Generative Garden**. Referencing the DevFlow agent also pulls in
> the `macos` workload for its `net10.0-macos` target, so run `dotnet workload install macos` too.

## Notes

- Requires the .NET MAUI workload (`dotnet workload install maui`), plus the `macos` workload
  (`dotnet workload install macos`) because the DevFlow agent multi-targets `net10.0-macos`.
- The app targets **Microsoft.OpenApi 2.0.0** (the version .NET 10's ASP.NET ships), matching the
  library — see the library README.
- Experimental; local-developer sample only. The user-secrets embedding target bakes secrets into
  the app binary, which is acceptable **only** because this is never published.
