---
name: ios-slim-bindings
description: >-
  Create and update slim/native platform interop bindings for iOS in .NET MAUI and .NET for iOS projects.
  Guides through creating Swift/Objective-C wrappers, configuring Xcode projects, generating C# API definitions,
  and integrating native iOS libraries using the Native Library Interop (NLI) approach.
  USE FOR: iOS bindings, xcframework integration, Swift interop, Objective Sharpie, bridging native iOS SDKs to .NET.
  DO NOT USE FOR: Android bindings (use android-slim-bindings), general MAUI app development, NuGet package issues.
---

# When to use this skill

Activate this skill when the user asks:
- How do I create iOS bindings for a native library?
- How do I wrap an iOS SDK for use in .NET MAUI?
- How do I create slim bindings for iOS?
- How do I use Native Library Interop for iOS?
- How do I bind a Swift library to .NET?
- How do I use Objective Sharpie for iOS bindings?
- How do I integrate an xcframework into .NET MAUI?
- How do I create a Swift wrapper for a native iOS library?
- How do I update iOS bindings when the native SDK changes?
- How do I fix iOS binding build errors?
- How do I expose native iOS APIs to C#?
- How do I handle CocoaPods dependencies in iOS bindings?
- How do I handle Swift Package Manager dependencies?

# Overview

This skill guides the creation of **Native Library Interop (Slim Bindings)** for iOS. This modern approach creates a thin native Swift/Objective-C wrapper exposing only the APIs you need from a native iOS library, making bindings easier to create and maintain.

## When to Use Slim Bindings vs Traditional Bindings

| Scenario | Recommended Approach |
|----------|---------------------|
| Need only a subset of library functionality | **Slim Bindings** |
| Easier maintenance when SDK updates | **Slim Bindings** |
| Prefer working in Swift/Objective-C for wrapper | **Slim Bindings** |
| Better isolation from breaking changes | **Slim Bindings** |
| Need entire library API surface | Traditional Bindings |
| Creating bindings for third-party developers | Traditional Bindings |
| Already maintaining traditional bindings | Traditional Bindings |

# Inputs

| Parameter | Required | Example | Notes |
|-----------|----------|---------|-------|
| libraryName | yes | `FirebaseMessaging`, `Lottie` | Name of the native iOS library to bind |
| bindingProjectName | yes | `MyBinding.MaciOS` | Name for the C# binding project |
| dependencySource | no | `cocoapods`, `spm`, `xcframework` | How the native library is distributed |
| targetFrameworks | no | `net10.0-ios;net10.0-maccatalyst` | Target frameworks (default: latest .NET iOS + Mac Catalyst) |
| exposedApis | no | List of specific APIs | Which native APIs to expose (helps scope the wrapper) |

# Project Structure

```
MyBinding/
 macios/
 native/   
 MyBinding/                    # Xcode project      
 MyBinding.xcodeproj/          
 project.pbxproj             
 MyBinding/          
 DotnetMyBinding.swift  # Swift wrapper implementation             
 Podfile                    # If using CocoaPods          
 Package.swift              # If using Swift Package Manager          
 MyBinding.MaciOS.Binding/   
 MyBinding.MaciOS.Binding.csproj       
 ApiDefinition.cs       
 sample/
 MauiSample/                        # Sample MAUI app   
 MauiSample.csproj       
 MainPage.xaml.cs       
 README.md
```

# Step-by-step Process

> **For detailed code examples and full implementation walkthroughs, see [references/ios-slim-bindings-implementation.md](references/ios-slim-bindings-implementation.md).**

## Step 1: Create Project Structure

Install XcodeGen (`brew install xcodegen`) and create the directory structure:

```bash
BINDING_NAME="MyBinding"
mkdir -p ${BINDING_NAME}/macios/native/${BINDING_NAME}/${BINDING_NAME}
mkdir -p ${BINDING_NAME}/macios/${BINDING_NAME}.MaciOS.Binding
```

## Step 2: Create the Xcode Project

Create a `project.yml` for XcodeGen with framework target, then run `xcodegen generate`. Key build settings:

- `BUILD_LIBRARY_FOR_DISTRIBUTION: YES`
- `MACH_O_TYPE: staticlib`
- `DEFINES_MODULE: YES`
- `SWIFT_VERSION: "5.0"`

## Step 3: Create the C# Binding Project

Create a `.csproj` with `<IsBindingProject>true</IsBindingProject>` and reference the Xcode project:

```xml
<XcodeProject Include="../native/MyBinding/MyBinding.xcodeproj">
  <SchemeName>MyBinding</SchemeName>
</XcodeProject>
```

Create an initial `ApiDefinition.cs` with `[BaseType]`, `[Static]`, and `[Export]` attributes.

## Step 4: Build and Verify

```bash
dotnet build
```

This invokes XcodeBuild, creates the xcframework, and generates the C# binding assembly.

## Step 5: Add Native Library Dependencies

Choose the appropriate method:

| Method | When to Use |
|--------|-------------|
| **CocoaPods** | Library distributed via CocoaPods. Use `use_frameworks! :linkage => :static`. After `pod install`, reference `.xcworkspace` instead of `.xcodeproj`. |
| **Swift Package Manager** | Library distributed via SPM. Add via Xcode UI or `Package.swift`. |
| **Manual XCFramework** | Pre-built xcframework. Drag into Xcode project, set "Do Not Embed" for static linking. |

## Step 6: Implement the Swift Wrapper

Create `DotnetMyBinding.swift` with these requirements:

- All classes must inherit from `NSObject` and have `@objc(ClassName)` annotation
- All methods must be `public` with `@objc(selector:)` annotation
- Use only Objective-C compatible types (see Type Mapping table below)
- Use completion handlers `(Result?, NSError?) -> Void` for async operations
- Convert all errors to `NSError` for proper propagation to C#

## Step 7: Generate ApiDefinition.cs

After building, find the generated Swift header and use Objective Sharpie:

```bash
sharpie bind --output=sharpie-output --namespace=MyBinding --sdk=iphoneos18.0 \
  --scope=Headers "path/to/MyBinding-Swift.h"
```

Clean up the generated output: add `[Async]` attributes, `[NullAllowed]` for nullable types, remove `[Verify]` attributes after review, and remove `InitWithCoder` constructors.

## Step 8: Build the Final Binding

```bash
dotnet build -c Release
```

## Step 9: Use in Your MAUI App

Add a conditional `<ProjectReference>`, initialize in `MauiProgram.cs` with `#if IOS || MACCATALYST`, and use `[Async]`-generated methods with `await`.

# Updating Bindings When Native SDK Changes

1. Update native dependency version (CocoaPods: `pod update`, SPM: update version, or replace xcframework)
2. Update Swift wrapper if APIs changed
3. Clean and rebuild: `dotnet clean && dotnet build`
4. Regenerate Objective Sharpie output and diff with existing `ApiDefinition.cs`
5. Merge changes: add new bindings, update signatures, preserve custom attributes.
6. Test with sample app

> For detailed update instructions, see [references/ios-slim-bindings-implementation.md](references/ios-slim-bindings-implementation.md).

# Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| "Framework not found" | XCFramework path incorrect or missing architectures | Verify path in `<XcodeProject>`, check `lipo -info` |
| "Undefined symbols for architecture" | Missing linked frameworks or symbol visibility | Add system frameworks, verify `public` + `@objc` annotations |
| "No type or protocol named" | Missing import or protocol mismatch | Add `using` directives, use interface types |
| "Duplicate symbol" | Multiple framework references | Remove duplicates, resolve version conflicts |
| "Native class hasn't been loaded" (runtime) | Framework not embedded or missing `@objc` | Set `<ForceLoad>true</ForceLoad>`, check annotations |
| "unrecognized selector" (runtime) | Selector mismatch between C# and Swift | Verify `[Export("...")]` matches `@objc(...)` exactly |
| "Library not loaded: @rpath" (runtime) | Swift runtime or embedding issue | Add linker flags, set "Embed & Sign" for dynamic frameworks |
| Callbacks not working | Wrong thread, GC'd reference, or missing `@escaping` | Use `DispatchQueue.main.async`, hold strong reference |

> For detailed troubleshooting with code examples, see [references/ios-slim-bindings-implementation.md](references/ios-slim-bindings-implementation.md).

# Quick Reference

## Swift Wrapper Annotation Requirements

```swift
@objc(ClassName)
public class ClassName: NSObject {
    @objc(methodNameWithParam:anotherParam:)
    public func methodName(param: String, anotherParam: Int) -> Bool { ... }

    @objc(staticMethodWithValue:)
    public static func staticMethod(value: String) -> String { ... }
}
```

## Type Mapping

| Swift Type | Objective-C Type | C# Type |
|------------|------------------|---------|
| `String` | `NSString *` | `string` |
| `Bool` | `BOOL` | `bool` |
| `Int`, `Int32` | `int` | `int` |
| `Int64` | `long long` | `long` |
| `Double` | `double` | `double` |
| `Float` | `float` | `float` |
| `Data` | `NSData *` | `NSData` |
| `[String: Any]` | `NSDictionary *` | `NSDictionary` |
| `[Any]` | `NSArray *` | `NSArray` |
| `UIView` | `UIView *` | `UIView` |
| `UIImage` | `UIImage *` | `UIImage` |
| `URL` | `NSURL *` | `NSUrl` |
| Custom Class | Must inherit `NSObject` | Interface with `[BaseType]` |
| `(Result, Error?) -> Void` | Block | `Action<Result?, NSError?>` |

## ApiDefinition Attributes

| Attribute | Purpose |
|-----------|---------|
| `[BaseType(typeof(NSObject))]` | Specifies base class |
| `[Static]` | Static method/property |
| `[Export("selector:")]` | Objective-C selector |
| `[Async]` | Generate async wrapper for completion handler methods |
| `[NullAllowed]` | Nullable parameter/return |
| `[Protocol]` | Objective-C protocol |
| `[Model]` | Protocol implementation |
| `[Abstract]` | Required protocol method |
| `[Internal]` | Don't expose publicly |
| `[Sealed]` | Prevent subclassing |

## XcodeProject MSBuild Properties

| Property | Description | Default |
|----------|-------------|---------|
| `SchemeName` | Xcode scheme to build | Required |
| `Configuration` | Build configuration | `Release` |
| `Kind` | `Framework` or `Static` | Auto-detected |
| `SmartLink` | Enable smart linking | `true` |
| `ForceLoad` | Force load all symbols | `false` |

# Resources

## Official Documentation
- [Native Library Interop - .NET Community Toolkit](https://learn.microsoft.com/dotnet/communitytoolkit/maui/native-library-interop)
- [iOS Binding Project Migration](https://learn.microsoft.com/dotnet/maui/migration/ios-binding-projects)
- [Binding Objective-C Libraries](https://learn.microsoft.com/xamarin/cross-platform/macios/binding/)
- [Objective Sharpie](https://learn.microsoft.com/previous-versions/xamarin/cross-platform/macios/binding/objective-sharpie/)

## Tools
- [XcodeGen](https://github.com/yonaskolb/XcodeGen) — Generate Xcode projects from YAML specification

## Templates and Examples
- [Maui.NativeLibraryInterop Repository](https://github.com/CommunityToolkit/Maui.NativeLibraryInterop)
- [CommunityToolkit.Maui Bindings](https://github.com/CommunityToolkit/Maui)

## Reference Files
- [ios-bindings-guide.md](references/ios-bindings-guide.md) — comprehensive binding reference
- [ios-slim-bindings-implementation.md](references/ios-slim-bindings-implementation.md) — detailed code examples, troubleshooting, and complete scripts
- For Android bindings, use the **android-slim-bindings** skill

# Output Format

When assisting with iOS slim bindings, provide:

1. **Project structure** - File/folder layout for the binding
2. **Swift wrapper code** - Complete `DotnetMyBinding.swift` implementation
3. **Xcode configuration** - Build settings and dependency setup
4. **C# binding project** - `.csproj` and `ApiDefinition.cs` files
5. **Usage examples** - How to call the binding from MAUI/C#
6. **Troubleshooting guidance** - Common issues and solutions for the specific library

Always verify:
- Swift classes have `@objc(ClassName)` annotations
- Methods have `@objc(selector:)` annotations matching Objective-C conventions
- Types are marshallable between Swift and C#
- Async operations use completion handlers with `[Async]` attribute
- Error handling uses `NSError` for proper propagation
