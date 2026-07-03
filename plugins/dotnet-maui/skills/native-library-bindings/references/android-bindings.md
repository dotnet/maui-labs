# Android Binding Projects

This reference covers Java/Kotlin bindings for .NET for Android and .NET MAUI.

## Project anatomy

Create a binding project with:

```bash
dotnet new android-bindinglib -n MySdk.Android.Binding
```

Core items:

```xml
<ItemGroup>
  <AndroidLibrary Include="libs/vendor-sdk.aar" />
  <TransformFile Include="Transforms/Metadata.xml" />
</ItemGroup>
```

For Maven-hosted artifacts on .NET 9+:

```xml
<ItemGroup>
  <AndroidMavenLibrary Include="com.vendor:sdk" Version="1.2.3" Repository="Central" />
</ItemGroup>
```

`AndroidMavenLibrary` downloads the requested artifact and POM metadata, then feeds dependency verification. It does not automatically include every transitive runtime dependency in your app or package.

## Building a wrapper AAR from a Gradle module

When you author a native Kotlin/Java wrapper (slim binding), let the .NET build compile the Gradle module into an AAR instead of hand-building it. `AndroidGradleProject` is the Android analog of Apple's `XcodeProject`:

```xml
<ItemGroup>
  <AndroidGradleProject Include="../native/build.gradle.kts">
    <ModuleName>myvendorbinding</ModuleName>
    <OutputPath>myvendorbinding-release.aar</OutputPath>
  </AndroidGradleProject>
</ItemGroup>
```

This runs Gradle during the .NET build, produces the wrapper AAR, and binds it. Prefer this over checking a prebuilt AAR into source control — it keeps the wrapper source of truth in Gradle and rebuilds it deterministically. Requires a JDK, Android SDK, and Gradle available to the build.

## Packing and skipping generation for vendor artifacts

`AndroidMavenLibrary` (and `AndroidLibrary`) support metadata that decouples "ship this artifact" from "generate C# for this artifact":

```xml
<AndroidMavenLibrary Include="com.vendor:sdk" Version="$(VendorSdkVersion)"
                     Bind="false" Pack="true" VerifyDependencies="False" />
```

- `Bind="false"` — download and include the artifact but do not generate a C# binding for it (the thin wrapper is your only bound surface).
- `Pack="true"` — include the vendor AAR in the produced NuGet so consumers get it (redistributable bindings).
- `VerifyDependencies="False"` — escape hatch that suppresses XA4241/XA4242 for that artifact. It does not satisfy the runtime graph; it only silences the check. If you use it, you take on manually satisfying every runtime dependency via `PackageReference`, packed `AndroidMavenLibrary`, or `AndroidIgnoredJavaDependency` (compile-only). Reach for it only when you are deliberately managing a large transitive graph by hand.

## Binding local AAR/JAR files

If the artifact is local:

```xml
<AndroidLibrary Include="libs/vendor-sdk-1.2.3.aar"
                Manifest="libs/vendor-sdk-1.2.3.pom"
                JavaArtifact="com.vendor:sdk"
                JavaVersion="1.2.3" />
```

Use `Bind="false"` for dependencies that must be packaged but do not need generated C# API:

```xml
<AndroidLibrary Include="libs/vendor-runtime.aar"
                JavaArtifact="com.vendor:runtime:1.2.3"
                Bind="false" />
```

## Java dependency verification

Java dependency verification emits errors when a binding references Java artifacts whose dependencies are not satisfied.

Typical errors:

```text
error XA4241: Java dependency 'androidx.core:core' is not satisfied.
error XA4242: Java dependency 'com.google.firebase:firebase-common:21.0.0' is not satisfied. Suggested fix: Install NuGet package 'Xamarin.Firebase.Common'.
```

Resolution order:

1. Install the suggested NuGet package for XA4242.
2. Search for an existing NuGet with an `artifact=` tag matching the Maven artifact.
3. Add `PackageReference` with `JavaArtifact` metadata if the package provides the artifact but lacks metadata.
4. Add `ProjectReference` with `JavaArtifact` if a local binding project provides it.
5. Add `AndroidMavenLibrary` or `AndroidLibrary` with `Bind="false"` for runtime dependencies that do not need C# bindings.
6. Add `AndroidIgnoredJavaDependency` only for compile-time-only dependencies.
7. As a last resort, set `VerifyDependencies="False"` on a specific `AndroidMavenLibrary`/`AndroidLibrary` to silence XA4241/XA4242 for it, then satisfy its runtime graph yourself with the options above. Silencing the check never makes a missing runtime dependency safe.

Example:

```xml
<PackageReference Include="Xamarin.AndroidX.Core" Version="1.13.1.3" />
<PackageReference Include="Xamarin.Kotlin.StdLib" Version="2.0.21.1"
                  JavaArtifact="org.jetbrains.kotlin:kotlin-stdlib:2.0.21" />
<AndroidMavenLibrary Include="javax.inject:javax.inject" Version="1" Bind="false" />
```

## Transitive binding-NuGet version compatibility

When you satisfy the graph with existing binding NuGets (`Xamarin.Firebase.*`, `Xamarin.AndroidX.*`, `Xamarin.GooglePlayServices.*`), those NuGets have their own tightly coupled version requirements. Bumping one vendor binding NuGet often forces matching bumps of its AndroidX/support NuGets, or you get fresh XA4241/duplicate-type errors. Example: a given `Xamarin.Firebase.Crashlytics` version expects specific `Xamarin.AndroidX.DataStore` and `Xamarin.AndroidX.Lifecycle.Process` versions.

Manage this deliberately:

- Treat the set of related binding NuGets as a version *matrix*, not independent packages. Verify the whole set builds together after any bump.
- Expose the coupled versions as MSBuild properties with sensible defaults so consumers (and CI) can float them without editing the project:

```xml
<PropertyGroup>
  <XamarinFirebaseCrashlyticsVersion Condition="'$(XamarinFirebaseCrashlyticsVersion)'==''">120.0.5</XamarinFirebaseCrashlyticsVersion>
  <XamarinAndroidXDataStoreVersion Condition="'$(XamarinAndroidXDataStoreVersion)'==''">1.2.1</XamarinAndroidXDataStoreVersion>
</PropertyGroup>
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
  <PackageReference Include="Xamarin.Firebase.Crashlytics" Version="$(XamarinFirebaseCrashlyticsVersion)" />
  <PackageReference Include="Xamarin.AndroidX.DataStore" Version="$(XamarinAndroidXDataStoreVersion)" />
</ItemGroup>
```

- In CI, build against the range of vendor NuGet versions you claim to support, each paired with its known-good support-library versions, to catch incompatible combinations before shipping.

## Finding NuGet packages for Maven artifacts

Microsoft-maintained bindings (AndroidX, Kotlin, Google Play Services, Firebase) tag their NuGet
packages with the Maven coordinate they satisfy:

| Tag | Description | Example |
|-----|-------------|---------|
| `artifact` | Maven group and artifact ID | `artifact=androidx.annotation:annotation` |
| `artifact_versioned` | Maven coordinate with version | `artifact_versioned=androidx.annotation:annotation:1.9.1` |

Search by artifact tag:

```bash
dotnet package search "artifact=androidx.annotation:annotation" --source https://api.nuget.org/v3/index.json

# Or query the NuGet search API directly
curl -s "https://azuresearch-usnc.nuget.org/query?q=tags:artifact=androidx.core:core&take=10" | jq '.data[] | {id: .id, version: .version}'
```

Verify a candidate package advertises the artifact before depending on it:

```bash
nuget install Xamarin.AndroidX.Annotation -Version 1.9.1 -OutputDirectory ./packages
cat ./packages/Xamarin.AndroidX.Annotation.1.9.1/Xamarin.AndroidX.Annotation.nuspec | grep -A2 "<tags>"
```

Common Maven→NuGet mappings:

| Maven Artifact | NuGet Package | Tag Search |
|----------------|---------------|------------|
| `androidx.annotation:annotation` | `Xamarin.AndroidX.Annotation` | `artifact=androidx.annotation:annotation` |
| `androidx.core:core` | `Xamarin.AndroidX.Core` | `artifact=androidx.core:core` |
| `androidx.core:core-ktx` | `Xamarin.AndroidX.Core.Core.Ktx` | `artifact=androidx.core:core-ktx` |
| `androidx.appcompat:appcompat` | `Xamarin.AndroidX.AppCompat` | `artifact=androidx.appcompat:appcompat` |
| `androidx.fragment:fragment` | `Xamarin.AndroidX.Fragment` | `artifact=androidx.fragment:fragment` |
| `androidx.activity:activity` | `Xamarin.AndroidX.Activity` | `artifact=androidx.activity:activity` |
| `androidx.lifecycle:lifecycle-common` | `Xamarin.AndroidX.Lifecycle.Common` | `artifact=androidx.lifecycle:lifecycle-common` |
| `org.jetbrains.kotlin:kotlin-stdlib` | `Xamarin.Kotlin.StdLib` | `artifact=org.jetbrains.kotlin:kotlin-stdlib` |
| `org.jetbrains.kotlinx:kotlinx-coroutines-core` | `Xamarin.KotlinX.Coroutines.Core` | `artifact=org.jetbrains.kotlinx:kotlinx-coroutines-core` |
| `com.google.android.material:material` | `Xamarin.Google.Android.Material` | `artifact=com.google.android.material:material` |

Some packages omit the `artifact=` tag entirely. In that case, declare what they provide explicitly:

```xml
<PackageReference Include="Xamarin.Kotlin.StdLib" Version="1.9.22.1"
                  JavaArtifact="org.jetbrains.kotlin:kotlin-stdlib:1.9.22" />
```

## Gradle dependency tree

Use Gradle to learn what Android actually resolves:

```bash
./gradlew :app:dependencies --configuration releaseRuntimeClasspath
./gradlew :app:dependencyInsight --configuration releaseRuntimeClasspath --dependency kotlin-stdlib
```

Read the tree:

| Marker | Meaning |
|--------|---------|
| `->` | Version conflict resolved to another version. |
| `(*)` | Dependency subtree repeated earlier. |
| `(c)` | Constraint. |

Use resolved versions, not just versions requested in vendor docs, when mapping to NuGet packages.

## Metadata.xml

Build once, then inspect generated API:

```text
obj/Debug/net10.0-android/api.xml
```

Do not edit `api.xml`. Add transforms in `Transforms/Metadata.xml`.

Common transforms:

```xml
<metadata>
  <attr path="/api/package[@name='com.vendor.sdk']"
        name="managedName">Vendor.Sdk</attr>

  <attr path="/api/package[@name='com.vendor.sdk']/class[@name='SDK']"
        name="managedName">VendorSdk</attr>

  <attr path="/api/package[@name='com.vendor.sdk']/class[@name='SDK']/method[@name='start']/parameter[@name='p0']"
        name="name">context</attr>

  <remove-node path="/api/package[@name='com.vendor.internal']" />
</metadata>
```

Use:

| Attribute/element | Use |
|-------------------|-----|
| `managedName` | Rename packages, classes, methods, parameters. |
| `managedType` | Change parameter type. |
| `managedReturn` | Change managed return type. |
| `argsType` | Fix generated EventArgs names. |
| `eventName` | Rename or suppress event generation. |
| `remove-node` | Hide broken/internal APIs. |
| `add-node` | Add missing API nodes when needed. |

For large bindings, don't hand-write one transform per error. When the build reports the same class of failure across many nodes (dozens of invalid managed names or duplicate members), capture the error list, extract the offending Java names with a script or editor macro, and emit the `<attr ... name="managedName">` (or `<remove-node>`) lines in bulk. Re-run and repeat until clean.

### Removing obfuscated/internal classes

Vendor SDKs frequently ship obfuscated helper classes (single letters, `$`-qualified inner/anonymous
classes) that should not be bound:

```xml
<metadata>
  <!-- Remove entire internal packages -->
  <remove-node path="/api/package[starts-with(@name, 'com.example.internal')]" />

  <!-- Remove single-letter obfuscated packages -->
  <remove-node path="/api/package[@name='a']" />
  <remove-node path="/api/package[@name='b']" />

  <!-- Remove a specific internal class -->
  <remove-node path="/api/package[@name='com.example']/class[@name='InternalHelper']" />

  <!-- Remove classes with $ (often internal/anonymous inner classes) -->
  <remove-node path="/api/package/class[contains(@name, '$')]" />
</metadata>
```

If a class that looks obfuscated is actually required, mark it as not obfuscated so the generator
still produces a binding for it, and fix up its visibility if needed:

```xml
<metadata>
  <!-- Force a binding to be generated for a class that looks obfuscated -->
  <attr path="/api/package[@name='com.example']/class[@name='a']"
        name="obfuscated">false</attr>

  <!-- Promote a class/method visibility so it is bindable -->
  <attr path="/api/package[@name='com.example']/class[@name='Helper']"
        name="visibility">public</attr>
</metadata>
```

Use `managedReturn` to fix a generated return type that the binding generator inferred incorrectly:

```xml
<metadata>
  <attr path="/api/package[@name='com.example']/class[@name='MyClass']/method[@name='getObject']"
        name="managedReturn">Java.Lang.Object</attr>
</metadata>
```

## Kotlin/Java wrapper rules

For slim bindings:

```kotlin
package com.example.mybinding

object DotnetMySdk {
    @JvmStatic
    fun fetchUser(id: String, callback: FetchUserCallback) {
        NativeSdk.fetchUser(id,
            { user -> callback.onSuccess(user.name) },
            { error -> callback.onError(error.message ?: "Unknown error") })
    }
}

interface FetchUserCallback {
    fun onSuccess(name: String)
    fun onError(message: String)
}
```

Avoid exposing:

- Kotlin `suspend` functions.
- Kotlin `Flow`.
- Kotlin lambdas as public wrapper API.
- Kotlin `Result`.
- Generic-heavy nested types.

Use callback/listener interfaces and simple marshallable types.

### Wrapper patterns for init/state/callback/view APIs

These are concise shapes, not full vendor SDK ports — adapt names/types to the
real SDK rather than copying wholesale.

**Init + shared state (Java):**

```java
package com.example.mybinding;

public final class DotnetMySdk {
    private static volatile boolean initialized = false;

    private DotnetMySdk() { }

    public static synchronized void initialize(Context context, String apiKey) {
        if (initialized) {
            return;
        }
        NativeSdk.configure(context.getApplicationContext(), apiKey);
        initialized = true;
    }

    public static boolean isInitialized() {
        return initialized;
    }
}
```

**Callback interface for async native work (Java):**

```java
public interface DotnetResultCallback<T> {
    void onSuccess(T result);
    void onError(String message);
}
```

Keep the callback interface non-generic-heavy where possible (prefer a
specific callback type per operation) if the binding generator struggles with
generic interfaces; a small, explicit set of callback interfaces is usually
easier to bind than one deeply generic interface.

**View-returning wrapper (Kotlin):**

```kotlin
object DotnetMySdkViewFactory {
    @JvmStatic
    fun createBannerView(context: Context): View {
        return NativeSdkBannerView(context)
    }
}
```

Expose the native view type directly (it is already a `View`/`ViewGroup`
subclass) rather than wrapping it further, so it can be hosted from a MAUI
handler with a standard native view mapping.

**MAUI-side project reference (conditional on Android TFM):**

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-android'))">
  <ProjectReference Include="..\android\MySdk.Android.Binding\MySdk.Android.Binding.csproj" />
</ItemGroup>
```

**MAUI-side lifecycle initialization and callback consumption:**

```csharp
// MauiProgram.cs — initialize during the Android activity lifecycle rather than
// at static/module load time, since the native SDK usually needs an Android Context.
using Microsoft.Maui.LifecycleEvents;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddAndroid(android => android.OnCreate((activity, bundle) =>
            {
                DotnetMySdk.Initialize(activity.ApplicationContext, apiKey: "...");
            }));
        });
#endif

        return builder.Build();
    }
}

// Consuming the callback interface via Java.Interop: implement callback interfaces
// with a small class extending Java.Lang.Object rather than a lambda-friendly shape.
#if ANDROID
public class FetchUserCallbackImpl : Java.Lang.Object, FetchUserCallback
{
    readonly Action<string> _onSuccess;
    readonly Action<string> _onError;

    public FetchUserCallbackImpl(Action<string> onSuccess, Action<string> onError)
    {
        _onSuccess = onSuccess;
        _onError = onError;
    }

    // Marshal back to the UI thread explicitly — native callbacks can arrive on
    // arbitrary background threads.
    public void OnSuccess(string name) => MainThread.BeginInvokeOnMainThread(() => _onSuccess(name));
    public void OnError(string message) => MainThread.BeginInvokeOnMainThread(() => _onError(message));
}

var callback = new FetchUserCallbackImpl(
    onSuccess: name => { /* update UI on main thread */ },
    onError: message => { /* surface error */ });

DotnetMySdk.FetchUser("user-id", callback);
#endif
```

Implement callback interfaces with a small private class rather than a
lambda-friendly shape, and marshal back to the UI thread explicitly if the
callback can update UI, matching the wrapper-rules guidance to avoid relying
on Kotlin/Java threading assumptions inside C#.

**Registering and cleaning up an event listener from a page:**

```csharp
#if ANDROID
public class MyEventListener : Java.Lang.Object, DotnetMySdk.IEventListener
{
    readonly Action<string, string> _onEvent;

    public MyEventListener(Action<string, string> onEvent) => _onEvent = onEvent;

    public void OnEvent(string eventType, string eventData) =>
        MainThread.BeginInvokeOnMainThread(() => _onEvent(eventType, eventData));
}
#endif

protected override void OnAppearing()
{
    base.OnAppearing();
#if ANDROID
    DotnetMySdk.SetEventListener(new MyEventListener((type, data) =>
    {
        StatusLabel.Text = $"Event: {type} - {data}";
    }));
#endif
}

// Always remove the listener when the page disappears — native SDKs commonly
// hold a strong reference to the listener object and will leak the page/Activity
// (and its Java.Lang.Object peer) if it is never unregistered.
protected override void OnDisappearing()
{
    base.OnDisappearing();
#if ANDROID
    DotnetMySdk.RemoveEventListener();
#endif
}
```

### Complex data: JSON-payload slim binding strategy

For Android SDKs that expose large or nested Java/Kotlin object graphs, avoid
binding the full model tree when the app only needs stable data snapshots:

- Have the Java/Kotlin wrapper serialize the native result to a JSON `String`
  and expose success/error through a simple callback interface.
- Deserialize the JSON in C# into shared .NET DTOs owned by the binding/app.
- When the same SDK exists on Apple, keep the Android JSON schema aligned with
  the Apple wrapper's JSON schema so the shared C# model layer remains
  cross-platform.
- During upstream SDK updates, test deserialization against real payloads from
  the new native SDK version; schema drift is a runtime contract break, not a
  binding-generator compile error.

Use this for complex or frequently-changing object graphs, not for simple
primitive/string results where direct wrapper methods are clearer.

## Slim binding update and versioning

For an Android NLI/slim wrapper, updates are usually smaller than a full
binding update, but still need the same dependency-graph re-check:

1. Update the vendor dependency version in the wrapper Gradle module (Gradle
   version catalog entry or direct `implementation("com.vendor:sdk:X.Y.Z")`).
2. Re-run `./gradlew :app:dependencies --configuration releaseRuntimeClasspath`
   for the wrapper module and compare the resolved graph against the previous
   version's report.
3. Compare the vendor's Java/Kotlin API surface against the thin wrapper; only
   update wrapper method signatures if the wrapper actually used the changed
   members.
4. Rebuild the wrapper AAR.
5. Update the bound `AndroidLibrary`/`AndroidMavenLibrary` reference (and any
   `PackageReference` NuGet dependency versions) to match the new wrapper/AAR.
6. Fix any new `XA4241`/`XA4242` errors using the same decision order as
   initial binding.
7. Update `Transforms/Metadata.xml` only for wrapper members that changed
   shape.
8. Re-test from a MAUI sample app on device/emulator before shipping the
   update.

## Native `.so` libraries

If an AAR contains `jni/<abi>/*.so`, verify all ABIs needed by the app:

```bash
unzip -l vendor.aar | grep '^.*jni/.*\\.so$'
```

For direct `.so` assets:

```xml
<AndroidNativeLibrary Include="libs/arm64-v8a/libvendor.so" />
```

Runtime `UnsatisfiedLinkError` usually means the `.so` is missing, wrong ABI, not packaged, or loaded in the wrong order.
