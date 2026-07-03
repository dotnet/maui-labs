---
name: native-library-bindings
description: >-
  Create, troubleshoot, package, and update native library bindings for .NET
  MAUI on Apple and Android. USE FOR: choosing between slim (Native Library
  Interop) and traditional/full bindings, app-local vs redistributable and
  narrow vs broad API-surface choices, multi-platform strategy, cross-ecosystem
  artifact acquisition (Maven, SPM, CocoaPods, xcframeworks, GitHub releases),
  xcframework slice verification, Swift-only binding options, Objective Sharpie
  cleanup, Android Gradle/Maven and Java dependency-verification resolution,
  redistributable NuGet packaging with buildTransitive props/targets, and
  updating a binding after an upstream SDK update. Owns the slim-vs-full
  decision for Apple and Android and can run slim workflows directly.
  DO NOT USE FOR: general MAUI app development, platform handlers, workload
  installation, or non-native .NET wrappers; do not hand slim work to narrower
  skills unless explicitly named. INVOKES: inspection scripts,
  Gradle/Xcode/Sharpie tooling, NuGet and package-manager commands.
---

# Native Library Bindings for .NET MAUI

Use this skill to bind native Apple and Android libraries for .NET MAUI or
platform-specific .NET apps, including creating reusable NuGet packages and
updating existing bindings when the upstream native SDK changes.

This skill covers both **Native Library Interop (slim bindings)** and
**traditional binding projects**, for Apple and Android alike. Slim bindings
are a first-class strategy in this skill, not a separate route: use the
strategy check below to decide, then follow the Apple or Android workflow
sections for the implementation details of whichever strategy is chosen.

## When to Use This Skill

Use this skill when the user asks to:

- Bind an iOS, Mac Catalyst, macOS, tvOS, or Android native SDK for .NET MAUI.
- Create or fix `ApiDefinition.cs`, `StructsAndEnums.cs`, or Objective Sharpie output.
- Integrate an `.xcframework`, `.framework`, `.a`, AAR, JAR, or Android `.so` file.
- Acquire native dependencies from Swift Package Manager, CocoaPods, Maven, Gradle, or GitHub releases.
- Resolve Android binding errors such as XA4241, XA4242, duplicate Java types, missing `.so` ABIs, or `NoClassDefFoundError`.
- Package a binding as a NuGet with native assets, transitive dependencies, `.props`, or `.targets`.
- Decide between slim bindings, full bindings, P/Invoke/C ABI, or Swift direct binding generation.
- Update an existing binding after the vendor ships a new native SDK version.

This skill is the single, standalone place to make the strategy decision
(slim vs full, app-local vs redistributable) and to own slim implementation
workflows, multi-platform strategy, traditional/full bindings,
cross-ecosystem acquisition decisions, dependency resolution across
ecosystems, redistributable NuGet packaging, and binding update workflows for
both Android and Apple platforms.

## First Decision: Binding Strategy

Before writing files, identify:

**First, check whether a maintained binding NuGet already exists** for this native SDK (for example `AdamE.Firebase.iOS.*` for the Firebase iOS SDK, or `Xamarin.Firebase.*`/`Xamarin.AndroidX.*`/`Xamarin.GooglePlayServices.*` for Android). If one exists, consume it — optionally behind a thin cross-platform C# abstraction — instead of building a new binding. Only build your own when none exists, it is stale/unmaintained, or it lacks APIs you need.

Then identify:

| Question | Why it matters |
|----------|----------------|
| Which platforms are required? | Apple slices and Android ABIs determine artifacts and TFMs. |
| How is the native SDK distributed? | Maven/SPM/CocoaPods/direct downloads need different acquisition and verification. |
| How much API surface is needed? | Small surface favors slim wrappers; broad public API favors full binding. |
| Is the output app-local or a redistributable package? | App-local + small surface favors slim; redistributable/broad favors a full binding project. |
| Does the native API expose ObjC-compatible headers? | Objective Sharpie only sees ObjC-visible APIs. |

**Stop and ask** when the app-local-vs-redistributable question or the
small-vs-broad-surface question is ambiguous from the request. Do not default
to a full binding just because the user said "bind this SDK" — ask (or infer
from concrete evidence such as "we're publishing a NuGet" or "we only call
three methods") before committing to a strategy. As a default heuristic when
evidence is available:

- App-local consumption + a small, well-defined API surface → **slim binding**
  (Native Library Interop) for that platform.
- Redistributable NuGet package, or a broad/public API surface consumers will
  call directly → **traditional/full binding project**.

Use the detailed strategy matrix in `references/strategy.md`.

## Useful Commands

Use these as starting points, not as a rigid sequence:

```bash
dotnet new android-bindinglib -n MySdk.Android.Binding
sharpie xcode -sdks
sharpie bind --output=sharpie-out --namespace=MySdk --sdk=iphoneos18.0 --scope=Headers Headers/MySdk-Swift.h
./gradlew :app:dependencies --configuration releaseRuntimeClasspath
dotnet pack -c Release
```

## Workflow

### 1. Inspect inputs and rights

- Confirm target TFMs, native SDK version, acquisition source, and whether the output is app-local or a NuGet package.
- Check native SDK license and redistribution terms before embedding binaries.
- For direct downloads, verify checksums or release provenance when available.

### 2. Acquire and verify native artifacts

- **Apple**: prefer direct `.xcframework` or SPM-to-xcframework when available; use CocoaPods as a fallback when it is the only maintained distribution path.
- **Android**: prefer Maven/Gradle coordinates; use direct AAR/JAR downloads only when Maven is not available.
- **Credentialed sources**: some SDKs (e.g. Mapbox) need a download token. Inject it via env / `~/.gradle/gradle.properties` / a git-ignored props file copied from a committed template; never commit it or ship it in the package, and keep the build-time download token separate from any runtime API key.
- Verify Apple slices/platforms before authoring bindings. Use `scripts/Test-AppleXCFramework.ps1` when an `.xcframework` exists.
- Resolve Android runtime dependencies before deciding `.csproj` items. Use the native project's Gradle wrapper only when the project is trusted; `scripts/Get-AndroidDependencyReport.ps1` can create a best-effort report from explicit Maven coordinates when JDK, Android SDK/`ANDROID_HOME`, network access, and Gradle are available.

See:

- `references/apple-acquisition.md`
- `references/android-acquisition.md`

### 3. Design the native interop surface

For slim bindings, create a native wrapper that exposes only simple, stable,
marshallable APIs.

**Apple wrapper rules:**

- Classes exposed to C# should inherit from `NSObject`.
- Use explicit `@objc(TypeName)` and `@objc(selector:)`.
- Keep methods `public`.
- Convert Swift async/await to completion handlers.
- Convert errors to `NSError`.
- Avoid Swift-only surface unless using an explicit Swift direct binding route.
- If the SDK is fully Swift with no ObjC surface (e.g. Mapbox iOS), build/maintain a separate `@objc` wrapper framework and bind that, not the SDK (see `references/apple-bindings.md`).
- For large/nested native object graphs, consider the JSON-payload complex-data
  strategy instead of modeling every nested type across the binding (see
  `references/apple-bindings.md`).

**Android wrapper rules:**

- Prefer Java-style static methods or simple Kotlin `object` wrappers.
- Add `@JvmStatic` for Kotlin static access.
- Use simple types: `String`, primitives, arrays, `Context`, `Activity`, `View`, or explicit callback interfaces.
- Convert coroutines, `Flow`, lambdas, and generic-heavy APIs to listener/callback patterns.
- For large/nested native object graphs, consider returning JSON strings from
  the wrapper and deserializing into shared C# models, mirroring the Apple
  JSON-payload strategy where cross-platform parity matters (see
  `references/android-bindings.md` and `references/apple-bindings.md`).

**Android Native Library Interop (NLI) workflow:**

Use this sequence for an app-local Android slim binding:

1. Analyze the vendor's Gradle dependency tree to learn resolved versions and
   transitive runtime dependencies (`references/android-acquisition.md`).
2. Create a small Android library wrapper project (Gradle module) that depends
   on the vendor SDK.
3. Expose a Java or Kotlin `DotnetMySdk`-style wrapper with only the simple,
   marshallable surface the app needs (see wrapper rules above and the
   patterns in `references/android-bindings.md`).
4. Build the wrapper into an AAR — prefer `AndroidGradleProject` in the binding
   `.csproj` to compile the Gradle module during the .NET build instead of
   hand-building or committing a prebuilt AAR.
5. Bind the wrapper AAR (not the vendor SDK directly) from a .NET Android
   binding project.
6. Resolve `XA4241`/`XA4242` Java dependency verification errors using the
   decision order in `references/android-bindings.md`.
7. Customize `Transforms/Metadata.xml` only for the thin wrapper surface, not
   the full vendor API.
8. Validate the wrapper end-to-end from a MAUI sample app on device/emulator.

### 4. Create the binding project

**Apple traditional/NLI binding projects** use SDK-style projects with:

- `IsBindingProject`
- `ObjcBindingApiDefinition`
- `ObjcBindingCoreSource`
- `NativeReference` for prebuilt native libraries
- `XcodeProject` when the native wrapper is built from an Xcode project/workspace

**Android binding projects** use:

- `AndroidLibrary` for local AAR/JAR files
- `AndroidMavenLibrary` for Maven artifacts on .NET 9+
- `AndroidGradleProject` to build a native Gradle wrapper module into an AAR during the .NET build (the Android analog of `XcodeProject`)
- `AndroidIgnoredJavaDependency` only for compile-time-only dependencies
- `TransformFile` / `Transforms/Metadata.xml` for binding cleanup

Use local assets as starting snippets, then adapt to the user's native SDK:

- `assets/apple-binding.csproj.xml`
- `assets/android-binding.csproj.xml`
- `assets/multi-platform-package.targets.xml`
- `assets/xcodegen-project.yml`
- `assets/xcodegen-info.plist`
- `assets/gradle-wrapper-build.gradle.kts`

### 5. Generate and clean binding definitions

For Apple ObjC-visible APIs:

- Run Objective Sharpie only after the native framework/header exists.
- Treat generated code as a draft.
- Review every `[Verify]` before removing it.
- Add `[NullAllowed]`, `[Async]`, `[Static]`, `[Internal]`, `[Protocol]`, `[Model]`, event/delegate attributes, and enum/struct corrections intentionally.
- Ensure each `[Export("selector:")]` exactly matches the native `@objc(selector:)` or ObjC header.

Use `scripts/Convert-SharpieOutputReport.ps1` to produce cleanup findings, not to blindly certify correctness.

For Android:

- Build once and inspect `obj/<Configuration>/<TFM>/api.xml`.
- Use XPath metadata transforms instead of editing generated `api.xml`.
- Prefer `managedName`, `managedType`, `managedReturn`, `argsType`, `eventName`, `remove-node`, and `add-node` patterns documented in `references/android-bindings.md`.

### 6. Resolve dependencies

**Android decision order:**

1. If XA4242 suggests a NuGet package, use it first.
2. If a NuGet package already provides the Java artifact, use `PackageReference`.
3. If another local binding project provides it, use `ProjectReference` with `JavaArtifact`.
4. If it is needed at runtime but not from C#, use `AndroidMavenLibrary` or `AndroidLibrary` with `Bind="false"`.
5. Use `AndroidIgnoredJavaDependency` only for compile-time-only artifacts such as annotation packages.

**Apple decision order:**

1. Verify the `.xcframework` contains every target platform/simulator/device slice required.
2. Add required Apple system frameworks and native linker settings.
3. Avoid duplicate static frameworks across package and app references.
4. For dynamic frameworks, verify embed/sign/rpath behavior in the consuming app.

### 7. Validate with a sample app

- Reference the binding project from a minimal MAUI app using platform-conditional `ProjectReference`.
- Exercise at least one native call per target platform.
- For Android, verify runtime dependencies on device/emulator, not only compile-time build.
- For Apple, verify both simulator and device/catalyst/macOS/tvOS slices as applicable.
- For a redistributable binding, also build the consumer app in Release with full trimming (`TrimMode=full`) and confirm manifest-only/native-callback types survive (see the trimming section in `references/troubleshooting.md`).

### 8. Package only after validation

Use `references/nuget-packaging.md` before creating packages. A redistributable binding package should include:

- Managed binding assemblies under the correct TFM.
- Native assets wired for consumers.
- Required `build` or `buildTransitive` `.props`/`.targets`.
- Accurate package dependencies for transitive bindings.
- Native license/notice files when required.
- A sample consumer project or validation steps.

For large modular SDKs (Stripe, AndroidX, Compose), prefer one binding package per native module with `ProjectReference`/`PackageReference`s that mirror the native module graph, rather than one monolithic binding. For Android you can either embed the AAR or ship a `.targets` that resolves it from Maven via Gradle at consumer build; ship `ProguardConfiguration` keep rules when the native code needs them. For a set of tightly coupled, statically linked xcframeworks (e.g. the Facebook SDK), add one `NativeReference` per framework in the same package and set `Linkage`/`ForceLoad` appropriately. See `references/nuget-packaging.md` and `references/apple-bindings.md`.

## Updating an Existing Binding

Use this workflow when the vendor ships a new native SDK version, for slim or
full bindings, on Apple, Android, or both:

1. **Re-acquire the native artifact.** Find the latest vendor release/version.
   Pin one version property per platform binding (MSBuild property, Gradle
   version catalog entry, or similar) as the source of truth. Do not assume a
   single global version across platforms — vendor version schemes rarely align
   (for example a vendor may ship Apple 5.72.0 and Android 10.5.0 for the "same"
   release), so each platform binding tracks its own version. Re-run the same
   acquisition steps as initial acquisition
   (`references/apple-acquisition.md`, `references/android-acquisition.md`).
2. **Redo the dependency graph basics.** Re-verify `.xcframework` slices for
   Apple, or re-run the Gradle dependency tree for Android
   (`./gradlew :app:dependencies ...`). Do not assume the previous slice list
   or resolved dependency graph still applies; vendors add/drop platforms and
   transitive dependencies between releases.
3. **Regenerate and diff API/metadata.** Re-run Objective Sharpie or rebuild
   the Android binding to regenerate `api.xml`, then diff the new output
   against the existing `ApiDefinition.cs`/`Transforms/Metadata.xml`. For a
   slim wrapper, diff the vendor's changelog/API against the thin wrapper
   surface instead of the whole SDK.
4. **Summarize C# API breaking changes.** Before changing wrapper or
   `ApiDefinition.cs` code, produce a short summary of what changed in the
   generated/exposed C# surface (renamed/removed members, changed
   signatures, new required parameters, changed nullability) so consumers can
   assess impact.
5. **Update package/license metadata.** Bump the binding package version,
   update any bundled license/notice files, and confirm redistribution terms
   still apply to the new vendor version.
6. **Validate from a consumer sample.** Rebuild the sample/consumer app against
   the updated binding and exercise the previously-working calls before
   shipping the update.

This applies uniformly to Apple full bindings, Apple slim wrappers (including
the JSON-payload pattern), Android full bindings, the Android NLI/slim
workflow, and packaged redistributable bindings — only the acquisition and
dependency-graph mechanics differ per platform/strategy.

## Critical Anti-Patterns

1. **Do not bind the whole SDK by default.** If the app needs three methods, build a small wrapper.
2. **Do not treat Objective Sharpie output as final.** It is a first-pass parser output that needs API review.
3. **Do not expose Swift async/await, Swift-only generics, Kotlin coroutines, or Kotlin Flow directly through wrapper APIs.** Convert to callbacks/completion handlers.
4. **Do not use `AndroidIgnoredJavaDependency` for runtime dependencies.** That hides build errors and often becomes `NoClassDefFoundError`.
5. **Do not assume `AndroidMavenLibrary` bundles transitive runtime dependencies.** It downloads the requested artifact and verifies dependencies; dependencies still need to be satisfied.
6. **Do not package third-party binaries before checking redistribution rights.**
7. **Do not duplicate native libraries across packages and app projects.** Duplicate static libraries can cause duplicate symbols; duplicate Java/Kotlin artifacts can cause type conflicts.
8. **Do not create a custom MCP server or broad scaffolding script before scripts/evals prove a repeatable gap.**
9. **Do not delete platform slices from an `.xcframework` without also removing the matching `AvailableLibraries` entries in `Info.plist`.** A mismatched `Info.plist` causes load failures even when the trimmed binary itself is fine.
10. **Do not collapse a modular native SDK into one giant binding to save package count.** Mirror the native module graph with per-module packages; collapsing reintroduces version-conflict and duplicate-type problems.

## Stop Signals

- Stop strategy research once you know the required platforms, acquisition source, redistribution requirement, and API-surface size.
- Stop and ask the user directly when app-local-vs-redistributable or narrow-vs-broad API surface cannot be determined from the request; do not guess and default to a full binding.
- Stop Sharpie automation at a cleanup report or candidate patch; final API shape requires review.
- Stop Android dependency guessing once Gradle and Java dependency verification disagree; inspect the resolved graph and generated errors before editing again.
- Stop environment troubleshooting after one missing-tool install attempt. Report the missing prerequisite and exact command needed.
- Stop packaging work if redistribution rights are unclear.
- When updating a binding, stop and summarize C# breaking changes before editing wrapper/`ApiDefinition.cs` code, so the user can confirm the impact is acceptable.

## References

- `references/strategy.md` — strategy decision matrix.
- `references/apple-bindings.md` — Apple binding project and API definition guidance.
- `references/apple-acquisition.md` — xcframework, SPM, CocoaPods, and direct download guidance.
- `references/android-bindings.md` — Android binding project, Maven, dependency, and metadata guidance.
- `references/android-acquisition.md` — Android artifact acquisition and Gradle resolution guidance.
- `references/nuget-packaging.md` — NuGet packaging for reusable binding libraries.
- `references/troubleshooting.md` — common build/runtime failures and fixes.
- `references/source-map.md` — primary sources backing the skill.
