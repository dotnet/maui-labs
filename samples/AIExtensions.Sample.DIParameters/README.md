# DIParameters — parameter binding shapes

A console app that shows the parameter shapes
AI Extensions supports beyond "plain value in, value out".

## What this demonstrates

All on a single `[ExportAIFunction]` method:

- **`[FromServices]`.** An `ITranslator translator` parameter is resolved from the
  service provider at invocation time. The AI never sees it in the schema.
- **Keyed DI.** `[FromKeyedServices("premium")] IModelProvider model` pulls
  the keyed registration.
- **Plain record argument.** `TranslationOptions` has no attribute — the generator
  treats it as a normal model-filled argument because it isn't marked for DI.
- **`CancellationToken`.** Bound automatically; never in the schema.

## Why this exists

Other attribute-based libraries typically only wrap `ReflectionAIFunction`
and expect you to hand-author `AIFunctionFactory.Create` calls to get DI
support. This sample is the proof the source generator emits the same
behaviour without runtime reflection — and adds first-class
`[FromServices]`/`[FromKeyedServices]` attribute support on top.

## Run

All four `AIExtensions.Sample.*` apps share one `UserSecretsId`
(`ai-attributes-secrets`), so you configure the endpoint once:

```bash
dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "https://<resource>.openai.azure.com"
dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<deployment-name>"

dotnet run --project samples/AIExtensions.Sample.DIParameters
```

Try: `Translate 'hello world' to pig latin with verbose output.`

## Inspecting the generated source

This csproj sets `EmitCompilerGeneratedFiles=true` so you can see exactly
what `Microsoft.Maui.AI.Attributes.Generators` emits for each tool context.
This is **not required** to run the sample. If you do not need to inspect
the generated source, you can remove the property from the csproj.

After a build, look under:

```
artifacts/obj/<ProjectName>/<Config>/<TargetFramework>/generated/Microsoft.Maui.AI.Attributes.Generators/Microsoft.Maui.AI.Attributes.Generators.AIToolContextGenerator/*.g.cs
```
