# Comet.Compose.Facade — interop spike notes

Phase 1, risk #1: *Can C# host a Jetpack Compose tree and drive it from managed
code on .NET 11?* This facade is Comet's **own** thin C# layer over the
`Xamarin.AndroidX.Compose.*` JNI bindings (we build our own; Jonathan Peppers'
`Microsoft.AndroidX.Compose` is reference only).

## Findings so far (2026-06-12)

| Question | Result |
|---|---|
| Do the `Xamarin.AndroidX.Compose.*` bindings **restore** on .NET 11 preview 5? | ✅ Yes |
| Do they **build** under the .NET 11 Android workload (minSdk 24)? | ✅ Yes, clean |
| Is `ComposeView` (the host Android view) reachable from C#? | ✅ `AndroidX.Compose.UI.Platform.ComposeView` |
| Are the snapshot-state primitives reachable? | ✅ `AndroidX.Compose.Runtime.IMutableState`, `SnapshotStateKt.MutableStateOf(value, policy)` |
| The Kotlin `$default` parameter gap (optional args lost in binding)? | ✅ Confirmed & solvable — pass the arg explicitly (e.g. `StructuralEqualityPolicy()`); a facade source generator will restore ergonomic call sites |

Version set that aligns on .NET 11 preview 5:
- `Xamarin.AndroidX.Compose.Runtime` 1.11.2.1, `.Runtime.Saveable` 1.11.2
- `Xamarin.AndroidX.Compose.UI[.Graphics/.Text/.Unit]` 1.11.2
- `Xamarin.AndroidX.Compose.Foundation[.Layout]` 1.11.2
- `Xamarin.AndroidX.Compose.Material3` 1.4.0.2
- `Xamarin.AndroidX.Activity[.Compose]` 1.13.0 (pin to resolve the transitive Activity graph)

## Still to prove (the hard part)

1. **Composable invocation from C#.** `@Composable` functions use a special
   calling convention — an implicit `Composer` parameter plus a `changed` int
   bitmask. Invoking `TextKt.Text(...)`, `ButtonKt.Button(...)`, `ColumnKt.Column(...)`
   from C# requires threading a `Composer` and the bitmask correctly.
2. **`ComposeView.SetContent(...)` with a C# composable lambda.** Needs a
   `Function2<Composer, int, Unit>` implemented in C# that re-enters our emit code.
3. **On-device render.** A `ComposeView` set as activity content rendering a
   C#-driven `Text` + `Button`, with a `MutableState` write recomposing the
   narrowest scope (proving steady-state JNI is limited to `setValue`).

Escape hatch if pure-C# composable invocation proves impractical: a tiny Kotlin
AAR holding only the composition skeleton (state holders + a node-kind `when`),
with C# still owning all logic. The `ICometBackendNode` protocol is unchanged
either way.

## Files
- `Comet.Compose.Facade.csproj` — package set + TFM (net11.0-android, minSdk 24)
- `InteropProbe.cs` — compiles the reachability checks above (de-risk signal)
