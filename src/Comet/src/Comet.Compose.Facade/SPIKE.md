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

## Composition entry point — confirmed signature

From `Xamarin.AndroidX.Compose.UI.Android` 1.11.2 (ikdasm):

```
ComposeView.SetContent(Kotlin.Jvm.Functions.IFunction2 content)
  // JNI: setContent.(Lkotlin/jvm/functions/Function2;)V
```

So C# drives Compose by implementing a `Kotlin.Jvm.Functions.IFunction2`
(the composable lambda `(Composer, Integer) -> Unit`) and passing it to
`SetContent`. The lambda's `Invoke(composer, changed)` re-enters our emit code,
threading the `composer` into each composable call.

## Still to prove (the hard part)

1. **Composable invocation from C#.** `@Composable` functions use a special
   calling convention — the implicit `Composer` parameter plus a `changed` int
   bitmask. Whether the Xamarin bindings surface `Text`/`Button`/`Column` as
   directly-callable C# methods (threading `IComposer`) or whether they must be
   invoked via JNI (as Peppers' generator does) is the next thing to determine —
   initial ikdasm probing did not surface a public `TextKt.Text(...)` overload,
   which suggests the JNI-invocation path (study Peppers' generator output).
2. **`ComposeView.SetContent(IFunction2)` with a C# composable lambda.** Entry
   point now known; implement the `IFunction2` in C#.
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
