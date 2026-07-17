# Apple Binding Projects

This reference covers iOS, Mac Catalyst, macOS, and tvOS native bindings for .NET MAUI and platform-specific .NET apps.

## Project anatomy

Modern Apple binding projects are SDK-style projects. A minimal traditional binding project uses:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-ios</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>true</ImplicitUsings>
    <IsBindingProject>true</IsBindingProject>
  </PropertyGroup>

  <ItemGroup>
    <ObjcBindingApiDefinition Include="ApiDefinition.cs" />
    <ObjcBindingCoreSource Include="StructsAndEnums.cs" />
  </ItemGroup>
</Project>
```

Use multi-targeting for shared Apple binding packages when the native artifact supports every target:

```xml
<TargetFrameworks>net10.0-ios;net10.0-maccatalyst;net10.0-macos;net10.0-tvos</TargetFrameworks>
```

Do not multi-target a platform unless the native binary actually contains that platform's slice.

### Useful binding project properties

| Property | Purpose |
|----------|---------|
| `EnableVersioning` | Enables .NET-style member/API versioning attributes (`[Introduced]`, `[Deprecated]`, `[Obsoleted]`) for the bound API surface; useful when tracking platform availability across SDK updates. |
| `CompressBindingResourcePackage` | Compresses bundled binding resources (for example bundled resource packages referenced via `BindingResourcePackage`) to reduce package size. |

Set these deliberately rather than by default; verify they behave as expected for the specific SDK before relying on them in a shipped package.

## NativeReference

Use `NativeReference` for prebuilt `.xcframework`, `.framework`, `.a`, or native libraries:

```xml
<NativeReference Include="native/MySdk.xcframework">
  <Kind>Framework</Kind>
  <ForceLoad>true</ForceLoad>
  <SmartLink>true</SmartLink>
  <Frameworks>Foundation UIKit</Frameworks>
</NativeReference>
```

Common metadata:

| Metadata | Purpose |
|----------|---------|
| `Kind` | Native artifact kind, such as `Framework` or `Static`. |
| `Linkage` | Declares `Static` or `Dynamic` linkage of the referenced framework. Set `Static` for statically linked `.xcframework`s so the toolchain links them in rather than expecting an embedded dynamic framework. |
| `IsCxx` | Set `true` when the native library exposes C++ symbols so the C++ runtime is linked. |
| `ForceLoad` | Force-load symbols, often needed for ObjC categories/static initializers. |
| `SmartLink` | Let the native linker remove unused symbols when safe. |
| `Frameworks` | Apple system frameworks required by the native library. |
| `WeakFrameworks` | Optional system frameworks. |
| `LinkerFlags` | Additional native linker flags when necessary. |
| `Pack` | Include native item in a generated NuGet package when packaging. |

Use `ForceLoad` carefully. It can fix missing ObjC category/static initializer issues, but can also increase size or expose duplicate-symbol problems.

### Multiple interdependent frameworks in one binding

A single SDK is often shipped as several xcframeworks that depend on each other (for example the Facebook iOS SDK ships `FBSDKCoreKit`, `FBSDKCoreKit_Basics`, `FBAEMKit`, and `FBSDKLoginKit`). Add one `NativeReference` per xcframework in the same binding project and keep the whole set in one NuGet, since they must version together. Reserve the "one native library per package" rule for libraries that version and ship independently. When these are statically linked, watch for duplicate-symbol errors and prefer enabling `ForceLoad` only on the frameworks that actually need it.

If you set `<IsTrimmable>true</IsTrimmable>` on the binding project you are asserting the binding is trim-safe. Back that claim with `[Preserve]` on types the native side calls into (see the trimming section in `troubleshooting.md`); otherwise consumers get Release-only failures.

## XcodeProject

Use `XcodeProject` when the binding project should build a native wrapper project:

```xml
<XcodeProject Include="../native/MyBinding/MyBinding.xcodeproj">
  <SchemeName>MyBinding</SchemeName>
  <Configuration>Release</Configuration>
</XcodeProject>
```

For CocoaPods, `pod install` produces an `.xcworkspace`; reference the workspace if the build depends on Pods.

## ApiDefinition.cs

`ApiDefinition.cs` declares interfaces with binding attributes. It should contain binding contracts, not implementation classes.

```csharp
using Foundation;

namespace MySdk;

[BaseType(typeof(NSObject))]
interface DotnetMySdk
{
    [Static]
    [Export("initializeWithApiKey:completion:")]
    [Async]
    void Initialize(string apiKey, Action<NSString?, NSError?> completion);
}
```

Key attributes:

| Attribute | Use |
|-----------|-----|
| `[BaseType]` | Generated bound type's native base class. |
| `[Export]` | ObjC selector. Must match headers or Swift `@objc(selector:)`. |
| `[Static]` | Static native member. |
| `[Async]` | Generate Task-based overload for completion handler patterns. |
| `[NullAllowed]` | Nullable ObjC reference parameter/return. |
| `[Protocol]`, `[Model]` | ObjC protocol/delegate binding patterns. |
| `[Internal]` | Hide generated member from public API. |

## StructsAndEnums.cs

Put enums, structs, delegates, and supporting types in `StructsAndEnums.cs` or other normal C# files. Do not put them inside `ApiDefinition.cs`.

Review enum backing types and flag semantics. Objective Sharpie often needs manual cleanup around `NS_ENUM`, `NS_OPTIONS`, prefix stripping, and availability.

## API definition patterns

These are the binding-definition patterns for a traditional/full ObjC binding.
They also apply when you bind a hand-written `@objc` wrapper framework (the
pure-Swift case below): the wrapper's generated `-Swift.h` is ObjC, so the same
`[BaseType]`/`[Export]` contracts apply. The quick table above is the at-a-glance
version; this section is the fuller reference. Every attribute here ships in the
.NET for iOS/macOS binding tooling and is usable from a third-party binding
project — you do not need the dotnet/macios repo to use them.

### Availability: usually skip it

A third-party binding owns its own minimum deployment target, so you generally
do **not** annotate per-member OS availability at all. Do not try to reproduce
the exhaustive `[iOS (x, y)]` / `[Mac (x, y)]` / `[NoTV]` version attributes you
see on every member in the dotnet/macios source — that exhaustiveness exists to
mirror Apple's SDK exactly and is driven by that repo's own API-diff tooling; a
binding you control does not need it.

If a specific member genuinely requires a newer OS than your project's minimum,
express it with the standard .NET attributes (not the macios-internal short
forms), on the API-definition member or on hand-written partial-class code:

```csharp
[SupportedOSPlatform ("ios17.0")]
[UnsupportedOSPlatform ("tvos")]
[Export ("newApiOnlyOniOS17")]
void NewApi ();
```

### Classes, properties, and methods

```csharp
[BaseType (typeof (NSObject))]
interface MyClass {
    // Read-write / read-only properties
    [Export ("name")]
    string Name { get; set; }

    [NullAllowed]
    [Export ("subtitle")]
    string Subtitle { get; }

    // Reference-semantics matter for delegates/blocks/closures held by the object
    [Export ("delegate", ArgumentSemantic.Weak)]
    [NullAllowed]
    NSObject WeakDelegate { get; set; }

    // Method with a nullable parameter and a nullable return
    [Export ("titleForState:")]
    [return: NullAllowed]
    string GetTitle (UIControlState state);

    // Static member
    [Static]
    [Export ("sharedInstance")]
    MyClass SharedInstance { get; }

    // Bind an init selector as a constructor
    [Export ("initWithApiKey:")]
    NativeHandle Constructor (string apiKey);
}
```

- `ArgumentSemantic` (`Weak`, `Strong`, `Copy`, `Assign`, `Retain`) mirrors the
  ObjC property attribute; get it right for delegate/block properties.
- Use `[DisableDefaultCtor]` on `[BaseType(...)]` when the native type has no
  usable parameterless initializer, so the generator does not emit one.
- Use `string`, not `NSString`, for string parameters/returns — the generator
  marshals automatically. Reserve `NSString` for dictionary keys and
  strongly-typed constants.

### Enums

```csharp
// NSInteger/NSUInteger-backed native enum
[Native]
public enum MyMode : long {
    Off = 0,
    On,
}

// Smart enum backed by NSString constants
[Native]
public enum MyReason : long {
    [Field ("MyReasonUserInitiated")]
    UserInitiated = 0,
    [Field ("MyReasonSystemInitiated")]
    SystemInitiated,
}
```

`[Native]` is required whenever the native enum is `NSInteger`/`NSUInteger`-based
(most Apple enums) so the managed size matches the native ABI.

### Notification fields

```csharp
[Notification]
[Field ("MyClassDidChangeNotification")]
NSString DidChangeNotification { get; }

// With strongly-typed event args generated from the notification's userInfo
[Notification (typeof (MyClassEventArgs))]
[Field ("MyClassDidUpdateNotification")]
NSString DidUpdateNotification { get; }
```

### Protocols, models, and the weak-delegate pattern

Bind ObjC protocols used as delegates/data sources with `[Protocol, Model]`.
Mark `@required` members `[Abstract]`; leave optional members un-attributed.

```csharp
[Protocol, Model]
[BaseType (typeof (NSObject))]
interface MyDelegate {
    [Abstract]                       // @required member
    [Export ("didFinish:")]
    void DidFinish (MyClass sender);

    [Export ("didProgress:")]        // @optional member
    void DidProgress (nfloat fraction);
}
```

Wire the delegate up with the **weak-delegate pattern** — expose the raw
`NSObject` property plus a strongly-typed `[Wrap]` accessor:

```csharp
[BaseType (typeof (NSObject))]
interface MyClass {
    [Export ("delegate", ArgumentSemantic.Weak)]
    [NullAllowed]
    NSObject WeakDelegate { get; set; }

    [Wrap ("WeakDelegate")]
    [NullAllowed]
    IMyDelegate Delegate { get; set; }
}
```

The `I`-prefix rule trips up almost everyone:

- **Definitions and conformance use the plain name.** Declare the protocol as
  `interface MyDelegate`, and add conformance as `interface MyClass : MyDelegate`.
- **Type references use the `I`-prefixed name.** When you reference the protocol
  *as a type* — a `[Wrap]` property, a parameter, a return type — use
  `IMyDelegate`. The generator produces that `I` interface for you.

### Blocks and completion handlers

Define a **named delegate type** for every block. Never bind a block as
`Action<T>`/`Func<T>` — a named delegate gives consumers real IntelliSense and
documentation, and Objective Sharpie's `Action`/`Func` output should be
converted.

```csharp
delegate void MyCompletionHandler (bool success, [NullAllowed] NSError error);

[Export ("performTaskWithCompletion:")]
void PerformTask ([NullAllowed] MyCompletionHandler completion);
```

### Async

Add `[Async]` to a method whose last parameter is a completion handler to
generate a `Task`/`Task<T>` overload. Use `ResultTypeName` when the handler
takes multiple non-error values, so the generated result type has a good name:

```csharp
delegate void FetchHandler (string value, nint count, [NullAllowed] NSError error);

[Export ("fetchWithCompletion:")]
[Async (ResultTypeName = "FetchResult")]
void Fetch (FetchHandler completion);
// generates: Task<FetchResult> FetchAsync ();
```

### Categories (ObjC extensions)

```csharp
[Category]
[BaseType (typeof (UIView))]
interface UIView_MyExtensions {
    [Export ("applyMyStyle")]
    void ApplyMyStyle ();
}
```

### Strongly-typed dictionaries

Turn an options/config `NSDictionary` into a typed surface with
`[StrongDictionary]` plus a `[Field]`-backed keys interface:

```csharp
[StrongDictionary ("MyOptionsKeys")]
interface MyOptions {
    string Name { get; set; }
    bool EnableFeature { get; set; }
}

[Static]
interface MyOptionsKeys {
    [Field ("MyNameKey")]
    NSString NameKey { get; }

    [Field ("MyEnableFeatureKey")]
    NSString EnableFeatureKey { get; }
}
```

### Type conversions with `[BindAs]`

`[BindAs]` marshals an ObjC type to a friendlier managed type (e.g. an
`NSValue`-wrapped `CGRect`, or an `NSString[]` to a smart-enum array):

```csharp
[BindAs (typeof (CGRect))]
[Export ("bounds")]
NSValue Bounds { get; set; }

[return: BindAs (typeof (MyMode []))]
[Export ("supportedModes")]
NSString [] GetSupportedModes ();
```

### Memory-management attributes

```csharp
// Native returns a +1 retained object (its name starts with create/copy)
[Export ("createObject")]
[return: Release]
NSObject CreateObject ();

// Cast the return to a type the header under-declares
[Export ("downloadTask")]
[return: ForcedType]
NSUrlSessionDownloadTask CreateDownloadTask ();

// Parameter only needs to live for the duration of the call
[Export ("processObject:")]
void ProcessObject ([Transient] NSObject obj);
```

### Error handling

Every method that takes `NSError**` (bound as `out NSError`) must mark it
`[NullAllowed]` — the error is null on success:

```csharp
[Export ("loadAndReturnError:")]
bool Load ([NullAllowed] out NSError error);
```

### Struct-array parameters (advanced)

When a selector takes a C struct pointer plus a count (`MyPoint *` + `count`, a
common MapKit/ARKit shape), the generator surfaces it as `IntPtr`. Bind the raw
selector `[Internal]` and expose a hand-written factory that pins a managed
array with `fixed`:

```csharp
// ApiDefinition.cs
[BaseType (typeof (NSObject))]
interface MyShape {
    [Static]
    [Internal]
    [Export ("shapeWithPoints:count:")]
    MyShape _Create (IntPtr points, nint count);
}
```

```csharp
// MyShape.cs (normal partial-class file, not ApiDefinition.cs)
public partial class MyShape {
    public static unsafe MyShape Create (MyPoint [] points) {
        ArgumentNullException.ThrowIfNull (points);
        fixed (MyPoint* first = points)
            return _Create ((IntPtr) first, points.Length);
    }
}
```

Prefer a static factory over a public constructor here — `fixed` inside a
constructor chain is awkward.

### Resolving `[Verify]`

Objective Sharpie emits `[Verify (...)]` wherever it guessed and needs you to
confirm. The generated code will not ship until each is resolved. The common
kinds:

- `[Verify (StronglyTypedNSArray)]` — replace `NSObject []` with the real element
  type (`MyItem []`).
- `[Verify (MethodToProperty)]` — a getter-like selector was bound as a method;
  turn it into a property if that reads better (`bool IsEnabled { get; }`).
- `[Verify (ConstantsInterfaceAssociation)]` / `[Verify (PlatformInvoke)]` —
  double-check the field/P-Invoke shape against the header.

Remove the attribute only after you have verified the API against the real
header — do not delete them blindly to make the build pass.

### Conventions that prevent runtime bugs

- **Selectors must match exactly.** A single character off in `[Export("...")]`
  compiles fine and crashes at runtime with `unrecognized selector` (see the
  "Selector mismatches" section below).
- **Use `nint`/`nuint`** for ObjC `NSInteger`/`NSUInteger`.
- **Name for .NET, not for ObjC.** Use verb-based method names and drop
  redundant prefixes (`- (void)menuWithContents:` → `void BuildMenu (...)`,
  `NSString name` → `string Name`).
- **In hand-written code, use `GetCheckedHandle ()`** instead of `Handle` when
  passing the native handle to a P/Invoke — it throws `ObjectDisposedException`
  instead of crashing natively on a disposed object.

## Swift wrapper rules

Objective Sharpie parses headers, not pure Swift surface. A Swift wrapper intended for binding should expose ObjC-compatible API:

```swift
import Foundation

@objc(DotnetMySdk)
public final class DotnetMySdk: NSObject {
    @objc(initializeWithApiKey:completion:)
    public static func initialize(apiKey: String, completion: @escaping (NSString?, NSError?) -> Void) {
        Task {
            do {
                let token = try await NativeSdk.initialize(apiKey: apiKey)
                completion(token as NSString, nil)
            } catch {
                completion(nil, error as NSError)
            }
        }
    }
}
```

Rules:

- Use explicit `@objc(TypeName)` and `@objc(selector:)`.
- Inherit from `NSObject` for classes exposed to C#.
- Keep methods and classes `public`.
- Use `NSString`, `NSData`, `NSArray`, `NSDictionary`, `NSNumber`, `NSError`, UIKit/AppKit types, or other ObjC-visible classes.
- Convert Swift `async throws` to completion handlers.
- Convert Swift errors to `NSError`.
- Avoid Swift-only generics, associated values, opaque result types, actors, and SwiftUI views unless using a direct Swift binding generator.

### Pure-Swift SDKs with no ObjC surface (hardest case)

Some SDKs (Mapbox's iOS SDK is the canonical example) migrated fully to Swift and expose almost nothing to Objective-C. A thin `@objc` wrapper over a few calls is not enough — you must build and maintain a **separate ObjC-compatible wrapper framework** that re-exposes the API surface you need, then bind that wrapper (not the SDK).

**First, check whether the vendor already ships an ObjC-compat module.** Some SDKs publish a companion framework specifically for ObjC interop (e.g. Datadog's `DatadogObjc`). If one exists, bind that instead of writing your own wrapper — it is far less to build and maintain. Only hand-build a wrapper when no such module is provided (Mapbox).

- The wrapper is its own Xcode/SPM/CocoaPods project that depends on the Swift SDK and exposes `@objc` `NSObject` types. Build it into an `.xcframework` (or reference its `.xcodeproj` via `XcodeProject`), then run Objective Sharpie against the wrapper's generated `-Swift.h`.
- Prefix wrapper types (Mapbox uses `TMB*`) to avoid clashing with the SDK's own symbols and to make the binding surface obvious.
- Factory/initializer patterns work best: expose `+createWith...` factory methods rather than trying to surface Swift initializers, generics, or protocols-with-associated-types.
- This wrapper is a real, versioned artifact you own: every SDK update may require regenerating/adjusting it. For large SDKs, consider code-generating the wrapper from the Swift API rather than hand-writing hundreds of shims.
- Then layer your binding (and any MAUI abstraction) on top, giving a three-layer stack: Swift SDK -> ObjC wrapper framework -> .NET binding (-> MAUI control).

## Complex data: JSON-payload slim binding strategy

For native APIs that return large or deeply nested object graphs, modeling
every nested type through the wrapper (or through a full binding) can be more
work than the app actually needs. As an alternative for slim bindings:

- Have the native wrapper serialize the native result to a JSON string
  (`String`/`NSString`) plus a separate success/error signal (for example an
  `NSError?` out-parameter or a `(json: String?, error: NSError?)` completion
  pair), instead of exposing the native object graph directly.
- On the C# side, deserialize the JSON string into shared, hand-written .NET
  model types (for example with `System.Text.Json`) that mirror only the
  fields the app actually needs.
- This trades wire-level type safety (the compiler cannot verify the native
  JSON shape) for a small, stable native surface: the wrapper's ApiDefinition
  stays a single string-returning method even as the native SDK's internal
  object graph changes.
- Because the JSON shape is a private contract between the native wrapper and
  the C# model, prefer defining the equivalent Android wrapper JSON shape
  using the same schema when the SDK exists on both platforms, for parity.

Tradeoffs and validation:

- Any schema drift between the native wrapper's JSON output and the C# model
  becomes a runtime deserialization failure, not a compile error. Add a test
  (unit or sample-app smoke test) that exercises deserialization against a
  real payload from the current native SDK version.
- When updating the binding after a vendor SDK update, explicitly re-check
  whether the native object graph's shape changed before assuming the JSON
  contract and C# model are still valid; treat this as part of the API-diff
  step in the binding update workflow.
- Do not use this pattern for small, simple return types where a normal
  bound/marshalled type is just as easy to expose — it is meant for large,
  nested, or frequently-changing object graphs, not as a default.

## Swift runtime linker flags

Swift libraries embed a dependency on the Swift runtime. If the app fails to launch
or crashes with a dynamic-linker error referencing `libswiftCore.dylib` (or similar),
add explicit search paths and rpath handling on the `NativeReference`:

```xml
<NativeReference Include="native/MySwiftSdk.xcframework">
  <Kind>Framework</Kind>
  <Frameworks>Foundation UIKit</Frameworks>
  <LinkerFlags>
    -L "/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/swift/iphoneos"
    -L "/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/swift/iphonesimulator"
    -Wl,-rpath -Wl,@executable_path/Frameworks
  </LinkerFlags>
</NativeReference>
```

Modern XCFrameworks built with `BUILD_LIBRARY_FOR_DISTRIBUTION` and Swift's stable ABI
usually resolve the runtime automatically; only add these flags if you observe a
concrete missing-symbol/missing-library linker or runtime error.

## MAUI app-local usage

**Conditional project reference (iOS + Mac Catalyst only):**

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-ios')) Or $(TargetFramework.Contains('-maccatalyst'))">
  <ProjectReference Include="..\..\macios\MySdk.MaciOS.Binding\MySdk.MaciOS.Binding.csproj" />
</ItemGroup>
```

**Initialize in `MauiProgram.cs`:**

```csharp
using Microsoft.Maui.Hosting;

#if IOS || MACCATALYST
using MySdk;
#endif

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if IOS || MACCATALYST
        DotnetMySdk.Initialize("your-api-key");
#endif

        return builder.Build();
    }
}
```

**Consuming `[Async]`-generated APIs and handling `NSErrorException`:**

```csharp
#if IOS || MACCATALYST
using MySdk;
#endif

private async void OnFetchClicked(object sender, EventArgs e)
{
#if IOS || MACCATALYST
    try
    {
        var result = await DotnetMySdk.FetchDataAsync("my query");
        await DisplayAlert("Success", result ?? "No data", "OK");
    }
    catch (NSErrorException ex)
    {
        // [Async] converts a completion-handler pattern where the NSError
        // parameter is non-null on failure into a thrown NSErrorException.
        await DisplayAlert("Error", ex.Error.LocalizedDescription, "OK");
    }
#endif
}
```

**Registering and unregistering a native callback from a page:**

```csharp
#if IOS || MACCATALYST
protected override void OnAppearing()
{
    base.OnAppearing();
    DotnetMySdk.RegisterCallback(message =>
    {
        MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Event: {message}");
    });
}

// Always unregister — native SDKs commonly hold a strong reference to the
// registered callback/delegate and will keep the page (and its native peer)
// alive if it is never released.
protected override void OnDisappearing()
{
    base.OnDisappearing();
    DotnetMySdk.UnregisterCallback();
}
#endif
```

## Objective Sharpie workflow

Objective Sharpie is a bootstrapper:

```bash
sharpie xcode -sdks
sharpie bind --output=sharpie-out --namespace=MySdk --sdk=iphoneos18.0 --scope=Headers Headers/MySdk-Swift.h
```

After generation:

1. Copy relevant `ApiDefinitions.cs` pieces into `ApiDefinition.cs`.
2. Move enums/structs into `StructsAndEnums.cs`.
3. Review and remove `[Verify]` only after resolving the question it marks.
4. Add `[NullAllowed]`, `[Async]`, `[Static]`, `[Internal]`, `[Protocol]`, `[Model]`, and event/delegate attributes intentionally.
5. Remove unwanted generated constructors such as `InitWithCoder` when they are not usable.
6. Build and fix compile errors before expanding API surface.

## Selector mismatches

Runtime `unrecognized selector sent to instance` usually means C# `[Export]` does not match the native selector.

Compare all three:

- Swift `@objc(selector:)`
- Generated Objective-C header selector
- C# `[Export("selector:")]`

If in doubt, make the Swift selector explicit and update C# to match.

## Platform notes

- **iOS**: verify device and simulator slices.
- **Mac Catalyst**: verify Catalyst slice, not just macOS or iOS simulator.
- **macOS**: AppKit-oriented APIs may need `net10.0-macos` and macOS-specific framework dependencies.
- **tvOS**: UIKit exists, but not all iOS frameworks/APIs are available.

Do not add a target framework just because the managed code compiles; native support must be present.
