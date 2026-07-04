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

## Notes

- Requires the .NET MAUI workload (`dotnet workload install maui`).
- The app targets **Microsoft.OpenApi 2.0.0** (the version .NET 10's ASP.NET ships), matching the
  library — see the library README.
- Experimental; local-developer sample only. The user-secrets embedding target bakes secrets into
  the app binary, which is acceptable **only** because this is never published.
