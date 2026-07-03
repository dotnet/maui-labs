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
| `ForceLoad` | Force-load symbols, often needed for ObjC categories/static initializers. |
| `SmartLink` | Let the native linker remove unused symbols when safe. |
| `Frameworks` | Apple system frameworks required by the native library. |
| `WeakFrameworks` | Optional system frameworks. |
| `LinkerFlags` | Additional native linker flags when necessary. |
| `Pack` | Include native item in a generated NuGet package when packaging. |

Use `ForceLoad` carefully. It can fix missing ObjC category/static initializer issues, but can also increase size or expose duplicate-symbol problems.

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
