# Microsoft.Maui.StartupProfiling

A lightweight helper for .NET MAUI startup profiling. Reference this package in your MAUI app, call `StartupProfilingMarker.Complete()` when startup is logically finished, and the `maui profile` CLI command will stop the trace automatically.

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
maui profile \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

The trace stops automatically when `Complete()` fires. No need to press Enter manually.

## Environment variables

| Variable | Values | Effect |
|---|---|---|
| `MAUI_STARTUP_PROFILING` | `1` / `true` | Indicates the app is running in a profiling session. Check `StartupProfilingMarker.IsProfilingSession` to gate profiling-only code paths. |
| `MAUI_STARTUP_PROFILING_AUTO_EXIT` | `1` / `true` | Process exits with code 0 immediately after `Complete()` is called. Useful for automated CI pipelines. |

## How it works

- The package registers an `EventSource` named `Microsoft.Maui.StartupProfiling` via a `[ModuleInitializer]` so the provider is visible to dotnet-trace from the very first moment the assembly loads.
- Calling `Complete()` emits a `StartupComplete` event on that provider.
- `dotnet-trace --stopping-event-provider-name` / `--stopping-event-event-name` stops collection when it observes this event.
