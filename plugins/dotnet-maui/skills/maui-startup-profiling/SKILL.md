---
name: maui-startup-profiling
description: >-
  Diagnoses slow .NET MAUI app startup using the `maui profile startup` CLI tool.
  Collects startup traces with MIBC format (producing both .mibc and .nettrace),
  analyzes hotspots with `dotnet-trace report topN`, and optionally categorizes
  CPU time by component via speedscope conversion. Recommends targeted optimizations
  and applies MIBC profiles for ReadyToRun on all CoreCLR platforms.
  USE FOR: "why is my app slow to start?", startup performance analysis, generating
  MIBC/PGO profiles for R2R, reducing app launch time, profiling MAUI startup.
  DO NOT USE FOR: runtime CPU profiling after startup, memory leak analysis,
  build performance, environment setup (use dotnet-maui-doctor).
---

# MAUI Startup Profiling

## Workflow

1. **Collect** → `maui profile startup --format mibc`
2. **Analyze** → `dotnet-trace report topN` or `scripts/analyze_speedscope.cs`
3. **Diagnose** → see `references/performance-tips.md`
4. **Apply MIBC** → see `references/mibc-r2r-guide.md`
5. **Re-measure**

## Step 1: Collect

Requires `maui` CLI (`dotnet tool install -g Microsoft.Maui.Cli`), .NET 11+ SDK, connected device/emulator.

```sh
maui profile startup \
  --project <path-to-csproj> \
  -f <target-framework> \
  --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

Outputs `<name>.mibc` (PGO profile) + `<name>.nettrace` (raw trace). Run `--help` for all options.

**Must use explicit stop.** Without `--stopping-event-*` or `--duration`, tool blocks on stdin.

| Platform | Auto-stop | Notes |
|---|---|---|
| Android | ✅ | CLI injects `StartupProfilingMarker` at build |
| iOS | ⚠️ #109 | Not wired yet. Add `Microsoft.Maui.StartupProfiling` NuGet + call `StartupProfilingMarker.Complete()` in `OnAppearing`, or use `--duration 00:00:15` |

## Step 2: Analyze

### Quick: `dotnet-trace report`

```sh
dotnet-trace report <name>.nettrace topN -n 30       # exclusive (self) time
dotnet-trace report <name>.nettrace topN -n 30 --inclusive  # inclusive time
dotnet-trace report <name>.nettrace topN -n 30 -v    # full signatures
```

What to look for:
- `coreclr!*`, `System.Reflection.*` → MIBC (Step 4)
- `ServiceProvider.GetService` → simplify DI
- App constructors → defer work
- XAML/handler frames → simplify first page, compiled bindings

### Deep: categorized speedscope analysis

```sh
dotnet-trace convert <name>.nettrace --format speedscope
dotnet run scripts/analyze_speedscope.cs -- <name>.speedscope.json \
  --app-namespaces MyApp.,MyLib. --top 15
```

Args: `--app-namespaces` (identify app code), `--top N`, `--json`.

Categories (precedence order):

| # | Category | Matches |
|---|---|---|
| 1 | App Code | user `--app-namespaces` prefixes |
| 2 | MAUI Framework | `Microsoft.Maui.*`, `Microsoft.Extensions.DependencyInjection.*` |
| 3 | .NET Bindings | `Java.Interop.*`, `Android.*`, `ObjCRuntime.*`, `UIKit.*` (managed) |
| 4 | .NET Runtime | `System.*`, `coreclr!*`, JIT, GC |
| 5 | BCL / Third-party | other managed frames |
| 6 | Platform / OS | `java.*`, `android.*`, `dalvik.*`, `objc_*`, native |
| 7 | Unknown | unresolvable |

> Reports **CPU sample distribution**, not wall-clock time. Use `adb logcat | grep ActivityManager` or Instruments for wall-clock.

## Step 3: Diagnose

| High % in | Action |
|---|---|
| App Code | `references/performance-tips.md` §Lazy Init, §Async, §Page Complexity |
| MAUI Framework | `references/performance-tips.md` §DI, §Shell, §XAML, §Compiled Bindings |
| .NET Runtime | apply MIBC (Step 4); §Trimming |
| .NET Bindings | batch managed↔native calls |
| Platform/OS | rarely actionable from app code |

## Step 4: Apply MIBC

Step 1 already produced `.mibc`. Copy into project, add to `.csproj`:

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-android'))">
  <_ReadyToRunPgoFiles Include="pgo/startup-android.mibc" />
</ItemGroup>
```

Full guide: `references/mibc-r2r-guide.md`. Re-measure to compare.

## Common Patterns

**DI-heavy** (Framework 45%, App 25%, Runtime 20%) → reduce registrations, factory methods, defer services.

**JIT-heavy** (Runtime 55%, Framework 20%) → MIBC + Partial R2R. See `references/mibc-r2r-guide.md`.

**Complex first page** (Framework 35%, App 30%, Bindings 20%) → simplify page, defer below-fold, use Shell.

**Sync I/O** (App 50%, Framework 25%) → async patterns, defer I/O past first render.
