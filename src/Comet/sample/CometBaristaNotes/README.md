# Comet Barista Notes

Barista Notes is the richer coffee sample for the evolved Comet surface. It keeps the existing interop-heavy shot logger, while the dashboard/detail flow acts as the current-surface reference path for incremental migration.

## What it demonstrates

- `TabView` as the runtime-safe sample tab shell for the dashboard, activity feed, and settings pages
- `CoffeeDashboardPage : Component<CoffeeDashboardState>`
- `CoffeeBeanDetailPage : Component<CoffeeBeanDetailState, CoffeeBeanDetailProps>`
- `Navigation.Navigate<TView>(props)` for typed navigation without route strings
- Existing `ShotLoggingPage` interop via `MauiViewHost` and Syncfusion gauges, launched through navigation instead of an eagerly-created root tab

## Key files

- `BaristaApp.cs` — root tab structure updated to use `TabView` + `NavigationView` tabs
- `Pages/CoffeeDashboardPage.cs` — component-based landing page with typed state filters
- `Pages/ActivityFeedPage.cs` — component-based activity tab that stays on the evolved surface
- `Pages/CoffeeBeanDetailPage.cs` — props-driven detail page opened through generic navigation
- `Pages/ShotLoggingPage.cs` — interop-heavy page showing how evolved samples can still host MAUI controls
- `Components/FormFields.cs` — shared card helpers tuned for runtime-safe list rows on iOS

## Build

From the repository root:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
dotnet build sample/CometBaristaNotes/CometBaristaNotes.csproj -c Release -f net10.0-maccatalyst
```

## Migration takeaway

You do not need to rewrite an entire app at once. Barista Notes keeps older Comet pages where they still fit, then layers new `Component` pages and typed navigation on top through `CoffeeDashboardPage` and `CoffeeBeanDetailPage`.
