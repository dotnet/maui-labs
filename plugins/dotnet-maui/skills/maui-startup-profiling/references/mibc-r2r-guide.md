# MIBC / ReadyToRun Guide

Read when trace shows significant .NET Runtime time (JIT, assembly loading). MIBC-based Partial R2R pre-compiles startup-critical methods while keeping app smaller than full R2R.

## Concepts

| Term | Meaning |
|---|---|
| R2R | AOT native code in .NET assemblies. Eliminates JIT at startup. |
| Partial R2R | Only PGO-profiled methods pre-compiled. Smaller than full R2R. |
| MIBC | Profile format listing methods from trace. Guides `crossgen2`. |

## Platform Support

- .NET 11 and newer: CoreCLR default on **all platforms** (Android, iOS, Mac Catalyst, Windows)
- MIBC/R2R works on any CoreCLR target — collect per-platform profiles
- No effect on Native AOT apps

## Workflow

### 1. Collect

```sh
maui profile startup \
  --project MyApp.csproj \
  -f net11.0-android \
  --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

Outputs `.mibc` + `.nettrace`. No manual conversion needed.

> If `--format mibc` unavailable (older CLI), collect `--format nettrace` then:
> `dotnet-pgo create-mibc --trace <name>.nettrace --output <name>.mibc`

### 2. Add to project

```sh
mkdir -p pgo/android
cp <name>.mibc pgo/android/startup.mibc
```

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-android'))">
  <_ReadyToRunPgoFiles Include="pgo/android/*.mibc" />
</ItemGroup>
<ItemGroup Condition="$(TargetFramework.Contains('-ios'))">
  <_ReadyToRunPgoFiles Include="pgo/ios/*.mibc" />
</ItemGroup>
```

### 3. Build

MAUI apps on CoreCLR use Composite Partial R2R by default in Release. `_ReadyToRunPgoFiles` consumed automatically by `crossgen2`. No extra MSBuild props needed.

If you had `<RunAOTCompilation>true</RunAOTCompilation>` for full R2R, consider removing it — custom MIBC gives better size/speed tradeoff.

### 4. Re-measure

```sh
maui profile startup \
  --project MyApp.csproj \
  -f net11.0-android \
  --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

Expect reduced JIT samples in Runtime category.

## Troubleshooting

**`dotnet-pgo create-mibc` empty output** — only when manually converting. Ensure `--format nettrace` was used (not speedscope-only).

**Still slow after MIBC** — check correct platform/arch, verify `_ReadyToRunPgoFiles` picked up (build with `/v:diag`, search `ReadyToRunPgo`). Bottleneck may have shifted category — re-analyze.

**When to regenerate** — major dependency updates, significant architecture changes, SDK upgrades. Minor code changes don't invalidate — unknown methods just JIT as usual.

## References

- [Runtimes and compilation in .NET MAUI](https://learn.microsoft.com/dotnet/maui/deployment/runtimes-compilation)
- [dotnet/maui PR #33234](https://github.com/dotnet/maui/pull/33234)
- [dotnet-pgo docs](https://learn.microsoft.com/dotnet/core/tools/dotnet-pgo)
