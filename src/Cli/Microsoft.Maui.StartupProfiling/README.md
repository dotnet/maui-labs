# Microsoft.Maui.StartupProfiling

`Microsoft.Maui.StartupProfiling` is the helper used by `maui profile startup` for zero-touch startup profiling.

In the normal CLI flow, **you do not need to reference this package or change your app code**. The `maui profile startup` command injects the helper into the target MAUI app at build time and uses it to:

- register the `Microsoft.Maui.StartupProfiling` `EventSource`
- report when the first MAUI UI is ready
- optionally receive a graceful exit request from the CLI

## Normal usage: `maui profile startup`

Run profiling from the CLI:

```sh
maui profile startup --project MyApp.csproj
```

The current experience is:

- the CLI can prompt for the target framework, device, and trace format
- the app is built and launched in **Release**
- by default, tracing is **manual stop**: wait for the app to reach the screen you care about, then press **Enter** or **Ctrl+C**

If you want automatic stop behavior, provide an explicit condition such as:

```sh
maui profile startup \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

or:

```sh
maui profile startup --duration 00:00:15
```

## Optional custom/manual integration

If you want to use this helper outside the zero-touch CLI flow, you can reference it directly and call `StartupProfilingMarker.Complete()` yourself when startup is logically finished.

```xml
<PackageReference Include="Microsoft.Maui.StartupProfiling" Version="*" />
```

Example:

```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    StartupProfilingMarker.Complete();
}
```

## Environment variables

| Variable | Values | Effect |
|---|---|---|
| `MAUI_STARTUP_PROFILING` | `1` / `true` | Indicates that the app is running in a profiling session. |
| `MAUI_STARTUP_PROFILING_EXIT_HOST` | host name / IP | Optional explicit host for the CLI exit-control channel. |
| `MAUI_STARTUP_PROFILING_EXIT_PORT` | TCP port | Optional explicit port for the CLI exit-control channel. |

## How it works

- The helper registers an `EventSource` named `Microsoft.Maui.StartupProfiling` via a module initializer.
- `StartupProfilingMarker.Complete()` emits the `StartupComplete` event on that provider.
- When the CLI injects the bootstrap source, it waits for the first MAUI page handler to exist and then calls `Complete()` from inside the app assembly.
- During `maui profile startup`, the helper can also connect back to the CLI over a small TCP exit-control channel so the app can terminate cleanly after trace finalization.

### Notes on MIBC generation

- The `maui profile startup` trace flow includes the runtime JIT/R2R provider needed for `dotnet-pgo create-mibc`.
- Android startup-profile runs also inject the same core runtime-PGO knobs used by the known-good `dotnet-optimization` IBC flow, including `DOTNET_TieredPGO=1` and `DOTNET_ReadyToRun=0`.
- This produces a valid startup MIBC based on the methods observed in the trace.
- If you dump a resulting `.mibc` and only see a `Methods` list, that usually means the trace did not contain the raw sample/LBR data needed for richer SPGO block/edge attribution.
- Marker- or duration-based stop conditions are more reliable than force-closing the app when you need a fully finalized trace file.
