---
name: xamarin-forms-migration
description: >-
  Plan and execute Xamarin.Forms to .NET MAUI app migrations. USE FOR:
  migration audits, new-project migration workflows, namespace/API changes,
  custom renderer to handler planning, DependencyService to DI, MessagingCenter
  replacement, platform capability parity, and deciding when to route to
  maui-current-apis, maui-custom-handlers, maui-platform-invoke, or slim
  bindings. DO NOT USE FOR: brand-new MAUI apps with no Xamarin.Forms code
  (use maui-app-architecture and maui-current-apis), implementing native
  library bindings directly (use android-slim-bindings or ios-slim-bindings),
  or backend/platform implementation work (use maui-platform-backend).
---

# Xamarin.Forms Migration

Use this skill to move a Xamarin.Forms app to .NET MAUI without blindly copying
obsolete patterns. Treat migration as an audit and phased rebuild on a fresh
MAUI project unless the user explicitly needs a small tactical port.

## Migration Tooling

Before migrating manually, check whether the .NET Upgrade Assistant can cover
the mechanical parts of the app:

```bash
dotnet tool install -g upgrade-assistant
upgrade-assistant upgrade <your-solution>.sln
```

Upgrade Assistant can help with project conversion and namespace/API rewrites.
Use this skill for decisions tooling cannot safely automate: renderer
classification, `DependencyService` lifetimes, `MessagingCenter` replacement,
platform capability parity, and validation planning.

## Migration Workflow

1. Inventory the current app:
   - Xamarin.Forms version, target platforms, platform projects, NuGet packages,
     renderers, effects, `DependencyService`, `MessagingCenter`, and native SDKs.
   - App startup, navigation, resource dictionaries, images/fonts, and platform
     entitlements/permissions.
2. Create a new .NET MAUI project or solution structure that matches the target
   app shape. Prefer this over editing the Xamarin.Forms project in place.
3. Move shared code first: models, services, view models, converters, resources,
   and XAML. Keep platform code out until shared code builds.
4. Update namespaces and APIs using `maui-current-apis`; do not introduce new
   `Xamarin.Forms` or `Xamarin.Essentials` references.
5. Migrate app startup to `MauiProgram.cs`, `CreateMauiApp`, DI registration,
   fonts, handlers, lifecycle events, and platform services.
6. Migrate UI and navigation incrementally. Keep Shell route names, modal flows,
   and deep-link behavior explicit.
7. Plan custom renderer/effect replacements:
   - simple native property tweak -> handler mapper customization;
   - reusable custom control -> custom handler;
   - non-visual platform API -> platform service via DI;
   - third-party native SDK surface -> slim binding.
8. Validate each migrated slice with build, platform launch, and accessibility or
   performance checks for changed UI.

## Common API Replacements

| Xamarin.Forms-era pattern | MAUI direction |
| --- | --- |
| `using Xamarin.Forms` | `using Microsoft.Maui.Controls` |
| `using Xamarin.Essentials` | `Microsoft.Maui.ApplicationModel`, `Microsoft.Maui.Devices`, `Microsoft.Maui.Storage`, or the specific MAUI namespace |
| `Device.BeginInvokeOnMainThread` | `MainThread.BeginInvokeOnMainThread` or `Dispatcher.Dispatch` |
| `Device.StartTimer` | `Dispatcher.StartTimer` or `IDispatcherTimer` |
| `Device.RuntimePlatform` | `DeviceInfo.Platform`, `OnPlatform`, partial classes, or DI services |
| `Color.Red`, `Color.FromHex` | `Colors.Red`, `Color.FromArgb`, or resource colors |
| `Application.Current.MainPage = ...` | `Window.Page` for intentional resets, or Shell/navigation abstractions for routine navigation |
| `DependencyService.Get<T>()` | Constructor-injected services registered in `MauiProgram.cs` |
| `MessagingCenter` | `WeakReferenceMessenger`, events, observable state, or explicit domain services |
| `ExportRenderer` / `CustomRenderer` | Handler mapper customization or `ViewHandler<TVirtualView,TPlatformView>` |

## DependencyService to DI

1. Move the cross-platform contract to shared code:

   ```csharp
   public interface IClipboardAuditService
   {
       Task TrackCopyAsync(string source, CancellationToken cancellationToken = default);
   }
   ```

2. Implement each platform in `Platforms/<Platform>/` or target-specific files.
3. Register the implementation in `MauiProgram.cs` with `AddSingleton`,
   `AddScoped`, or `AddTransient` based on lifetime needs.
4. Inject the service into view models or services. Avoid service locator calls
   from pages unless the app already uses that pattern and you are containing
   migration risk.

## MessagingCenter Replacement

Choose the smallest explicit communication pattern:

| Existing use | Replacement |
| --- | --- |
| View model updates a bound page | Observable properties and binding |
| Domain event across features | `WeakReferenceMessenger` or an app event aggregator |
| One service asks another to work | Inject the target service and call a method |
| Navigation trigger | Navigation service or Shell route call |
| Platform callback | Platform service event or `IObservable<T>` exposed through DI |

Do not migrate `MessagingCenter` by adding global static events everywhere.
Document message names, payload types, sender/receiver lifetimes, and threading
before replacing them.

## Renderer to Handler Planning

Audit each renderer and classify it before writing code:

| Renderer shape | MAUI migration |
| --- | --- |
| Changes one native property on an existing MAUI control | `Handler.Mapper.AppendToMapping` scoped to a subclass or named mapping |
| Needs a cross-platform custom control with bindable properties | Custom handler with `PropertyMapper` and optional `CommandMapper` |
| Subscribes to native events | Handler `ConnectHandler`/`DisconnectHandler` with explicit unsubscribe cleanup |
| Calls camera, health, maps, payment, or other SDK APIs | Platform service or slim binding; do not hide SDK workflow in a visual handler |
| Uses Xamarin.Forms effects only for styling | Prefer styles, visual states, or a handler mapper |

## Platform Capability Parity

For each target platform, record:

- required permissions, entitlements, manifests, Info.plist keys, and app
  capabilities;
- Essentials API coverage and behavioral differences;
- native SDK availability and version requirements;
- background tasks, push/deep link, secure storage, file picker, and biometric
  differences;
- UI parity risks such as safe areas, keyboard/soft input, gestures, and desktop
  pointer behavior.

## Cross-Skill Routing

- Use `maui-current-apis` for version-specific MAUI API replacements.
- Use `maui-custom-handlers` for handler mapper/custom handler implementation.
- Use `maui-platform-invoke` for non-visual platform services, partial classes,
  permissions, and lifecycle hooks.
- Use `android-slim-bindings` or `ios-slim-bindings` when a native SDK needs a
  maintainable binding surface.
- Use `maui-accessibility` and `maui-performance` for migrated UI risk areas.

## Validation Checklist

- The migration plan starts from an audited Xamarin.Forms solution, not guesses.
- New code targets MAUI namespaces and current APIs.
- Platform capabilities and permissions are mapped for every retained platform.
- Renderers/effects are classified before migration.
- `DependencyService` and `MessagingCenter` replacements have explicit lifetimes.
- Each migrated slice builds and runs on at least one intended platform.
