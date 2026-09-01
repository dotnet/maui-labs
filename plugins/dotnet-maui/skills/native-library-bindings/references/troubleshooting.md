# Native Binding Troubleshooting

Start with the platform and phase: acquisition, binding generation, build, package, or runtime.

## Apple build errors

| Error | Likely cause | Fix |
|-------|--------------|-----|
| `Framework not found` | Bad `NativeReference` path or package target path | Inspect project/package paths and generated MSBuild items. |
| `Undefined symbols for architecture` | Missing native dependency, wrong slice, or system framework | Inspect slices and `otool -L`; add dependency/framework. |
| `building for iOS Simulator, but linking in object file built for iOS` | Device binary used for simulator | Use `.xcframework` with simulator slice. |
| `No such module` in Swift | Missing Swift module or wrong search path | Verify `.swiftmodule` files and Xcode build settings. |
| Sharpie parse errors | Missing headers, wrong SDK, missing compiler flags | Point Sharpie at built headers and pass required include paths/scope. |

Verify which architecture slices a framework binary actually contains before
assuming a missing-symbol or wrong-slice error is a linking configuration
problem:

```bash
lipo -info path/to/Framework.framework/Framework
# Example output: Architectures in the fat file: Framework are: armv7 arm64
```

If Sharpie cannot find an SDK, or fails with "Unable to find SDK", confirm the
active Xcode toolchain and reinstall command line tools if needed:

```bash
sharpie xcode -sdks
xcode-select --install
sudo xcode-select -s /Applications/Xcode.app
```

Fine-tune force-loading and linker behavior directly on `NativeReference` when
symbols are missing at link time or duplicated at runtime:

```xml
<NativeReference Include="Library.xcframework">
  <Kind>Framework</Kind>
  <Frameworks>Foundation UIKit</Frameworks>
  <LinkerFlags>-lsqlite3</LinkerFlags>
  <SmartLink>true</SmartLink>
  <ForceLoad>false</ForceLoad>
</NativeReference>
```

- `ForceLoad=true` — forces the linker to include every object file from a
  static library, needed when the missing symbols are from ObjC
  categories/static initializers that the linker would otherwise dead-strip.
- `SmartLink=true` — lets the linker drop unused symbols; usually safe to leave
  on, but if a runtime `unrecognized selector`/missing-class error appears only
  in Release builds, try `SmartLink=false` to rule out over-aggressive
  stripping before assuming it is a selector mismatch.

## Apple runtime errors

| Error | Likely cause | Fix |
|-------|--------------|-----|
| `unrecognized selector sent to instance` | `[Export]` selector mismatch | Match Swift `@objc(selector:)`, generated header, and C# export. |
| `Native class hasn't been loaded` | Framework not linked/loaded or class not ObjC-visible | Verify `NativeReference`, `ForceLoad`, `@objc`, and public visibility. |
| `Library not loaded: @rpath` | Dynamic framework not embedded or rpath wrong | Verify package/app embed/sign behavior. |
| Callback never fires | Wrapper loses callback reference or wrong thread | Hold references and marshal to main thread as needed. |

## Android build errors

| Error | Likely cause | Fix |
|-------|--------------|-----|
| XA4241 | Unsatisfied Java dependency | Add `PackageReference`, `ProjectReference`, `AndroidMavenLibrary`, or `AndroidLibrary`. |
| XA4242 | Unsatisfied dependency with known NuGet suggestion | Add suggested NuGet package first. |
| Duplicate Java type | Same AAR/JAR included twice | Remove duplicate package/library or set dependency as provided by one package only. |
| C# interface implementation error | Generator inferred incompatible types | Use `Metadata.xml` `managedType`/`managedReturn` or additions. |
| Invalid C# name | Java member/package generates illegal name | Use `managedName`, `argsType`, or remove broken API. |

## Android runtime errors

| Error | Likely cause | Fix |
|-------|--------------|-----|
| `NoClassDefFoundError` | Runtime dependency ignored or missing | Do not use `AndroidIgnoredJavaDependency`; include dependency. |
| `UnsatisfiedLinkError` | Missing `.so` or wrong ABI | Verify AAR `jni` folders or add `AndroidNativeLibrary`. |
| Callback not invoked | Listener GC'd or wrong threading | Hold strong reference and marshal to UI thread as needed. |
| `ClassNotFoundException` | ProGuard/R8/resource or dependency issue | Verify packaging and dependency tree. |
| `Java.Lang.IllegalStateException: Must call initialize() first` | Wrapper/native SDK requires an explicit init call before other APIs are used | Call the wrapper's `Initialize(...)` during app/Activity startup (e.g. `MauiProgram`/`ConfigureLifecycleEvents`) before any other wrapper method. |

## Trimming and linker stripping (Release / full trimming)

Types reachable only from native code, the Android manifest, reflection, or ObjC selectors are invisible to the .NET trimmer and can be stripped in Release/`TrimMode=full` builds. These failures never appear in Debug, so test trimmed.

Android:

- Android callable wrappers (subclasses of `Service`, `BroadcastReceiver`, or `Activity` referenced only from the manifest, e.g. a `FirebaseMessagingService` subclass) can be trimmed away. Symptom: the service/receiver never fires and the type is missing from the generated manifest.
- Preserve them: keep the `[Service]`/`[BroadcastReceiver]` attributes (with `Exported=false` and the correct `[IntentFilter]`), and add `[Android.Runtime.Preserve(AllMembers = true)]` (or a linker descriptor) if members are dropped.
- Validate after a `TrimMode=full` build: confirm the managed type maps to a Java type in `obj/.../acw-map.txt`, and that it is registered as a `<service>`/`<receiver>` with the expected `android:exported` and `<intent-filter>` action in the generated `AndroidManifest.xml`.

Apple:

- Managed types/members invoked only from native (via selectors, `NSInvocation`, or KVO) can be trimmed. Symptom: `unrecognized selector` or `Native class hasn't been loaded` only in Release.
- Add `[Foundation.Preserve(AllMembers = true)]` to bound/wrapper classes the native side calls back into, or ship a linker descriptor XML in the package.

Always smoke-test a redistributable binding from a consumer app built in Release with full trimming before shipping.

## IntelliSense in binding projects (Android and Apple)

Both Android and Apple binding projects intentionally show IntelliSense errors
in the generated binding namespace/types even when the project builds and runs
successfully:

- Binding projects generate their C# API surface at build time (from
  `api.xml`/Sharpie output), not via a live source generator IntelliSense can
  follow incrementally.
- Build the binding project first, then reload/re-open the solution so the
  IDE picks up the generated assembly reference.
- Treat "red squiggles that disappear after a successful build + reload" as
  expected; do not chase them as a real compile error.

## Metadata debugging

For Android:

1. Build the binding once.
2. Inspect `obj/<Configuration>/<TFM>/api.xml`.
3. Write precise XPath transforms in `Transforms/Metadata.xml`.
4. Rebuild and inspect generated C# errors.

Do not edit `api.xml` directly.

For Apple:

1. Inspect generated ObjC headers.
2. Generate Sharpie output.
3. Diff generated definitions against existing `ApiDefinition.cs`.
4. Merge intentionally and rebuild.

## When to stop

- Stop if license terms do not permit redistribution.
- Stop if a native SDK lacks required platform slices and no source/build path exists.
- Stop after one failed missing-prerequisite install attempt; report the exact missing tool.
- Stop if dependency resolution is speculative; get Gradle/Xcode/package-manager facts first.
