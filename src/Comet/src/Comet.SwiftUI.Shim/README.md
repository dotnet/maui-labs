# CometSwiftUIShim

A small Swift package that exposes **SwiftUI** behind an `@objc`-representable surface so
.NET for iOS can drive it **without Swift-ABI interop** — everything crosses the boundary
as Objective-C-compatible types, bound with a standard `.NET`-for-iOS binding
(`../Comet.SwiftUI.Binding`).

This is the iOS counterpart to the vendored Jetpack Compose facade on Android: the
platform-specific UI-kit bridge that Comet-Next's `ICometBackendNode` SwiftUI backend
renders through.

## Build

```bash
./build-xcframework.sh   # produces CometSwiftUIShim.xcframework (device + simulator)
```

`CometSwiftUIShim.xcframework` is a first-party generated package input. It is part of
the Comet solution and is tracked with the Swift source.

Whenever `Sources/CometSwiftUIShim/` or the Objective-C binding contract changes:

1. Run `./build-xcframework.sh`.
2. Include the regenerated `CometSwiftUIShim.xcframework` in the same change.
3. Clean the `CometSwiftUIProbe` outputs so the native framework is relinked.
4. Build and run the iOS probe before reporting the change as complete.

The XCFramework contains device and simulator slices. Do not update only one slice, and
do not treat a locally generated framework as an untracked prerequisite.

## Why @objc instead of Swift bindings

Stock .NET for iOS can't call Swift directly, but it has always bound Objective-C. A Swift
`@objc` class (NSObject subclass, explicit `@objc(Name)` selectors) is ObjC-callable, so
the binding is a plain `ApiDefinition` + `NativeReference` to the framework. iOS ships the
Swift runtime, so the dynamic framework resolves it via `@rpath`. This is the plan's
"fallback" path — in practice the lower-risk primary path (no `swift-dotnet-bindings`
dependency, no `CallConvSwift`).

Verified on the iOS 18 / iPhone 16 simulator: a SwiftUI view hosted via
`UIHostingController`, with its text supplied from C#, renders correctly.
