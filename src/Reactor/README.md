# Reactor ⚛️ (proposal)

Reactor is a **React-style** declarative UI framework for C#: you describe your UI as an
immutable **virtual element tree**, manage state with **hooks** (`UseState`, `UseEffect`,
`UseReducer`, `UseMemo`, …), and a **reconciler** diffs successive trees and patches only what
changed on the real native controls.

It originates from [microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor),
where it renders **WinUI 3** controls on Windows. This proposal is about giving Reactor a
**.NET MAUI backend** so the *same component source* can also run on macOS / iOS / Android.

> **Status:** This is a **proposal + external proof-of-concept**. No framework source is included
> in this PR yet — see [What this PR contains](#what-this-pr-contains). It is filed here because
> [dotnet/maui-labs](https://github.com/dotnet/maui-labs) is the home for experimental .NET MAUI
> UI frameworks, and Reactor is a natural sibling to [Comet](../Comet/README.md).

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<MyApp>("Hello Reactor");

class MyApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            Heading($"Count: {count}"),
            HStack(8,
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))
            )
        );
    }
}
```

This exact source has been built and run **unchanged** on **Mac Catalyst** through a MAUI backend
in an external PoC (a working counter window on macOS), validating that the hook/state engine and
the reconciler are platform-neutral.

## Why maui-labs

maui-labs already hosts [Comet](../Comet/README.md) — an experimental declarative C# UI framework
for MAUI — so it is the obvious home for a second one. Reactor and Comet are **complementary
mental models** rather than competitors:

| | Comet | Reactor |
|---|---|---|
| Programming model | MVU + reactive **signals** (tracks what you *read*) | React-style **hooks** + **reconciler** (diffs what you *return*) |
| Re-render unit | Only the lambdas that read changed state | Re-run `Render()`, diff the element tree, patch deltas |
| State | Plain C# state object + `SetState` | `UseState` / `UseReducer` / `UseEffect` call-ordered hooks |
| Heritage | Built for MAUI | Ported from WinUI 3 (`microsoft-ui-reactor`) |

Both share interest in **Yoga / Flexbox layout** (Comet ships `Comet.Layout.Yoga`; Reactor has its
own C# Yoga port upstream), which is a natural area for collaboration.

## How it would fit (proposed layout)

Following the per-product convention used by `src/Comet`, `src/Go`, etc.:

```
src/Reactor/
  README.md            ← this proposal
  src/
    Reactor.Core/      neutral element tree + hooks/state + reconciler (no WinUI, no MAUI)
    Reactor.Maui/      MAUI backend: maps elements → MAUI controls; hosts the app
  sample/
    Reactor.Sample.Counter/
  tests/
    Reactor.Core.Tests/  headless state/reconciler tests
```

Proposed packages (experimental / pre-release, as with the rest of this repo):

| Package | Description |
|---------|-------------|
| `Reactor.Core` | Platform-neutral element tree, hooks, and reconciler |
| `Reactor.Maui` | .NET MAUI backend (renders Reactor elements as MAUI controls) |
| `Reactor.Layout.Yoga` *(later)* | Yoga/Flexbox layout integration |

The PoC validated this split: a neutral core (`Element` records, `RenderContext`/hooks, the
reconciler) driven by an `IViewBackend` seam, with a thin MAUI backend that creates and patches
`StackLayout` / `Label` / `Button` and marshals re-renders onto the UI thread.

## What this PR contains

This PR is a **proposal only**. It deliberately contains **no source code from
`microsoft/microsoft-ui-reactor`** — just this document describing the experiment, how Reactor
relates to Comet, and the proposed shape for hosting it here.

Concretely, this PR adds:
- `src/Reactor/README.md` (this file)
- a `Reactor` entry in the repository's **Products** list, next to Comet

## Open questions for maintainers

1. **Scope.** Start as a **MAUI-native** declarative framework (the portable core + MAUI backend),
   or pursue the larger "write once, run on WinUI **and** MAUI" story (which requires decoupling
   the upstream WinUI-coupled engine — a much bigger effort)?
2. **Naming.** The upstream namespace is `Microsoft.UI.Reactor`, which reads as WinUI. For a MAUI
   repo a rebrand (e.g. `Microsoft.Maui.Reactor` or simply `Reactor`) may be preferable.
3. **Provenance.** How should code be brought over (fresh contribution vs. port) given CLA /
   licensing / `THIRD-PARTY-NOTICES` requirements?
4. **Relationship to Comet.** Ship as a fully independent product, or share infrastructure
   (e.g. Yoga layout, tooling) where it makes sense?

Feedback welcome before any framework code lands.
