# GenerativeUI.Sample.Garden

An experimental .NET MAUI garden shop with two runtime UI modes:

- **Component Composer** (default) reads typed product data, prefilters an app-authored component
  catalog by available facets, and asks a dedicated structured-output model to select, prioritize,
  and arrange native components.
- **Baseline Full Generation** preserves the original research mode in which the chat model authors
  a complete primitive UI-DSL tree and inflates it at runtime.

The modes share the OpenAPI reducer/cache, `read_api`/`write_api` tools, automatic write approval,
the persistent `CanvasState.StateRoot`, JSON Patch, `itemsBind`, `IChatBridge`, and DevFlow agent.
The first Component Composer slice is intentionally read-only; Baseline Full Generation retains the
existing write and approval flows.

## Component Composer slice

The Garden app registers five ordinary MAUI `ContentView`s:

| Component | Data requirement | Purpose |
|---|---|---|
| `ProductHero` | Product name; optional image/emoji | Product identity and hero |
| `ProductCoreInfo` | Name, description, price | Core buying information |
| `DimensionsPanel` | Dimensions facet | Width, height, and depth |
| `ColorGallery` | ColorOptions facet | Native color swatches/gallery |
| `SeedGrowingTimeline` | SeedDetails facet | Planting, germination, and harvest |

`ProductDetailScaffold` keeps persistent Hero, Primary, Supporting, and Actions slots. Follow-up
plans retain `planId`, increment `revision`, preserve stable section IDs, and reconcile only affected
slot contents. Invalid plans receive one structured correction retry; a second failure uses a
deterministic ProductHero + ProductCoreInfo plan, never primitive generation.

Seed products expose `SeedDetails`. The Watering Can exposes `Dimensions` and `ColorOptions`. These
are optional adjacent records on the existing `Product` DTO, so cart/order and mutation flows do not
need a polymorphic hierarchy.

## Run it

1. Start the Garden server:

   ```bash
   dotnet run --project samples/GenerativeUI.Sample.Garden.Server
   ```

2. Configure AI credentials (shared with the AIExtensions samples):

   ```bash
   dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:ExpensiveDeploymentName" "<optional-stronger-deployment>"
   ```

3. Override the default server address when needed:

   ```bash
   dotnet user-secrets --id ai-attributes-secrets set "Api:BaseAddress" "http://localhost:5225"
   ```

   Android emulators reach the host through `http://10.0.2.2:<port>`.

4. Run the Mac Catalyst app:

   ```bash
   dotnet build samples/GenerativeUI.Sample.Garden -f net10.0-maccatalyst -t:Run
   ```

Use the **Runtime UI mode** picker to switch modes. Switching clears chat/canvas/composition state so
the modes never share incompatible tool history.

## Comparison prompts

Run the same prompts in both modes:

- "Show me the watering can."
- "How big is the watering can?"
- "What colors?"
- "Show me the basil seeds."

In Component Composer mode, the dimensions question promotes `DimensionsPanel`; the colors question
promotes `ColorGallery` with its richer `gallery` variant; seed products use
`SeedGrowingTimeline` and never receive dimensions/colors components.

The diagnostics strip reports provider-supplied main/composer token counts, latency, plan source and
validity, correction count, scaffold reuse, and section add/reuse/move/reconfigure/remove counts.
Missing provider usage is displayed as `n/a`, not estimated.

## Drive it with DevFlow

The app registers the DevFlow agent. With the app running:

```bash
maui devflow list
maui devflow ui tree --agent-port <port>
maui devflow ui screenshot --agent-port <port>
maui devflow ui fill <entryId> "show me the watering can" --agent-port <port>
maui devflow ui tap --text Send --agent-port <port>
```

Useful automation IDs include `GenerationModePicker`, `GenerationMetrics`,
`ProductDetailScaffold`, `ProductHero`, `ProductCoreInfo`, `DimensionsPanel`, `ColorGallery`, and
`SeedGrowingTimeline`.

## Requirements and status

- .NET 10 SDK.
- MAUI plus iOS/Mac Catalyst/macOS workloads for the Mac Catalyst + DevFlow build.
- Microsoft.OpenApi 2.0.0 in the app/server path, matching ASP.NET Core's .NET 10 OpenAPI stack.
- Experimental local-developer sample only. User secrets are embedded into the app binary and must
  not be used for a published app.

Deferred work includes review/write actions in composer mode, a primitive GeneratedPanel fallback,
rich descriptor policy, catalog source generation, and a development-time component scaffolding
skill.
