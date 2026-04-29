# CometProjectManager

A sample demonstrating **Comet + MAUI Controls interop** — not a pure Comet app.

## What This Demonstrates

This project shows how to use Comet as a **controller/state layer** while rendering
UI with native MAUI controls and third-party libraries. It is an intentional hybrid
that exercises:

- **MauiViewHost wrapping** — Comet `Component<TState>` classes build MAUI control
  hierarchies (Grid, Label, Entry, etc.) and return them via `new MauiViewHost(...)`.
- **Syncfusion.Maui.Toolkit integration** — `SfCircularChart`, `SfEffectsView`,
  `SfPullToRefresh`, `SfShimmer`, `SfTextInputLayout`, and `SfSegmentedControl`.
- **CommunityToolkit.Maui** — toast notifications.
- **Dual navigation modes** — a pure-Comet `NavigationView` path (for snapshot testing
  via `--page=` CLI arg) and a MAUI `Shell` path (flyout/hamburger for production).
- **Reactive state with Signal\<T\>** — all data flows through Comet's reactive
  primitives, even though the rendered controls are MAUI-native.
- **FluentUI icon font** — custom font icons instead of image assets.
- **Dark/light theme switching** — via Syncfusion `SfSegmentedControl`.

## Architecture

```
ProjectManagerApp : CometApp        ← Comet entry (snapshot mode)
ShellMauiApp : Application          ← MAUI Shell entry (production mode)
AppNavigation                       ← Routing + mode detection

Pages/
  DashboardPage   : Component<T>   ← Comet state, renders MAUI Grid + Syncfusion charts
  ProjectListPage : Component<T>   ← Comet state, renders MAUI ListView
  ProjectDetailPage : Component<T> ← CRUD form with Syncfusion text inputs
  TaskDetailPage  : Component<T>   ← Task editing
  ManageMetaPage  : Component<T>   ← Category/status management

Controls/
  MauiControls.cs                  ← Pure MAUI custom controls (no Comet)
    CategoryChartControl           ← SfCircularChart wrapper
    TaskViewControl                ← SfEffectsView + CheckBox
    ProjectCardControl             ← Styled card with FluentUI icons

DataStore.cs                       ← Reactive data layer using Signal<T>
Models.cs                          ← Plain C# model classes
```

## Why It Looks Like "an Abomination"

Every page's `Render()` method builds a MAUI control tree (using `new MauiGrid`,
`new MauiLabel`, etc.) rather than Comet's own `Text()`, `VStack()`, etc.
The Comet framework is used only for:

1. State management (`Component<TState>`, `Signal<T>`, `SetState`)
2. Navigation (`NavigationView.Navigate`, `NavigationView.Pop`)
3. Hosting (`MauiViewHost` bridge)

This is a valid integration pattern for teams migrating from MAUI to Comet
incrementally, or for apps that need third-party MAUI controls (like Syncfusion)
that don't have Comet equivalents.

## Dependencies

| Package | Purpose |
|---------|---------|
| Microsoft.Maui.Controls | Native MAUI UI controls |
| CommunityToolkit.Maui | Toast notifications |
| Syncfusion.Maui.Toolkit | Charts, effects, shimmer, text input, pull-to-refresh |
| Comet | State management and navigation |

## Build

From the repository root:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
dotnet build sample/CometProjectManager/CometProjectManager.csproj -c Release -f net11.0-maccatalyst
```
