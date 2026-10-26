# Vendored: Microsoft.AndroidX.Compose

These two projects are vendored (forked) from **jonathanpeppers/Microsoft.AndroidX.Compose**,
MIT licensed (see `LICENSE.Microsoft.AndroidX.Compose`).

- Source: https://github.com/jonathanpeppers/Microsoft.AndroidX.Compose
- Commit: `29017064e3f3775d808d0b301ca994ac9eea7e9e`
- Vendored: 2026-06-12

## Why vendored

Comet-Next renders Android via Jetpack Compose. The C#→Compose **bridge layer**
(raw-JNI invocation of `@Composable` functions, the `$changed`/`$default` bitmask
ABI, value-class mangling, and the Roslyn generator that emits it) is ~33,000 LOC
of commodity Compose-ABI plumbing with zero Comet-specific differentiation. Per
David's decision (2026-06-12), we vendor this proven layer rather than re-derive
it, and build Comet's value-add — the reactive MVU core, the `ICometBackendNode`
protocol, Yoga layout, and typed storage — on top.

`Microsoft.AndroidX.Compose` provides the `AndroidX.Compose.*` C# facade
(`ComposableNode`, `Text`, `Column`, `Button`, `MutableState`, `SetContent`, …).
`Microsoft.AndroidX.Compose.SourceGenerators` emits the raw-JNI bridge bodies.

## Local modifications

- Facade `TargetFramework` bumped `net10.0-android` → `net11.0-android`.
- Compose package versions pinned in-csproj (Comet's repo has central package
  management disabled), mirroring the upstream `Directory.Build.targets` pins.
- Dropped the unused `Microsoft.AndroidX.Compose.Maui` MAUI-backend coupling.

Keep this attribution and the MIT `LICENSE.*` file when updating.
