---
name: maui-app-architecture
description: >-
  Design and update .NET MAUI app architecture around dependency injection,
  MVVM, compiled bindings, Shell navigation, route registration, and testable
  services. USE FOR: MauiProgram service registration, ViewModel/page wiring,
  Shell GoToAsync routes, query parameters, adding x:DataType compiled binding
  annotations to XAML pages and DataTemplate item templates, service lifetime
  choices, and replacing DependencyService or service locator patterns. DO NOT USE FOR: project file/resource layout (use
  maui-project-structure), current API deprecation checks (use maui-current-apis),
  or runtime UI inspection (use maui-devflow-debug).
---

# MAUI App Architecture

Use this skill for app-level wiring: services, pages, ViewModels, bindings, and
navigation. Favor explicit, testable architecture over service locator patterns.

## Workflow

1. Inspect `MauiProgram.cs`, `AppShell.xaml`, page constructors, and existing
   ViewModels.
2. Preserve the app's UI pattern: XAML/MVVM, C# Markup, MauiReactor, Blazor
   Hybrid, or a mix.
3. Register dependencies in `MauiProgram.cs`.
4. Use constructor injection for pages and ViewModels.
5. Use compiled bindings with `x:DataType` in pages and data templates.
6. Register Shell routes once, near app startup. When asked how to register a
   Shell route, show the literal `Routing.RegisterRoute(...)` call, not only a
   prose summary.
7. Pass navigation data through route query parameters, `[QueryProperty]`, or
   `IQueryAttributable`.
8. Keep platform APIs behind interfaces so ViewModels remain unit-testable.

## Dependency Injection Guidance

| Dependency | Typical lifetime |
| --- | --- |
| Stateless API clients and data services | Singleton or typed `HttpClient` service |
| ViewModels with page state | Transient |
| Pages | Transient |
| User session/state service | Singleton if intentionally app-wide |
| Disposable per-flow services | Scoped only if the app has an explicit scope boundary |

Avoid calling `BuildServiceProvider()` inside `MauiProgram.cs`. Register the
type and let MAUI resolve it.

## Shell Navigation Pattern

```csharp
Routing.RegisterRoute(nameof(DetailsPage), typeof(DetailsPage));

await Shell.Current.GoToAsync($"{nameof(DetailsPage)}?id={Uri.EscapeDataString(id)}");
```

```csharp
public sealed partial class DetailsViewModel : ObservableObject, IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && value is string id)
        {
            Load(id);
        }
    }
}
```

## Compiled Binding Pattern

```xml
<ContentPage
    x:Class="MyApp.Views.ProductsPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:viewModels="clr-namespace:MyApp.ViewModels"
    xmlns:models="clr-namespace:MyApp.Models"
    x:DataType="viewModels:ProductsViewModel">
    <CollectionView ItemsSource="{Binding Products}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="models:Product">
                <Label Text="{Binding Name}" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

## Migration from Older Patterns

| Older pattern | Preferred MAUI pattern |
| --- | --- |
| `DependencyService.Get<T>()` | Register `T` in DI and inject it |
| Static service locators | Constructor injection |
| Stringly typed BindingContext setup everywhere | Page/ViewModel registration and compiled bindings |
| Unregistered Shell route strings | `Routing.RegisterRoute` plus constants or `nameof` |
| Platform code in ViewModels | Interface abstraction with platform implementations |

## Validation Checklist

- Services, pages, and ViewModels are registered consistently.
- No new service locator or `BuildServiceProvider()` usage was introduced.
- Pages and templates that bind to ViewModels/models have `x:DataType`.
- Routes are registered before use and query values are encoded.
- ViewModels remain testable without starting a MAUI app.
