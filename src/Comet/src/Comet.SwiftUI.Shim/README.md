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

The `.xcframework` is a build output (git-ignored); regenerate it before building the
binding or the `CometSwiftUIProbe` sample.

## Why @objc instead of Swift bindings

Stock .NET for iOS can't call Swift directly, but it has always bound Objective-C. A Swift
`@objc` class (NSObject subclass, explicit `@objc(Name)` selectors) is ObjC-callable, so
the binding is a plain `ApiDefinition` + `NativeReference` to the framework. iOS ships the
Swift runtime, so the dynamic framework resolves it via `@rpath`. This is the plan's
"fallback" path — in practice the lower-risk primary path (no `swift-dotnet-bindings`
dependency, no `CallConvSwift`).

Verified on the iOS 18 / iPhone 16 simulator: a SwiftUI view hosted via
`UIHostingController`, with its text supplied from C#, renders correctly.
