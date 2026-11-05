# iOS Slim Bindings — Detailed Implementation Guide

This reference contains detailed code examples, step-by-step instructions, troubleshooting, and complete scripts for creating iOS slim bindings. For the high-level workflow overview, see the main [SKILL.md](../SKILL.md).

---

## Step 1: Create Project Structure from Command Line

### Prerequisites

Install XcodeGen (generates Xcode projects from YAML):

```bash
brew install xcodegen
```

### Create Directory Structure

```bash
BINDING_NAME="MyBinding"

mkdir -p ${BINDING_NAME}/macios/native/${BINDING_NAME}/${BINDING_NAME}
mkdir -p ${BINDING_NAME}/macios/${BINDING_NAME}.MaciOS.Binding
mkdir -p ${BINDING_NAME}/sample/MauiSample

cd ${BINDING_NAME}
```

## Step 2: Create the Xcode Project with XcodeGen

### Create the XcodeGen Project Spec

Create `macios/native/${BINDING_NAME}/project.yml`:

```yaml
name: MyBinding
options:
  bundleIdPrefix: com.example
  deploymentTarget:
    iOS: "15.0"
    macOS: "12.0"
  xcodeVersion: "15.0"
  generateEmptyDirectories: true

settings:
  base:
    MARKETING_VERSION: "1.0.0"
    CURRENT_PROJECT_VERSION: "1"
    BUILD_LIBRARY_FOR_DISTRIBUTION: YES
    SKIP_INSTALL: NO
    MACH_O_TYPE: staticlib
    SWIFT_VERSION: "5.0"
    ENABLE_BITCODE: NO
    DEFINES_MODULE: YES

targets:
  MyBinding:
    type: framework
    platform: iOS
    sources:
      - path: MyBinding
        type: group
    settings:
      base:
        INFOPLIST_FILE: MyBinding/Info.plist
        PRODUCT_BUNDLE_IDENTIFIER: com.example.mybinding
        PRODUCT_NAME: MyBinding
        TARGETED_DEVICE_FAMILY: "1,2"
    scheme:
      gatherCoverageData: false
      shared: true
```

### Create the Swift Source File

```bash
cat > macios/native/${BINDING_NAME}/${BINDING_NAME}/Dotnet${BINDING_NAME}.swift << 'EOF'
import Foundation
import UIKit

@objc(DotnetMyBinding)
public class DotnetMyBinding: NSObject {
    
    @objc(initialize)
    public static func initialize() {
        print("MyBinding initialized")
    }
    
    @objc(getVersion)
    public static func getVersion() -> String {
        return "1.0.0"
    }
    
    @objc(fetchDataWithQuery:completion:)
    public static func fetchData(
        query: String,
        completion: @escaping (String?, NSError?) -> Void
    ) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
            completion("Result for: \(query)", nil)
        }
    }
    
    @objc(createViewWithFrame:)
    public static func createView(frame: CGRect) -> UIView {
        let view = UIView(frame: frame)
        view.backgroundColor = .systemBlue
        return view
    }
}
EOF
```

### Create Info.plist

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>$(DEVELOPMENT_LANGUAGE)</string>
    <key>CFBundleExecutable</key>
    <string>$(EXECUTABLE_NAME)</string>
    <key>CFBundleIdentifier</key>
    <string>$(PRODUCT_BUNDLE_IDENTIFIER)</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>$(PRODUCT_NAME)</string>
    <key>CFBundlePackageType</key>
    <string>$(PRODUCT_BUNDLE_PACKAGE_TYPE)</string>
    <key>CFBundleShortVersionString</key>
    <string>$(MARKETING_VERSION)</string>
    <key>CFBundleVersion</key>
    <string>$(CURRENT_PROJECT_VERSION)</string>
    <key>NSPrincipalClass</key>
    <string></string>
</dict>
</plist>
```

### Generate the Xcode Project

```bash
cd macios/native/${BINDING_NAME}
xcodegen generate
cd ../../..
```

## Step 3: Create the C# Binding Project

### Create the Binding .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0-ios;net9.0-maccatalyst</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>true</ImplicitUsings>
    <IsBindingProject>true</IsBindingProject>
    <PackageId>MyBinding.MaciOS</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <XcodeProject Include="../native/MyBinding/MyBinding.xcodeproj">
      <SchemeName>MyBinding</SchemeName>
    </XcodeProject>
  </ItemGroup>

  <ItemGroup>
    <ObjcBindingApiDefinition Include="ApiDefinition.cs" />
  </ItemGroup>
</Project>
```

### Create Initial ApiDefinition.cs

```csharp
using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace MyBinding
{
    [BaseType(typeof(NSObject))]
    interface DotnetMyBinding
    {
        [Static]
        [Export("initialize")]
        void Initialize();

        [Static]
        [Export("getVersion")]
        string GetVersion();

        [Static]
        [Export("fetchDataWithQuery:completion:")]
        [Async]
        void FetchData(string query, Action<string?, NSError?> completion);

        [Static]
        [Export("createViewWithFrame:")]
        UIView CreateView(CGRect frame);
    }
}
```

## Step 4: Build and Verify

```bash
cd macios/${BINDING_NAME}.MaciOS.Binding
dotnet build
```

Verify the output:
```bash
find bin -name "*.xcframework" -type d
find bin -name "*-Swift.h" -type f
```

## CocoaPods Support

### Create Podfile

```ruby
platform :ios, '15.0'

target 'MyBinding' do
  use_frameworks! :linkage => :static
  
  # pod 'FirebaseMessaging', '~> 10.0'
end

post_install do |installer|
  installer.pods_project.targets.each do |target|
    target.build_configurations.each do |config|
      config.build_settings['BUILD_LIBRARY_FOR_DISTRIBUTION'] = 'YES'
      config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'
    end
  end
end
```

### Install Pods

```bash
cd macios/native/${BINDING_NAME}
pod install
cd ../../..

# Update the binding project to use xcworkspace instead of xcodeproj
sed -i '' 's/\.xcodeproj/\.xcworkspace/g' macios/${BINDING_NAME}.MaciOS.Binding/${BINDING_NAME}.MaciOS.Binding.csproj
```

## Swift Package Manager Support

```swift
// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "MyBinding",
    platforms: [.iOS(.v15), .macCatalyst(.v15)],
    products: [
        .library(name: "MyBinding", type: .static, targets: ["MyBinding"])
    ],
    dependencies: [
        .package(url: "https://github.com/example/SomeLibrary.git", from: "1.0.0")
    ],
    targets: [
        .target(
            name: "MyBinding",
            dependencies: [
                .product(name: "SomeLibrary", package: "SomeLibrary")
            ]
        )
    ]
)
```

## Full Swift Wrapper Example

```swift
import Foundation
import UIKit
import TheNativeLibrary

@objc(DotnetMyBinding)
public class DotnetMyBinding: NSObject {
    
    // MARK: - Initialization
    
    @objc(initializeWithApiKey:)
    public static func initialize(apiKey: String) {
        TheNativeLibrary.configure(withApiKey: apiKey)
    }
    
    @objc(isInitialized)
    public static func isInitialized() -> Bool {
        return TheNativeLibrary.isConfigured
    }
    
    // MARK: - Synchronous Methods
    
    @objc(getVersion)
    public static func getVersion() -> String {
        return TheNativeLibrary.version
    }
    
    @objc(processDataWithInput:)
    public static func processData(input: String) -> String? {
        guard let result = TheNativeLibrary.process(input) else { return nil }
        return result.stringValue
    }
    
    // MARK: - Asynchronous Methods
    
    @objc(fetchDataWithQuery:completion:)
    public static func fetchData(
        query: String,
        completion: @escaping (String?, NSError?) -> Void
    ) {
        TheNativeLibrary.fetch(query: query) { result in
            switch result {
            case .success(let data):
                completion(data.stringValue, nil)
            case .failure(let error):
                completion(nil, error as NSError)
            }
        }
    }
    
    @objc(performOperationWithConfig:completion:)
    public static func performOperation(
        config: NSDictionary,
        completion: @escaping (NSData?, NSError?) -> Void
    ) {
        guard let configDict = config as? [String: Any] else {
            let error = NSError(
                domain: "DotnetMyBinding", code: -1,
                userInfo: [NSLocalizedDescriptionKey: "Invalid configuration"]
            )
            completion(nil, error)
            return
        }
        TheNativeLibrary.performOperation(config: configDict) { result in
            switch result {
            case .success(let data): completion(data, nil)
            case .failure(let error): completion(nil, error as NSError)
            }
        }
    }
    
    // MARK: - View Creation
    
    @objc(createViewWithFrame:)
    public static func createView(frame: CGRect) -> UIView {
        let nativeView = TheNativeLibrary.createCustomView()
        nativeView.frame = frame
        return nativeView
    }
    
    @objc(createViewWithFrame:options:)
    public static func createView(frame: CGRect, options: NSDictionary) -> UIView {
        let config = options as? [String: Any] ?? [:]
        let nativeView = TheNativeLibrary.createCustomView(options: config)
        nativeView.frame = frame
        return nativeView
    }
    
    // MARK: - Delegate/Callback Pattern
    
    private static var callbackHandler: ((String) -> Void)?
    
    @objc(registerCallbackWithHandler:)
    public static func registerCallback(handler: @escaping (String) -> Void) {
        callbackHandler = handler
        TheNativeLibrary.setEventHandler { event in
            callbackHandler?(event.description)
        }
    }
    
    @objc(unregisterCallback)
    public static func unregisterCallback() {
        callbackHandler = nil
        TheNativeLibrary.setEventHandler(nil)
    }
}
```

### Completion Handler Pattern

```swift
// Swift
@objc(operationWithInput:completion:)
public static func operation(
    input: String,
    completion: @escaping (String?, NSError?) -> Void
) {
    DispatchQueue.main.async {
        completion(result, nil)    // Success
        // OR
        completion(nil, error as NSError)  // Failure
    }
}
```

```csharp
// C# ApiDefinition.cs - Add [Async] for automatic async wrapper
[Static]
[Export("operationWithInput:completion:")]
[Async]
void Operation(string input, Action<string?, NSError?> completion);

// Usage
var result = await DotnetMyBinding.OperationAsync("input");
```

### Error Handling Pattern

```swift
@objc(riskyOperationWithCompletion:)
public static func riskyOperation(completion: @escaping (Bool, NSError?) -> Void) {
    do {
        try TheNativeLibrary.riskyOperation()
        completion(true, nil)
    } catch {
        let nsError = NSError(
            domain: "DotnetMyBinding",
            code: (error as NSError).code,
            userInfo: [
                NSLocalizedDescriptionKey: error.localizedDescription,
                NSUnderlyingErrorKey: error
            ]
        )
        completion(false, nsError)
    }
}
```

## Full C# Binding Project (Alternative .csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0-ios;net9.0-maccatalyst</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>true</ImplicitUsings>
    <IsBindingProject>true</IsBindingProject>
  </PropertyGroup>

  <ItemGroup>
    <XcodeProject Include="../native/MyBinding/MyBinding.xcodeproj">
      <SchemeName>MyBinding</SchemeName>
    </XcodeProject>
  </ItemGroup>

  <!-- If using xcworkspace (CocoaPods) -->
  <!--
  <ItemGroup>
    <XcodeProject Include="../native/MyBinding/MyBinding.xcworkspace">
      <SchemeName>MyBinding</SchemeName>
    </XcodeProject>
  </ItemGroup>
  -->

  <ItemGroup>
    <ObjcBindingApiDefinition Include="ApiDefinition.cs" />
  </ItemGroup>
</Project>
```

### XcodeProject Properties

| Property | Description | Default |
|----------|-------------|---------|
| `SchemeName` | Xcode scheme to build | Required |
| `Configuration` | Build configuration | `Release` |
| `Kind` | `Framework` or `Static` | Auto-detected |
| `SmartLink` | Enable smart linking | `true` |
| `ForceLoad` | Force load all symbols | `false` |

## Generate ApiDefinition.cs with Objective Sharpie

### Install Objective Sharpie

```bash
brew install --cask objectivesharpie
```

### Check Available SDKs

```bash
sharpie xcode -sdks
```

### Generate Bindings

```bash
HEADER_PATH="bin/Debug/net9.0-ios/MyBinding.MaciOS.Binding.resources/MyBindingiOS.xcframework/ios-arm64/MyBinding.framework/Headers/MyBinding-Swift.h"
SDK_VERSION="iphoneos18.0"
NAMESPACE="MyBinding"

sharpie bind \
  --output=sharpie-output \
  --namespace=$NAMESPACE \
  --sdk=$SDK_VERSION \
  --scope=Headers \
  "$HEADER_PATH"
```

### Full ApiDefinition.cs Example

```csharp
using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace MyBinding
{
    [BaseType(typeof(NSObject))]
    interface DotnetMyBinding
    {
        [Static]
        [Export("initializeWithApiKey:")]
        void Initialize(string apiKey);

        [Static]
        [Export("isInitialized")]
        bool IsInitialized { get; }

        [Static]
        [Export("getVersion")]
        string GetVersion();

        [Static]
        [Export("processDataWithInput:")]
        [return: NullAllowed]
        string ProcessData(string input);

        [Static]
        [Export("fetchDataWithQuery:completion:")]
        [Async]
        void FetchData(string query, Action<string?, NSError?> completion);

        [Static]
        [Export("performOperationWithConfig:completion:")]
        [Async]
        void PerformOperation(NSDictionary config, Action<NSData?, NSError?> completion);

        [Static]
        [Export("createViewWithFrame:")]
        UIView CreateView(CGRect frame);

        [Static]
        [Export("createViewWithFrame:options:")]
        UIView CreateView(CGRect frame, NSDictionary options);

        [Static]
        [Export("registerCallbackWithHandler:")]
        void RegisterCallback(Action<string> handler);

        [Static]
        [Export("unregisterCallback")]
        void UnregisterCallback();
    }
}
```

### Common Cleanup Tasks

| Issue | Solution |
|-------|----------|
| Missing namespace | Add `namespace MyBinding { ... }` |
| `[Verify]` attributes | Review each, remove after confirming correctness |
| `InitWithCoder` constructors | Remove — conflicts with linker |
| Protocol type mismatches | Use interface types (e.g., `ICAAnimation`) |
| Missing `[NullAllowed]` | Add for nullable parameters/returns |
| Completion handlers | Add `[Async]` attribute for async generation |

## Use in Your MAUI App

### Add Project Reference

```xml
<ItemGroup Condition="$(TargetFramework.Contains('ios')) Or $(TargetFramework.Contains('maccatalyst'))">
  <ProjectReference Include="..\..\macios\MyBinding.MaciOS.Binding\MyBinding.MaciOS.Binding.csproj" />
</ItemGroup>
```

### Initialize in MauiProgram.cs

```csharp
using Microsoft.Maui.Hosting;

#if IOS || MACCATALYST
using MyBinding;
#endif

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if IOS || MACCATALYST
        DotnetMyBinding.Initialize("your-api-key");
#endif

        return builder.Build();
    }
}
```

### Use Async APIs

```csharp
#if IOS || MACCATALYST
using MyBinding;
#endif

public partial class MainPage : ContentPage
{
    private async void OnFetchClicked(object sender, EventArgs e)
    {
#if IOS || MACCATALYST
        try
        {
            var result = await DotnetMyBinding.FetchDataAsync("my query");
            await DisplayAlert("Success", result ?? "No data", "OK");
        }
        catch (NSErrorException ex)
        {
            await DisplayAlert("Error", ex.Error.LocalizedDescription, "OK");
        }
#endif
    }

    private void OnCreateViewClicked(object sender, EventArgs e)
    {
#if IOS || MACCATALYST
        var nativeView = DotnetMyBinding.CreateView(new CoreGraphics.CGRect(0, 0, 300, 200));
#endif
    }
}
```

### Register Callbacks

```csharp
#if IOS || MACCATALYST
protected override void OnAppearing()
{
    base.OnAppearing();
    DotnetMyBinding.RegisterCallback((message) =>
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = $"Event: {message}";
        });
    });
}

protected override void OnDisappearing()
{
    base.OnDisappearing();
    DotnetMyBinding.UnregisterCallback();
}
#endif
```

## Updating Bindings When Native SDK Changes

### 1. Update Native Dependency Version

**CocoaPods:**
```ruby
pod 'FirebaseMessaging', '~> 11.0'
```
```bash
cd macios/native/MyBinding
pod update
```

**Swift Package Manager:** Update version in Xcode's Package Dependencies or `Package.swift`.

**Manual XCFramework:** Replace the xcframework file with the new version.

### 2. Update Swift Wrapper (If Needed)

Review release notes and update `DotnetMyBinding.swift`:
- Add new methods for new APIs
- Update method signatures for changed APIs
- Remove deprecated API wrappers

### 3. Regenerate API Definition

```bash
cd macios/MyBinding.MaciOS.Binding
dotnet clean
dotnet build

sharpie bind \
  --output=sharpie-output-new \
  --namespace=MyBinding \
  --sdk=iphoneos18.0 \
  --scope=Headers \
  "bin/Debug/net9.0-ios/MyBinding.MaciOS.Binding.resources/MyBindingiOS.xcframework/ios-arm64/MyBinding.framework/Headers/MyBinding-Swift.h"
```

### 4. Diff and Merge Changes

```bash
diff ApiDefinition.cs sharpie-output-new/ApiDefinitions.cs
```

Manually merge: add new bindings, update changed signatures, remove deleted methods, preserve custom attributes.

### 5. Test the Updated Bindings

```bash
dotnet build -c Release
dotnet test
```

## Troubleshooting

### Build Errors

#### "Framework not found" / "Library not found"

1. **XCFramework path incorrect** — Verify the path in `<XcodeProject>` or `<NativeReference>`
2. **Missing architectures** — Ensure xcframework includes arm64 (device) and arm64/x86_64 (simulator)
3. **CocoaPods not installed** — Run `pod install` in the native directory

```bash
lipo -info path/to/Framework.framework/Framework
```

#### "Undefined symbols for architecture"

1. **Missing linked frameworks** — Add system frameworks to Xcode project's "Link Binary with Libraries"
2. **Static vs Dynamic mismatch** — Ensure consistent linkage
3. **Symbol visibility** — Verify Swift classes/methods are `public` and have `@objc`

```xml
<XcodeProject Include="...">
  <SchemeName>MyBinding</SchemeName>
  <ForceLoad>true</ForceLoad>
  <SmartLink>false</SmartLink>
</XcodeProject>
```

#### "No type or protocol named..."

1. **Missing import** — Add required imports to `ApiDefinition.cs`
2. **Protocol vs Interface** — Use interface types (`ICAAnimation` not `CAAnimation`)
3. **Namespace mismatch** — Verify namespace matches

#### "Duplicate symbol" / "Symbol already defined"

1. **Multiple references to same framework** — Check for duplicate entries
2. **Conflicting dependency versions** — Resolve CocoaPods/SPM version conflicts
3. **InitWithCoder constructor** — Remove from ApiDefinition.cs

#### Objective Sharpie Errors

**"Unable to find SDK":**
```bash
sharpie xcode -sdks
xcode-select --install
sudo xcode-select -s /Applications/Xcode.app
```

**"Parse error in header":**
- Simplify the Swift wrapper to use basic types
- Use `--scope=Headers` to limit parsing

### Runtime Errors

#### "Native class hasn't been loaded"

1. **Framework not embedded** — Check native resources are included in app bundle
2. **Static library not linked** — Verify `<ForceLoad>true</ForceLoad>` is set
3. **Missing Objective-C class registration** — Ensure `@objc(ClassName)` annotation is present

#### "unrecognized selector sent to instance"

1. **Selector mismatch** — Verify `[Export("selector:")]` matches Swift `@objc(selector:)` exactly
2. **Method signature mismatch** — Check parameter count and types
3. **Static vs instance method** — Ensure `[Static]` attribute is correct

#### "Library not loaded: @rpath/..."

1. **Swift runtime missing** — Add linker flags for Swift libraries
2. **Framework not embedded** — Set "Embed & Sign" in Xcode for dynamic frameworks
3. **rpath not set** — Add `-Wl,-rpath -Wl,@executable_path/Frameworks`

#### Callbacks Not Working

1. **Callback on wrong thread** — Use `DispatchQueue.main.async` in Swift
2. **Callback garbage collected** — Store strong reference to handler
3. **Missing `@escaping`** — Completion handlers must be `@escaping` in Swift

### IntelliSense Issues

IntelliSense shows errors but project compiles — this is expected. Binding projects don't use source generators. Build first, then reload the solution.

## NativeReference MSBuild Properties (Traditional Bindings)

```xml
<NativeReference Include="Library.xcframework">
  <Kind>Framework</Kind>
  <Frameworks>Foundation UIKit</Frameworks>
  <LinkerFlags>-lsqlite3</LinkerFlags>
  <SmartLink>true</SmartLink>
  <ForceLoad>false</ForceLoad>
  <IsCxx>false</IsCxx>
</NativeReference>
```

## Using the Community Toolkit Template (Alternative)

```bash
git clone https://github.com/CommunityToolkit/Maui.NativeLibraryInterop
cp -r Maui.NativeLibraryInterop/template ./MyBinding
cd MyBinding

find . -name "*NewBinding*" -exec bash -c 'mv "$0" "${0//NewBinding/MyBinding}"' {} \;
find . -type f \( -name "*.cs" -o -name "*.csproj" -o -name "*.swift" -o -name "*.yml" \) | xargs sed -i '' 's/NewBinding/MyBinding/g'
```

The template includes pre-configured Xcode project, binding .csproj, sample MAUI app, and CI/CD workflows.

## Complete Script: Create iOS Binding Project

```bash
#!/bin/bash
set -e

BINDING_NAME="${1:-MyBinding}"
BUNDLE_ID_PREFIX="${2:-com.example}"
MIN_IOS_VERSION="${3:-15.0}"

echo "Creating iOS binding project: ${BINDING_NAME}"

if ! command -v xcodegen &> /dev/null; then
    echo "Installing xcodegen..."
    brew install xcodegen
fi

mkdir -p ${BINDING_NAME}/macios/native/${BINDING_NAME}/${BINDING_NAME}
mkdir -p ${BINDING_NAME}/macios/${BINDING_NAME}.MaciOS.Binding
cd ${BINDING_NAME}

# Create XcodeGen project spec
cat > macios/native/${BINDING_NAME}/project.yml << EOF
name: ${BINDING_NAME}
options:
  bundleIdPrefix: ${BUNDLE_ID_PREFIX}
  deploymentTarget:
    iOS: "${MIN_IOS_VERSION}"
    macOS: "12.0"
settings:
  base:
    BUILD_LIBRARY_FOR_DISTRIBUTION: YES
    SKIP_INSTALL: NO
    MACH_O_TYPE: staticlib
    SWIFT_VERSION: "5.0"
    DEFINES_MODULE: YES
targets:
  ${BINDING_NAME}:
    type: framework
    platform: iOS
    sources:
      - path: ${BINDING_NAME}
        type: group
    settings:
      base:
        INFOPLIST_FILE: ${BINDING_NAME}/Info.plist
        PRODUCT_BUNDLE_IDENTIFIER: ${BUNDLE_ID_PREFIX}.${BINDING_NAME,,}
        PRODUCT_NAME: ${BINDING_NAME}
    scheme:
      shared: true
EOF

# Create Swift wrapper
cat > macios/native/${BINDING_NAME}/${BINDING_NAME}/Dotnet${BINDING_NAME}.swift << EOF
import Foundation
import UIKit

@objc(Dotnet${BINDING_NAME})
public class Dotnet${BINDING_NAME}: NSObject {
    @objc(initialize) public static func initialize() { print("${BINDING_NAME} initialized") }
    @objc(getVersion) public static func getVersion() -> String { return "1.0.0" }
}
EOF

# Create Info.plist
cat > macios/native/${BINDING_NAME}/${BINDING_NAME}/Info.plist << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key><string>$(DEVELOPMENT_LANGUAGE)</string>
    <key>CFBundleExecutable</key><string>$(EXECUTABLE_NAME)</string>
    <key>CFBundleIdentifier</key><string>$(PRODUCT_BUNDLE_IDENTIFIER)</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>$(PRODUCT_NAME)</string>
    <key>CFBundlePackageType</key><string>$(PRODUCT_BUNDLE_PACKAGE_TYPE)</string>
    <key>CFBundleShortVersionString</key><string>$(MARKETING_VERSION)</string>
    <key>CFBundleVersion</key><string>$(CURRENT_PROJECT_VERSION)</string>
</dict>
</plist>
EOF

cd macios/native/${BINDING_NAME}
xcodegen generate
cd ../../..

# Create binding .csproj
cat > macios/${BINDING_NAME}.MaciOS.Binding/${BINDING_NAME}.MaciOS.Binding.csproj << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0-ios;net9.0-maccatalyst</TargetFrameworks>
    <IsBindingProject>true</IsBindingProject>
  </PropertyGroup>
  <ItemGroup>
    <XcodeProject Include="../native/${BINDING_NAME}/${BINDING_NAME}.xcodeproj">
      <SchemeName>${BINDING_NAME}</SchemeName>
    </XcodeProject>
  </ItemGroup>
  <ItemGroup>
    <ObjcBindingApiDefinition Include="ApiDefinition.cs" />
  </ItemGroup>
</Project>
EOF

# Create ApiDefinition.cs
cat > macios/${BINDING_NAME}.MaciOS.Binding/ApiDefinition.cs << EOF
using Foundation;
namespace ${BINDING_NAME}
{
    [BaseType(typeof(NSObject))]
    interface Dotnet${BINDING_NAME}
    {
        [Static] [Export("initialize")] void Initialize();
        [Static] [Export("getVersion")] string GetVersion();
    }
}
EOF

echo "✅ Created ${BINDING_NAME} binding project!"
```

Usage:
```bash
chmod +x create-ios-binding.sh
./create-ios-binding.sh MyAwesomeBinding com.mycompany 15.0
```
