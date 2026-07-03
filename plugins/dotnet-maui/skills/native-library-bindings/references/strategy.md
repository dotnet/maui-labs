# Binding Strategy

Use this decision matrix before creating projects or downloading binaries.

## Ask before choosing when ambiguous

Two questions drive the slim-vs-full decision. If either is unclear from the
request, stop and ask rather than defaulting to a full binding:

1. **App-local or redistributable?** Is this binding only ever consumed inside
   one app, or will it be published/shared as a NuGet package for other
   projects/teams to consume?
2. **Narrow or broad API surface?** Does the app call a small, fixed set of
   native members, or do consumers need broad/general access to the native
   SDK's public API?

| App-local vs redistributable | Narrow vs broad surface | Recommended strategy |
|---|---|---|
| App-local | Narrow | Slim binding (Native Library Interop) |
| App-local | Broad | Full binding, scoped to only the members actually used, still app-local |
| Redistributable | Narrow | Slim wrapper packaged as a small NuGet, if consumers only need that surface |
| Redistributable | Broad | Full/traditional binding project, packaged for redistribution |

## Strategy matrix

| Need | Prefer | Why |
|------|--------|-----|
| App needs a small subset of an SDK | Native Library Interop / slim wrapper | Smaller API, fewer generator issues, easier updates. |
| Public NuGet exposes a broad SDK | Traditional binding project | Consumers expect full API surface and packageable assets. |
| Native SDK has stable C ABI | P/Invoke | Avoid ObjC/Java binding generators when the native ABI is simple. |
| Swift-only xcframework with no ObjC surface | Swift direct binding generator or Swift wrapper | Objective Sharpie cannot see pure Swift ABI surface. |
| Internal app-only integration | App-local binding project | Avoid package complexity until redistribution is needed. |
| Reusable company/community package | NuGet-ready binding project | Requires asset, dependency, and license discipline. |

## Native Library Interop (slim bindings)

Use slim bindings when:

- The app only needs a small, well-defined subset of the native SDK.
- Native APIs use Swift/Kotlin features that are awkward to bind directly.
- You can write a wrapper in Swift/ObjC or Java/Kotlin.
- The wrapper can expose simple marshallable types and callbacks.

Avoid slim bindings when:

- Consumers need the full native SDK surface.
- The native SDK already exposes a clean ObjC/Java API and full binding is manageable.
- You need to preserve native types and inheritance across most of the SDK.

## Traditional bindings

Use traditional bindings when:

- The package itself is the product.
- The native API is broad and users need to call many APIs directly.
- You can maintain generated metadata/API definitions over time.
- Native dependencies can be represented as NuGet/project/native references.

Traditional bindings are not "set and forget." Expect generated output cleanup, dependency verification, package validation, and sample-app testing.

## P/Invoke and C ABI

Use P/Invoke when:

- The library exposes C functions or a stable C ABI.
- The desired surface is procedural or data-structure based.
- You do not need ObjC runtime or JVM type projection.

For Apple static libraries, calls often use `DllImport("__Internal")` after the native library is linked into the app. For dynamic libraries, verify platform rules and loader behavior before choosing this route.

## Swift direct binding generators

Consider a Swift direct binding generator, such as `swift-dotnet-bindings`, when:

- The input is a compiled `.xcframework`.
- The library is Swift-only or has important Swift-only APIs.
- ObjC-compatible wrapper APIs would lose too much value.
- The environment satisfies the generator's prerequisites.

Treat this as an advanced path. Validate generated packages with a sample app, inspect native asset packaging, and keep a fallback slim-wrapper plan for unsupported language features.

## Acquisition decision

| Source | Apple handling | Android handling |
|--------|----------------|------------------|
| Package manager | SPM-to-xcframework or CocoaPods workspace | Maven/Gradle coordinates |
| Direct release asset | Verify `.xcframework` slices and checksums | Verify AAR/JAR/POM and checksums |
| Source repo | Build Xcode project/workspace | Build Gradle module/AAR |
| Existing local binary | Inspect slices, headers, modules | Inspect AAR/JAR/classes/POM/native ABIs |

## Redistribution decision

Before packaging native binaries into NuGet:

1. Confirm the license permits redistribution.
2. Confirm whether notice files must be included.
3. Confirm whether transitive native dependencies must be redistributed separately.
4. Confirm whether the native SDK vendor forbids repackaging.
5. Confirm package layout with a consumer sample app.

Stop packaging if redistribution terms are unclear.

## Updating an existing binding

The strategy used to create a binding also determines how you update it:

- **Slim (Apple or Android)**: re-acquire the vendor SDK, rebuild the thin
  wrapper against the new version, and diff only the small wrapper surface
  against the vendor changelog.
- **Full/traditional (Apple or Android)**: re-acquire the vendor SDK, redo the
  dependency graph (xcframework slices or Gradle dependency tree), and
  regenerate/diff the full API metadata.
- **Packaged/redistributable**: do the above, then also bump package version,
  refresh license/notice files, and validate from a clean consumer app.

See the `Updating an Existing Binding` workflow in `SKILL.md`, plus the update
guidance in `references/apple-acquisition.md` and `references/android-bindings.md`.
