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

## Mechanism — fully reverse-engineered (risk retired)

The composables are **not** callable binding methods (the .NET-for-Android binder
drops `@Composable` functions — they have no `$default` sibling overload and a
mangled JVM name from Kotlin value-class params). They must be invoked by **raw
JNI**. The complete, proven pattern (from Peppers' `ComposeBridgeGenerator`):

**1. Invoke a composable** — cached `FindClass` + `GetStaticMethodID` + `CallStaticVoidMethod`:
```
s_Button_class = FindClass("androidx/compose/material3/ButtonKt")      // cached global ref
mid            = GetStaticMethodID(s_Button_class, "Button", signature) // JVM sig incl. value classes
int defaults = (int)ButtonDefault.All;                  // $default bitmask: bit set = use Kotlin default
if (modifier is not null) defaults &= ~(int)ButtonDefault.Modifier;  // clear bit for each arg we pass
// marshal args + composer.Handle + $changed(0) + defaults into JValue*
CallStaticVoidMethod(s_Button_class, mid, args);
GC.KeepAlive(onClick); GC.KeepAlive(content); GC.KeepAlive(composer);   // across the JNI call
```
- The JVM **signature** ends `…Landroidx/compose/runtime/Composer;II)V` (or `;III)V`
  when params > ~10 → `$changed` splits across multiple ints). Material3 `Text`'s
  name is mangled `Text--4IGK_g` and packs `Color`/`TextUnit` value classes as `J` (long).
- `$default` enum per composable (`[ComposeDefaults("ButtonDefault","!onClick","modifier",…,"!content")]`):
  `!`-prefixed = required (no default bit). Provide an arg → clear its bit.

**2. SetContent entry**:
```
view.SetContent(ComposableLambdaKt.ComposableLambdaInstance(key:-1, tracked:…, block: <IFunction2 ACW>))
// block.Invoke(composer, changed) -> content(composer).Render(composer)
```

**3. Kotlin function params** (onClick, content, the SetContent block) are
`[Register("net/compose/…")]`'d `Java.Lang.Object` ACWs implementing the bound
`Kotlin.Jvm.Functions.IFunction0/2/3`, so Compose's bytecode-typed lambda slots
accept them. `Invoke()` returns `Kotlin.Unit.Instance`.

**4. The node model maps 1:1 onto Comet's protocol.** Peppers' `ComposableNode.Render(IComposer)`
is the moral equivalent of `ICometBackendNode`: a passive AST whose `Render` does the
JNI invocation, threading the composer to children via containers. Comet's
`ComposeXxxNode` will hold its `MutableState`s and read them in `Render` so a
`setValue` recomposes the narrowest scope (the steady-state-JNI-is-`setValue` goal).

## ⚠️ Strategic finding — build-vs-reuse of the bridge layer

Building Comet's **own** facade (per the plan) means reproducing **all of the
above** for ~65 composables: a multi-file Roslyn generator
(`ComposeBridgeGenerator` + `ComposeFacadeGenerator` + `ComposeDefaultsGenerator`
+ `ComposeCompanionGenerator`) emitting ~4,000 lines of raw-JNI bridges from
`[ComposeBridge]`/`[ComposeDefaults]` metadata, including value-class ABI
mangling and `$changed`/`$default` bit math. That is the bulk of Peppers'
~8,000-line library — and it is **commodity Compose-ABI plumbing with zero Comet
differentiation**. Comet's value-add is the reactive MVU core, the
`ICometBackendNode` protocol, Yoga layout, and typed storage — none of which this
plumbing touches.

**Recommendation to revisit with David:** the "build our own facade, Peppers as
reference only" decision was made before the bridge layer's true shape was known.
Options, fastest-to-most-control:
1. **Vendor/fork Peppers' `*.SourceGenerators` + bridges as the JNI layer**, build
   Comet's `ICometBackendNode` Compose backend on top (own the node model, reuse the
   ABI plumbing). Keeps full control of everything that differentiates Comet.
2. **Reference his NuGet** as a dependency (least control, fastest).
3. **Build our own generator** from this blueprint (max control, ~weeks of ABI work,
   no differentiation gained).

Open environment question for any option: Peppers targets `net10.0-android`;
confirm his composition renders under **.NET 11 preview 5** on the Pixel 5
(binding rebuild may be required).

## Decision & status (2026-06-12)

**Decided: vendor/fork Peppers' facade** as the bridge layer (this folder).
✅ Vendored `Microsoft.AndroidX.Compose` (facade + JNI-bridge generator, 309 files)
**builds clean under .NET 11 preview 5** — net10→net11 bump + inline version pins
(StdLib 2.4.0 for Core 1.19.0). The whole C#→Compose plumbing is now in-tree.

## Next: the retained↔composition backend bridge (Comet's value-add)

Comet's diff drives **imperative retained mutations** (`ICometBackendNode`:
ApplyProperty/InsertChild/…); the vendored facade is **declarative
rebuild-per-composition** (`ComposableNode.Render(IComposer)`). Bridge them in
`src/Comet/src/Comet/Platform/Compose/` (android TFM), referencing the vendored
facade:

- `ComposeNode : ICometBackendNode` base. Holds children in a vendored
  `MutableStateList` so structural diffs (InsertChild/RemoveChildAt/MoveChild)
  recompose the container. Implements `Render(IComposer)` (abstract).
- Per-control nodes (`ComposeTextNode`, `ComposeButtonNode`, `ComposeColumnNode`…):
  each property is a vendored `IMutableState` from
  `SnapshotStateKt.MutableStateOf(value, StructuralEqualityPolicy())`.
  - `ApplyProperty(id, value)` writes the matching state's `.Value` — the single
    steady-state JNI call (`setValue`).
  - `Render(composer)` builds the vendored composable (`new AndroidX.Compose.Text(
    (string)_text.Value)`) reading the states, so Compose tracks the read and
    recomposes only that scope on the next `setValue`.
- `ComposeBackendRoot`: owns the `ComposeView`, `SetContent(c => rootNode)`, set as
  activity content. `CreateBackendNode` overrides on Comet's control partials
  (`Text.Compose.g.cs` …) return the matching `ComposeNode` — the only reference, so
  unused controls + nodes trim away.

Then: wire `Comet.csproj` → conditional ProjectReference to the vendored facade for
`net11.0-android`; render the mini Todo on the Pixel 5 (Phase 1 gate); A/B vs the
`RESULTS.md` baseline.

Escape hatch (still open) if the retained↔composition bridge proves impractical: a
tiny Kotlin AAR holding only the composition skeleton (state holders + a node-kind
`when`), with C# owning all logic. The `ICometBackendNode` protocol is unchanged.

## Files
- `Comet.Compose.Facade.csproj` — package set + TFM (net11.0-android, minSdk 24)
- `InteropProbe.cs` — compiles the reachability checks above (de-risk signal)
