# Microsoft.Maui.StartupProfiling

A lightweight helper for .NET MAUI startup profiling. Reference this package in your MAUI app, call `StartupProfilingMarker.Complete()` when startup is logically finished, and `maui profile` can stop the trace automatically and then request a graceful app exit so PGO data has a chance to flush.

## Usage

### 1. Add the package

```xml
<PackageReference Include="Microsoft.Maui.StartupProfiling" Version="*" />
```

### 2. Call `Complete()` at the end of startup

Call it from wherever you consider startup "done" — for example, after your first `ContentPage` is fully constructed, or after the first navigation completes:

```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    StartupProfilingMarker.Complete();
}
```

Or anywhere in your `App.xaml.cs`:

```csharp
public App()
{
    InitializeComponent();
    MainPage = new AppShell();
    StartupProfilingMarker.Complete();
}
```

### 3. Profile with `maui profile`

```sh
maui profile
```

If the app references `Microsoft.Maui.StartupProfiling` and no fixed `--duration` or custom `--stopping-event-*` options are supplied, `maui profile` automatically uses the `Microsoft.Maui.StartupProfiling/StartupComplete` stopping event. The trace stops when `Complete()` fires, and the CLI then asks the app to exit cleanly.

You can still override that behavior explicitly:

```sh
maui profile \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

## Environment variables

| Variable | Values | Effect |
|---|---|---|
| `MAUI_STARTUP_PROFILING` | `1` / `true` | Indicates the app is running in a profiling session. Check `StartupProfilingMarker.IsProfilingSession` to gate profiling-only code paths. |
| `MAUI_STARTUP_PROFILING_AUTO_EXIT` | `1` / `true` | Process exits with code 0 immediately after `Complete()` is called. Useful for automated CI pipelines. |
| `MAUI_STARTUP_PROFILING_EXIT_HOST` | host name / IP | Optional explicit host for the CLI's exit-control channel. Mostly useful for debugging custom launch flows. |
| `MAUI_STARTUP_PROFILING_EXIT_PORT` | TCP port | Optional explicit port for the CLI's exit-control channel. If omitted, the helper derives it from the diagnostic port. |

## How it works

- The package registers an `EventSource` named `Microsoft.Maui.StartupProfiling` via a `[ModuleInitializer]` so the provider is visible to dotnet-trace from the very first moment the assembly loads.
- Calling `Complete()` emits a `StartupComplete` event on that provider.
- `dotnet-trace --stopping-event-provider-name` / `--stopping-event-event-name` stops collection when it observes this event.
- While the app is running under `maui profile`, the helper also opens a small TCP control channel back to the CLI and waits for an `exit` command so the process can terminate cleanly after the trace is finalized.
