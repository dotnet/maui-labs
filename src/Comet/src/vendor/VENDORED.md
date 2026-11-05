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

## ART baseline-profile build support

The profile and MSBuild targets in
`Microsoft.AndroidX.Compose/buildTransitive/` are copied from
[upstream commit 8be2f82fa16abb2bb1180b04a38e4de943b749e9](https://github.com/jonathanpeppers/Microsoft.AndroidX.Compose/tree/8be2f82fa16abb2bb1180b04a38e4de943b749e9).
This is a build-support backport, not a facade or generator replacement.
The original vendor revision above still identifies their starting point.

- `Microsoft.AndroidX.Compose.targets` SHA-256:
  `25b9d24e7cdc555e89f67b71c0e6e94f9a606f26d499afb3637f62fb76666cc3`.
- `Microsoft.AndroidX.Compose.baseline-prof.txt` SHA-256:
  `39e93121f7a1cf6613505dfad28b2c05c78bcb62a11790f9db81ea20472dbcf4`.
- The `.pro` file retains only the upstream rules for helpers present in this
  fork: `GCUserPeer` and `PointerInputEventHandlerImpl`. New upstream shared-state
  and measure-policy helpers are not part of this backport.
- The existing probe-specific NativeAOT GC peer rule remains for compatibility
  with its app profile. Correctness rules apply even with ART profiles disabled.

CometComposeProbe explicitly imports `eng/ComposeAndroid.targets` for its source
reference, including the standalone BaristaNotes variant. This is not a global
directory-target import: unrelated Android apps and Apple targets are unaffected.
The Comet package also carries a `buildTransitive/Comet.targets` entry point and
the same support files for package consumers. External source apps must import
`eng/ComposeAndroid.targets` explicitly after their project references; source
project references do not propagate NuGet build targets.

Profiles are enabled by default for Android applications built with R8.
Set `MicrosoftAndroidXComposeEnableBaselineProfile=false` to disable profiles.
Use `MicrosoftAndroidXComposeProfgenClasspath` to select `profgen-classpath.jar`
if it is not in the Android SDK's `cmdline-tools/latest/lib` directory. Missing
required tools fail the profile-enabled build. `AndroidArtProfile` items can
add app-specific rules. These library profiles are not a recording of BaristaNotes.

The build rewrites rules through R8 and validates binary profiles against the
final DEX. C# Native AOT and Java/Kotlin ART compilation are separate. Packaged
profiles do not prove that a device has applied profile-guided compilation.
