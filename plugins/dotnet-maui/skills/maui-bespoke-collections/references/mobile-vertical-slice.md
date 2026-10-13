# Mobile fast path and readiness checkpoints

For implementation, choose the user's priority target and prove one small slice
before paging or another backend. This makes existing selective-reading guidance
operational; it is not a new scaffolding tool or a universal list library.

## 1. Record scope before handlers

Use the [scenario brief](scenario-selection.md) and a compact platform ledger:

```text
target/backend | requested | priority | host/device available
minimum acceptable evidence | implemented | compiled | deployed
behavior | lifecycle | performance | blocked/deferred
```

Priority does not remove the other requested targets. Reconcile the ledger when
the user changes scope. Preserve existing deferred-target behavior; do not
migrate or stub an excluded frontend. Source inspection, compile, deployment,
runtime and measured performance are different states.

Initially read scenario selection, the chosen recipe/platform and needed
source/lifecycle material. Add [hosting](retained-content-hosting.md), interaction
or benchmark references only when that work is reached. Do not front-load every
recipe/platform or start several handlers before a first runtime result.

## 2. Unchanged baseline and bounded environment preflight

Inspect resolved SDK/packages/workloads, deployment minimum, native host and
available explicit device. Build/run the unchanged baseline before a renderer
rewrite. If there is no runnable app, create the smallest authorized host.
Build success does not prove signing, complete runtime assets or launchability.

Verify app/package identity, platform, window/viewport and actual native host.
When using automation, match the CLI/agent version/protocol and verify a usable
endpoint/tree/property/screenshot for **that app**, not just broker registration.
A selected agent can belong to another target; an explicit reachable endpoint can
also exist without catalog registration.

Delegate deployment/connectivity work to current DevFlow/debugging guidance when
applicable; do not install companion skills automatically or reproduce a full
toolchain manual here. Preserve pins and production configuration. App-local
toolchain or packaging changes require an actual project need and recorded
effects, not a shortcut to make a benchmark pass.

After two distinct failed diagnostic hypotheses, stop identical retries: inspect
the first meaningful failure, isolate a smaller probe and classify the blocker.
Record:

```text
failure class | evidence | changed hypothesis | next smallest probe | stop condition
```

If a probe is blocked, preserve scope/evidence state and report it; do not invent
API fallback, repeat full builds or silently drop a platform.

## 3. Compile the smallest host/bridge

Resolve ambiguous types explicitly, for example:

```csharp
using MauiView = Microsoft.Maui.Controls.View;
using MauiRect = Microsoft.Maui.Graphics.Rect;
// Android-only source:
using NativeView = Android.Views.View;
```

Check cell/holder constructors, native callback signatures, property/command
mappers, context conversion, one handler registration, and disconnect cleanup in
a small compile target against the consuming SDK before full-app integration.
Include the needed imports/aliases with any generated snippet.

Existing narrow version-stamped proof:
[maui-labs mobile sample](https://github.com/dotnet/maui-labs/tree/17ce2e9245387b0ab355df14498d41066d7818d6/samples/BespokeCollections)
at initial implementation commit `17ce2e9245387b0ab355df14498d41066d7818d6`:
SDK 10.0.401, MAUI 10.0.110, Android 36.1.69, Apple 26.5.10318.
Its compiled Android adapter overrides OnCreateViewHolder/OnBindViewHolder with
RecyclerView.ViewHolder and OnViewRecycled with Java.Lang.Object; UIKit registers
a CGRect cell constructor with initWithFrame: and uses a retained data-source
bridge. Read the actual sources for full signatures/nullable annotations.

That sample is **typed native metadata photo content**, not a certified
retained-MAUI variable-text fixture or every-platform implementation.
[Hosting](retained-content-hosting.md) describes the additional measurement/
logical-owner contract. Do not label those procedures newly compiled/run merely
because the native sample builds. MAUI internals are prior art, not public bases.

## 4. Gate each stage

| Stage | Exit evidence |
|---|---|
| One populated item | Intended host/viewport, accepted key/caption/content and fully visible text/chrome at the intended width |
| Small resident mixed list | Correct frames, zero/one cases, wrapped header/footer/margins and variable heights |
| Reuse/lifetime | Equivalent forward/reverse traversal, native/tree counts, reset state, source replacement/disconnect and late-completion rejection |
| Bounded source/paging | Real load/error/end, epoch/cursor consistency, retention/demand and keyed anchor tests |
| Additional target | Repeat its compile/deploy/behavior/lifecycle gates; don't inherit another platform's evidence |
| Comparison/profile | [Benchmark validity](benchmark-harness.md) passes separately for each variant/run |

A placeholder/empty screen does not pass the populated-item gate. Verify loaded
content and native frames/visible keys, not only a synchronous offset setter or
logical item count. Preserve source/measurement work; do not rerender every item
simply to force a screenshot.

## Local discovery and provenance

If an explicitly supplied local copy is absent from the host registry, inspect
its SKILL.md frontmatter and relative references first. Where the host permits,
read it as source guidance without claiming successful registry activation;
otherwise report the registration blocker. Reload/re-register through the host's
supported workflow. Never bypass content exclusion or other host restrictions.

Do not search unrelated workspaces or install companions merely because a loader
missed a local copy. Distribution source here is `dotnet/maui-labs`,
`plugins/dotnet-maui/skills/maui-bespoke-collections`; read the plugin manifest and
installed provenance/content hash for the actual version/revision. A copied file
without metadata has unknown provenance, not a guessed upstream version.

The catalog must contain the skill in the selected revision before CLI installation
can discover it; a feature-branch source file is not proof it is on the default
catalog. Use existing marketplace/install guidance rather than fabricating commands.
