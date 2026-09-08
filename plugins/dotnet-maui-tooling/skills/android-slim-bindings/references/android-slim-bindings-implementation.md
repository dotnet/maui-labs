# Android Slim Bindings — Detailed Implementation Guide

This reference contains detailed code examples, step-by-step instructions, troubleshooting, and complete scripts for creating Android slim bindings. For the high-level workflow overview, see the main [SKILL.md](../SKILL.md).

---

## Step 1: Prerequisites

Ensure you have:
- JDK 17+
- Android SDK with `ANDROID_HOME` environment variable set
- .NET SDK 9+ (recommended for Java Dependency Verification)

> **Note:** You don't need Gradle installed globally—we'll use the Gradle Wrapper which downloads the correct version automatically.

## Step 2: Create the Android Library Project

### Option A: Use Android Studio (Recommended for GUI)

1. **Open Android Studio**
2. **File → New → New Project**
3. Select **"No Activity"** template
4. Configure:
   - Name: `MyBindingNative`
   - Package name: `com.example.mybinding`
   - Language: Java or Kotlin
   - Minimum SDK: API 21
5. **File → New → New Module**
6. Select **"Android Library"**
7. Name it `app` (or your preferred module name)

### Option B: Use `gradle init` with Wrapper (CLI)

```bash
# Set your binding name
BINDING_NAME="MyBinding"
PACKAGE_NAME="com.example.mybinding"

# Create directory structure
mkdir -p ${BINDING_NAME}/android/native
mkdir -p ${BINDING_NAME}/android/${BINDING_NAME}.Android.Binding/Transforms
mkdir -p ${BINDING_NAME}/sample/MauiSample

cd ${BINDING_NAME}/android/native

# Download and use Gradle wrapper (no global Gradle install needed)
curl -sL https://services.gradle.org/distributions/gradle-9.2.1-bin.zip -o gradle.zip
unzip -q gradle.zip
./gradle-9.2.1/bin/gradle wrapper --gradle-version 9.2.1
rm -rf gradle.zip gradle-9.2.1

# Initialize a basic Kotlin library project
./gradlew init --type kotlin-library --dsl kotlin --project-name ${BINDING_NAME}Native --package ${PACKAGE_NAME} --no-split-project --java-version 17

# The project needs to be converted to Android Library (see next section)
```

> **Note:** `gradle init` creates a standard JVM library, not an Android library. You'll need to modify the generated files to add Android support (see "Converting to Android Library" below).

### Option C: Clone from Template Repository

```bash
BINDING_NAME="MyBinding"

# Clone the template
git clone https://github.com/AdrianSimionescu/NativeLibraryInterop-Template.git ${BINDING_NAME}
cd ${BINDING_NAME}

# Or use the official CommunityToolkit repo structure
git clone --depth 1 https://github.com/AdrianSimionescu/NativeLibraryInterop-Template.git ${BINDING_NAME}

# Rename files and references
find . -type f -name "*.gradle*" -exec sed -i '' 's/TemplateBinding/'"${BINDING_NAME}"'/g' {} \;
```

### Option D: Manual Creation with Gradle Wrapper

```bash
BINDING_NAME="MyBinding"
PACKAGE_NAME="com.example.mybinding"
PACKAGE_PATH="${PACKAGE_NAME//./\/}"

# Create directory structure
mkdir -p ${BINDING_NAME}/android/native/app/src/main/java/${PACKAGE_PATH}
mkdir -p ${BINDING_NAME}/android/${BINDING_NAME}.Android.Binding/Transforms
mkdir -p ${BINDING_NAME}/sample/MauiSample

cd ${BINDING_NAME}/android/native

# Create Gradle wrapper
mkdir -p tmp && cd tmp
curl -sL https://services.gradle.org/distributions/gradle-9.2.1-bin.zip -o gradle.zip
unzip -q gradle.zip && ./gradle-9.2.1/bin/gradle wrapper --gradle-version 9.2.1
mv gradlew gradlew.bat gradle ../ && cd .. && rm -rf tmp
```

### Creating Gradle Build Files

**settings.gradle.kts:**

```kotlin
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "MyBindingNative"
include(":app")
```

**build.gradle.kts (root):**

```kotlin
plugins {
    id("com.android.library") version "8.2.2" apply false
    id("org.jetbrains.kotlin.android") version "1.9.22" apply false
}
```

**app/build.gradle.kts:**

```kotlin
plugins {
    id("com.android.library")
}

android {
    namespace = "com.example.mybinding"
    compileSdk = 34

    defaultConfig {
        minSdk = 21
        consumerProguardFiles("consumer-rules.pro")
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    // Add the native library you want to wrap
    // implementation("com.squareup.okhttp3:okhttp:4.12.0")
}
```

### Checking for Latest Versions

```bash
# Check latest Android Gradle Plugin version
open "https://developer.android.com/build/releases/gradle-plugin"

# Check latest Kotlin version
curl -s "https://api.github.com/repos/JetBrains/kotlin/releases/latest" | grep '"tag_name"' | cut -d'"' -f4
```

## Step 3: Analyze the Full Dependency Tree

```bash
# Get full dependency tree
./gradlew app:dependencies --configuration releaseRuntimeClasspath

# Get flat list of all dependencies
./gradlew app:dependencies --configuration releaseRuntimeClasspath | grep -E "^[+\\\\]" | sed 's/.*--- //' | sort -u

# For insight into a specific dependency
./gradlew app:dependencyInsight --dependency kotlin-stdlib --configuration releaseRuntimeClasspath
```

**Understanding the output:**
- `->` indicates version conflict resolution (e.g., `1.9.10 -> 1.9.21`)
- `(*)` indicates the dependency was already shown elsewhere (deduplication)
- `(c)` indicates a constraint

### Document Your Dependencies

| Maven Artifact | Version | NuGet Package | Strategy |
|----------------|---------|---------------|----------|
| `androidx.core:core` | 1.12.0 | `Xamarin.AndroidX.Core` | NuGet |
| `org.jetbrains.kotlin:kotlin-stdlib` | 1.9.22 | `Xamarin.Kotlin.StdLib` | NuGet |
| `com.example:internal-lib` | 1.0.0 | N/A | `Bind="false"` |
| `org.jetbrains:annotations` | 23.0.0 | N/A | Ignore |

## Step 4: Create the Wrapper Class

### Java Wrapper

Create `app/src/main/java/com/example/mybinding/DotnetMyBinding.java`:

```java
package com.example.mybinding;

import android.content.Context;
import android.util.Log;
import android.view.View;

public class DotnetMyBinding {
    private static final String TAG = "DotnetMyBinding";
    private static boolean initialized = false;

    // Initialization
    public static void initialize(Context context, boolean debug) {
        if (initialized) {
            Log.w(TAG, "Already initialized");
            return;
        }
        // Initialize your native library here
        initialized = true;
        Log.d(TAG, "MyBinding initialized");
    }

    public static boolean isInitialized() {
        return initialized;
    }

    // Synchronous Methods
    public static String getVersion() {
        return "1.0.0";
    }

    public static String processData(String input) {
        if (!initialized) {
            throw new IllegalStateException("Must call initialize() first");
        }
        return "Processed: " + input;
    }

    // Asynchronous Methods (Callback Pattern)
    public interface DataCallback {
        void onSuccess(String result);
        void onError(String error);
    }

    public static void fetchDataAsync(String query, final DataCallback callback) {
        if (!initialized) {
            callback.onError("Not initialized");
            return;
        }
        new Thread(() -> {
            try {
                Thread.sleep(100);
                String result = "Result for: " + query;
                callback.onSuccess(result);
            } catch (Exception e) {
                callback.onError(e.getMessage());
            }
        }).start();
    }

    // Complex callback with multiple result types
    public interface ComplexCallback {
        void onSuccess(String data, int count, boolean hasMore);
        void onError(int errorCode, String errorMessage);
    }

    public static void performComplexOperation(
            String input, int options, final ComplexCallback callback) {
        callback.onSuccess("data", 42, true);
    }

    // View Creation
    public static View createView(Context context) {
        View view = new View(context);
        view.setBackgroundColor(0xFF0066CC);
        return view;
    }

    // Event Handling
    public interface EventListener {
        void onEvent(String eventType, String eventData);
    }

    private static EventListener eventListener;

    public static void setEventListener(EventListener listener) {
        eventListener = listener;
    }

    public static void removeEventListener() {
        eventListener = null;
    }

    // Configuration
    public static void configure(
            String apiKey, String apiSecret, int timeout, boolean enableLogging) {
        Log.d(TAG, "Configured with apiKey: " + apiKey);
    }
}
```

### Kotlin Wrapper (Alternative)

Create `app/src/main/kotlin/com/example/mybinding/DotnetMyBinding.kt`:

```kotlin
package com.example.mybinding

import android.content.Context
import android.util.Log
import android.view.View

object DotnetMyBinding {
    private const val TAG = "DotnetMyBinding"
    private var initialized = false

    @JvmStatic
    fun initialize(context: Context, debug: Boolean) {
        if (initialized) { Log.w(TAG, "Already initialized"); return }
        initialized = true
        Log.d(TAG, "MyBinding initialized")
    }

    @JvmStatic fun isInitialized(): Boolean = initialized
    @JvmStatic fun getVersion(): String = "1.0.0"

    @JvmStatic
    fun processData(input: String): String {
        check(initialized) { "Must call initialize() first" }
        return "Processed: $input"
    }

    interface DataCallback {
        fun onSuccess(result: String)
        fun onError(error: String)
    }

    @JvmStatic
    fun fetchDataAsync(query: String, callback: DataCallback) {
        if (!initialized) { callback.onError("Not initialized"); return }
        Thread {
            try {
                Thread.sleep(100)
                callback.onSuccess("Result for: $query")
            } catch (e: Exception) {
                callback.onError(e.message ?: "Unknown error")
            }
        }.start()
    }

    @JvmStatic
    fun createView(context: Context): View {
        return View(context).apply { setBackgroundColor(0xFF0066CC.toInt()) }
    }

    interface EventListener {
        fun onEvent(eventType: String, eventData: String)
    }

    private var eventListener: EventListener? = null

    @JvmStatic fun setEventListener(listener: EventListener?) { eventListener = listener }
    @JvmStatic fun removeEventListener() { eventListener = null }

    @JvmStatic
    fun configure(apiKey: String, apiSecret: String, timeout: Int, enableLogging: Boolean) {
        Log.d(TAG, "Configured with apiKey: $apiKey")
    }
}
```

**Key Points for Kotlin:**
- Use `@JvmStatic` on all methods for static access from C#
- Use `object` for singleton pattern (maps to static methods)
- Use Java-style callback interfaces (not Kotlin lambdas)
- Avoid coroutines in the public API (use callbacks instead)

## Step 5: Build the AAR

```bash
cd android/native
./gradlew :app:assembleRelease
# Output: app/build/outputs/aar/app-release.aar
```

Verify contents:
```bash
unzip -l app/build/outputs/aar/app-release.aar
```

## Step 6: Create the C# Binding Project

```bash
cd android
dotnet new androidbinding -n MyBinding.Android.Binding
cd MyBinding.Android.Binding
```

### Full .csproj Example

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-android</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>true</ImplicitUsings>
    <SupportedOSPlatformVersion>21</SupportedOSPlatformVersion>
  </PropertyGroup>

  <!-- Your wrapper AAR -->
  <ItemGroup>
    <AndroidLibrary Include="../native/app/build/outputs/aar/app-release.aar" />
  </ItemGroup>

  <!-- Dependencies: NuGet packages for common libraries -->
  <ItemGroup>
    <PackageReference Include="Xamarin.AndroidX.Core" Version="1.12.0.4" />
    <PackageReference Include="Xamarin.Kotlin.StdLib" Version="1.9.22.1" />
  </ItemGroup>

  <!-- Dependencies without NuGet: Include but don't bind -->
  <ItemGroup>
    <!-- <AndroidMavenLibrary Include="com.thirdparty:awesomelib" Version="1.0.0" Bind="false" /> -->
  </ItemGroup>

  <!-- Compile-time only dependencies to ignore -->
  <ItemGroup>
    <AndroidIgnoredJavaDependency Include="org.jetbrains:annotations" Version="*" />
  </ItemGroup>
</Project>
```

### Metadata.xml

```xml
<metadata>
  <attr path="/api/package[@name='com.example.mybinding']" 
        name="managedName">MyBinding</attr>
  
  <attr path="/api/package[@name='com.example.mybinding']/class[@name='DotnetMyBinding']/method[@name='initialize']/parameter[@name='p0']" 
        name="name">context</attr>
  <attr path="/api/package[@name='com.example.mybinding']/class[@name='DotnetMyBinding']/method[@name='initialize']/parameter[@name='p1']" 
        name="name">debug</attr>
  <attr path="/api/package[@name='com.example.mybinding']/class[@name='DotnetMyBinding']/method[@name='processData']/parameter[@name='p0']" 
        name="name">input</attr>
  <attr path="/api/package[@name='com.example.mybinding']/class[@name='DotnetMyBinding']/method[@name='fetchDataAsync']/parameter[@name='p0']" 
        name="name">query</attr>
  <attr path="/api/package[@name='com.example.mybinding']/class[@name='DotnetMyBinding']/method[@name='fetchDataAsync']/parameter[@name='p1']" 
        name="name">callback</attr>
</metadata>
```

## Step 7: Resolve Dependencies — Detailed Examples

### Finding NuGet Packages for Maven Dependencies

```bash
# Search NuGet for a specific Maven artifact
dotnet package search "artifact=androidx.core:core" --source https://api.nuget.org/v3/index.json

# Or use curl
curl -s "https://azuresearch-usnc.nuget.org/query?q=tags:artifact=androidx.core:core&take=10" | jq '.data[] | {id: .id, version: .version}'
```

### Practical Example: Complex Library Binding

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-android</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <AndroidLibrary Include="../native/app/build/outputs/aar/app-release.aar" />
  </ItemGroup>

  <!-- Dependencies satisfied by NuGet (preferred) -->
  <ItemGroup>
    <PackageReference Include="Xamarin.AndroidX.Core" Version="1.12.0.4" />
    <PackageReference Include="Xamarin.AndroidX.AppCompat" Version="1.6.1.6" />
    <PackageReference Include="Xamarin.Kotlin.StdLib" Version="1.9.22.1" />
    <PackageReference Include="Square.OkHttp3" Version="4.12.0.1" />
  </ItemGroup>

  <!-- The library being wrapped - include but don't bind -->
  <ItemGroup>
    <AndroidMavenLibrary Include="com.thirdparty:awesomelib" Version="1.0.0" Bind="false" />
  </ItemGroup>

  <!-- Dependencies with no NuGet - include but don't bind -->
  <ItemGroup>
    <AndroidMavenLibrary Include="com.thirdparty:internal-util" Version="1.0.0" Bind="false" />
  </ItemGroup>

  <!-- Compile-time only dependencies to ignore -->
  <ItemGroup>
    <AndroidIgnoredJavaDependency Include="org.jetbrains:annotations" Version="*" />
    <AndroidIgnoredJavaDependency Include="com.google.errorprone:error_prone_annotations" Version="*" />
    <AndroidIgnoredJavaDependency Include="com.google.code.findbugs:jsr305" Version="*" />
  </ItemGroup>
</Project>
```

## Step 8: Customize Bindings with Metadata — Full Examples

### Rename Java Packages to .NET Namespaces

```xml
<metadata>
  <attr path="/api/package[@name='com.example.mybinding']" 
        name="managedName">MyBinding</attr>
  <attr path="/api/package[@name='com.example.mybinding.utils']" 
        name="managedName">MyBinding.Utils</attr>
</metadata>
```

### Rename Classes and Methods

```xml
<metadata>
  <attr path="/api/package[@name='com.example']/class[@name='Util']" 
        name="managedName">Utilities</attr>
  <attr path="/api/package[@name='com.example']/class[@name='MyClass']/method[@name='getData']" 
        name="managedName">GetData</attr>
</metadata>
```

### Rename Parameters

```xml
<metadata>
  <attr path="/api/package[@name='com.example']/class[@name='MyClass']/method[@name='process']/parameter[@name='p0']" 
        name="name">input</attr>
  <attr path="/api/package[@name='com.example']/class[@name='MyClass']/method[@name='process']/parameter[@name='p1']" 
        name="name">options</attr>
</metadata>
```

### Remove Internal or Obfuscated Types

```xml
<metadata>
  <remove-node path="/api/package[starts-with(@name, 'com.example.internal')]" />
  <remove-node path="/api/package[@name='a']" />
  <remove-node path="/api/package[@name='b']" />
  <remove-node path="/api/package[@name='com.example']/class[@name='InternalHelper']" />
  <remove-node path="/api/package/class[contains(@name, '$')]" />
</metadata>
```

### Fix Visibility Issues

```xml
<metadata>
  <attr path="/api/package[@name='com.example']/class[@name='Helper']" 
        name="visibility">public</attr>
</metadata>
```

### Examining api.xml for XPath Construction

```bash
cat obj/Debug/net9.0-android/api.xml
```

## Step 10: Use in Your MAUI App — Full Examples

### Add Project Reference

```xml
<ItemGroup Condition="$(TargetFramework.Contains('android'))">
  <ProjectReference Include="..\android\MyBinding.Android.Binding\MyBinding.Android.Binding.csproj" />
</ItemGroup>
```

### Initialize in MauiProgram.cs

```csharp
using Microsoft.Maui.Hosting;

#if ANDROID
using MyBinding;
#endif

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddAndroid(android =>
            {
                android.OnCreate((activity, bundle) =>
                {
                    DotnetMyBinding.Initialize(activity, debug: true);
                });
            });
        });
#endif

        return builder.Build();
    }
}
```

### Implement Callback Interface

```csharp
#if ANDROID
using MyBinding;
using Android.Runtime;

public class MyDataCallback : Java.Lang.Object, DotnetMyBinding.IDataCallback
{
    private readonly Action<string> _onSuccess;
    private readonly Action<string> _onError;

    public MyDataCallback(Action<string> onSuccess, Action<string> onError)
    {
        _onSuccess = onSuccess;
        _onError = onError;
    }

    public void OnSuccess(string result) => _onSuccess(result);
    public void OnError(string error) => _onError(error);
}
#endif
```

### Use in Your Page

```csharp
public partial class MainPage : ContentPage
{
    public MainPage() { InitializeComponent(); }

    private void OnSyncButtonClicked(object sender, EventArgs e)
    {
#if ANDROID
        var version = DotnetMyBinding.GetVersion();
        var result = DotnetMyBinding.ProcessData("test input");
        DisplayAlert("Result", $"Version: {version}\nResult: {result}", "OK");
#endif
    }

    private void OnAsyncButtonClicked(object sender, EventArgs e)
    {
#if ANDROID
        DotnetMyBinding.FetchDataAsync("my query", new MyDataCallback(
            onSuccess: result => MainThread.BeginInvokeOnMainThread(() => 
                DisplayAlert("Success", result, "OK")),
            onError: error => MainThread.BeginInvokeOnMainThread(() => 
                DisplayAlert("Error", error, "OK"))
        ));
#endif
    }
}
```

### Register Event Listener

```csharp
#if ANDROID
public class MyEventListener : Java.Lang.Object, DotnetMyBinding.IEventListener
{
    private readonly Action<string, string> _onEvent;

    public MyEventListener(Action<string, string> onEvent) { _onEvent = onEvent; }

    public void OnEvent(string eventType, string eventData)
    {
        MainThread.BeginInvokeOnMainThread(() => _onEvent(eventType, eventData));
    }
}

// In your page
protected override void OnAppearing()
{
    base.OnAppearing();
    DotnetMyBinding.SetEventListener(new MyEventListener((type, data) =>
    {
        StatusLabel.Text = $"Event: {type} - {data}";
    }));
}

protected override void OnDisappearing()
{
    base.OnDisappearing();
    DotnetMyBinding.RemoveEventListener();
}
#endif
```

## Updating Bindings When Native SDK Changes

### 1. Update Native Dependency Version

Edit `app/build.gradle.kts`:
```kotlin
dependencies {
    implementation("com.thirdparty:awesomelib:2.0.0")  // Updated version
}
```

### 2. Re-analyze Dependencies

```bash
cd android/native
./gradlew app:dependencies --configuration releaseRuntimeClasspath
```

Compare with your previous dependency mapping and identify new, changed, and removed dependencies.

### 3. Update the Wrapper (If Needed)

Review release notes and update `DotnetMyBinding.java`:
- Add new methods for new APIs
- Update method signatures for changed APIs
- Remove deprecated API wrappers

### 4. Rebuild the AAR

```bash
./gradlew :app:assembleRelease
```

### 5. Update Binding Project Dependencies

Update NuGet packages and Maven dependencies in the binding `.csproj`.

### 6. Rebuild and Fix Errors

```bash
cd ../MyBinding.Android.Binding
dotnet build
```

### 7. Update Metadata.xml (If Needed)

If new classes/methods were added, update parameter names and namespace mappings.

### 8. Test

Build and run the sample app to verify functionality.

## Handling Native Libraries (.so files)

Some Android libraries include native code (`.so` files). If you get `java.lang.UnsatisfiedLinkError`, ensure native libraries are properly included.

### Including .so Files

For AAR files, `.so` files are usually included automatically. For JAR files, add them manually:

```xml
<ItemGroup>
  <AndroidNativeLibrary Include="libs\arm64-v8a\libmynative.so">
    <Abi>arm64-v8a</Abi>
  </AndroidNativeLibrary>
  <AndroidNativeLibrary Include="libs\armeabi-v7a\libmynative.so">
    <Abi>armeabi-v7a</Abi>
  </AndroidNativeLibrary>
  <AndroidNativeLibrary Include="libs\x86_64\libmynative.so">
    <Abi>x86_64</Abi>
  </AndroidNativeLibrary>
</ItemGroup>
```

### Manually Loading Native Libraries

```csharp
Java.Lang.JavaSystem.LoadLibrary("mynative");
```

## Troubleshooting

### Build Errors

#### "Java dependency 'X' is not satisfied" (XA4241/XA4242)

Missing transitive dependency. Follow the dependency resolution decision tree:
1. If XA4242 suggests a NuGet package → Install it
2. If you don't need the API in C# → Use `AndroidMavenLibrary` with `Bind="false"`
3. If it's compile-time only → Use `AndroidIgnoredJavaDependency`

#### "Type is defined multiple times"

Duplicate classes from including the same dependency twice. Check for overlap between NuGet packages and AAR contents. Use `Bind="false"` or remove redundant references.

#### "Class 'X' does not implement interface member 'Y'"

Covariant return types or interface implementation issues. Add custom implementation in `Additions/` folder:

```csharp
namespace Com.Example
{
    public partial class MyClass
    {
        Java.Lang.Object SomeInterface.SomeMethod()
        {
            return RawSomeMethod();
        }
    }
}
```

#### "Cannot find symbol" / "Package does not exist"

Wrapper code references classes not available. Verify all dependencies are declared in `build.gradle.kts` and rebuild the AAR.

#### Gradle Build Failures

```bash
./gradlew clean
./gradlew :app:assembleRelease --stacktrace
```

### Runtime Errors

#### `Java.Lang.NoClassDefFoundError`

Missing dependency at runtime. A required dependency was ignored. Remove it from `AndroidIgnoredJavaDependency` and properly satisfy it.

#### `Java.Lang.UnsatisfiedLinkError`

Native library (.so) not loaded:
1. Ensure .so files are included in the AAR
2. Call `JavaSystem.LoadLibrary()` if needed
3. Check ABI compatibility (arm64-v8a, armeabi-v7a, x86_64)

#### `Java.Lang.IllegalStateException: Must call initialize() first`

Ensure you call `DotnetMyBinding.Initialize()` before using other methods.

#### Callback Not Invoked

1. **Callback on wrong thread** — Use `MainThread.BeginInvokeOnMainThread()` for UI updates
2. **Callback garbage collected** — Store a strong reference to the callback object
3. **Exception in native code** — Add try/catch in wrapper and check logcat

### IntelliSense Issues

IntelliSense shows errors but project compiles — this is normal for binding projects. The binding generator doesn't use source generators. Build the project first and IntelliSense will catch up.

## Callback Interface Pattern

```java
// Java - define a simple callback interface
public interface DataCallback {
    void onSuccess(String result);
    void onError(String error);
}
```

```csharp
// C# - implement the generated interface
public class MyCallback : Java.Lang.Object, DotnetMyBinding.IDataCallback
{
    private readonly Action<string> _onSuccess;
    private readonly Action<string> _onError;

    public MyCallback(Action<string> onSuccess, Action<string> onError)
    {
        _onSuccess = onSuccess;
        _onError = onError;
    }

    public void OnSuccess(string result) => _onSuccess(result);
    public void OnError(string error) => _onError(error);
}
```

## Complete Script: Create Android Binding Project

```bash
#!/bin/bash
set -e

# Configuration
BINDING_NAME="${1:-MyBinding}"
PACKAGE_NAME="${2:-com.example.mybinding}"
MIN_SDK="${3:-21}"
GRADLE_VERSION="${4:-9.2.1}"
AGP_VERSION="${5:-8.2.2}"

echo "Creating Android binding project: ${BINDING_NAME}"
echo "  Package: ${PACKAGE_NAME}"
echo "  Min SDK: ${MIN_SDK}"
echo "  Gradle: ${GRADLE_VERSION}"

# Create directory structure
mkdir -p ${BINDING_NAME}/android/native/app/src/main/java/${PACKAGE_NAME//./\/}
mkdir -p ${BINDING_NAME}/android/${BINDING_NAME}.Android.Binding/Transforms
mkdir -p ${BINDING_NAME}/sample/MauiSample

cd ${BINDING_NAME}/android/native

# Download and setup Gradle Wrapper
echo "Setting up Gradle Wrapper..."
mkdir -p gradle/wrapper
cat > gradle/wrapper/gradle-wrapper.properties << EOF
distributionBase=GRADLE_USER_HOME
distributionPath=wrapper/dists
distributionUrl=https\://services.gradle.org/distributions/gradle-${GRADLE_VERSION}-bin.zip
networkTimeout=10000
validateDistributionUrl=true
zipStoreBase=GRADLE_USER_HOME
zipStorePath=wrapper/dists
EOF

cat > gradlew << 'GRADLEW_EOF'
#!/bin/sh
exec java -jar "$(dirname "$0")/gradle/wrapper/gradle-wrapper.jar" "$@"
GRADLEW_EOF
chmod +x gradlew

curl -sL -o gradle/wrapper/gradle-wrapper.jar \
  "https://raw.githubusercontent.com/gradle/gradle/v${GRADLE_VERSION}/gradle/wrapper/gradle-wrapper.jar" 2>/dev/null || \
  echo "Note: Could not download gradle-wrapper.jar. Run './gradlew' to auto-download."

# Create settings.gradle.kts
cat > settings.gradle.kts << EOF
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "${BINDING_NAME}Native"
include(":app")
EOF

cat > build.gradle.kts << EOF
plugins {
    id("com.android.library") version "${AGP_VERSION}" apply false
}
EOF

cat > app/build.gradle.kts << EOF
plugins {
    id("com.android.library")
}

android {
    namespace = "${PACKAGE_NAME}"
    compileSdk = 34
    defaultConfig { minSdk = ${MIN_SDK} }
    buildTypes { release { isMinifyEnabled = false } }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation("androidx.core:core:1.12.0")
}
EOF

# Create Java wrapper
cat > app/src/main/java/${PACKAGE_NAME//./\/}/Dotnet${BINDING_NAME}.java << EOF
package ${PACKAGE_NAME};

import android.content.Context;
import android.util.Log;

public class Dotnet${BINDING_NAME} {
    private static final String TAG = "Dotnet${BINDING_NAME}";
    private static boolean initialized = false;

    public static void initialize(Context context, boolean debug) {
        if (initialized) return;
        initialized = true;
        Log.d(TAG, "${BINDING_NAME} initialized");
    }

    public static boolean isInitialized() { return initialized; }
    public static String getVersion() { return "1.0.0"; }
    
    public interface DataCallback {
        void onSuccess(String result);
        void onError(String error);
    }
    
    public static void fetchDataAsync(String query, DataCallback callback) {
        if (!initialized) { callback.onError("Not initialized"); return; }
        callback.onSuccess("Result for: " + query);
    }
}
EOF

cd ../..

dotnet new androidbinding -n ${BINDING_NAME}.Android.Binding -o ${BINDING_NAME}.Android.Binding

cat > ${BINDING_NAME}.Android.Binding/Transforms/Metadata.xml << EOF
<metadata>
  <attr path="/api/package[@name='${PACKAGE_NAME}']" 
        name="managedName">${BINDING_NAME}</attr>
</metadata>
EOF

cd ..

echo ""
echo "✅ Created ${BINDING_NAME} Android binding project!"
echo ""
echo "Next steps:"
echo "  1. cd ${BINDING_NAME}/android/native"
echo "  2. Add your native library to app/build.gradle.kts"
echo "  3. ./gradlew :app:assembleRelease"
echo "  4. cd ../${BINDING_NAME}.Android.Binding"
echo "  5. dotnet build"
echo "  6. Resolve any dependency errors"
```

Usage:
```bash
chmod +x create-android-binding.sh
./create-android-binding.sh MyAwesomeBinding
./create-android-binding.sh MyAwesomeBinding com.mycompany.mybinding 21 9.2.1 8.2.2
```
