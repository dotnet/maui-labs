---
name: android-slim-bindings
description: >-
  Create and update slim/native platform interop bindings for Android in .NET MAUI and .NET for Android projects.
  Guides through creating Java/Kotlin wrappers, configuring Gradle projects, resolving Maven dependencies,
  generating C# bindings, and integrating native Android libraries using the Native Library Interop (NLI) approach.
  USE FOR: Android bindings, AAR/JAR integration, Kotlin interop, Maven dependencies, bridging native Android SDKs to .NET.
  DO NOT USE FOR: iOS/macOS bindings (use ios-slim-bindings), general MAUI app development, NuGet package issues.
---

# When to use this skill

Activate this skill when the user asks:
- How do I create Android bindings for a native library?
- How do I wrap an Android SDK for use in .NET MAUI?
- How do I create slim bindings for Android?
- How do I use Native Library Interop for Android?
- How do I bind a Kotlin library to .NET?
- How do I bind a Java library to .NET?
- How do I integrate an AAR or JAR into .NET MAUI?
- How do I create a Java/Kotlin wrapper for a native Android library?
- How do I update Android bindings when the native SDK changes?
- How do I fix Android binding build errors?
- How do I expose native Android APIs to C#?
- How do I resolve Maven dependencies for Android bindings?
- How do I handle AndroidX dependencies in bindings?
- How do I fix "Java dependency is not satisfied" errors?
- How do I use AndroidMavenLibrary in my binding project?

# Overview

This skill guides the creation of **Native Library Interop (Slim Bindings)** for Android. This modern approach creates a thin native Java/Kotlin wrapper exposing only the APIs you need from a native Android library, making bindings easier to create and maintain.

## When to Use Slim Bindings vs Traditional Bindings

| Scenario | Recommended Approach |
|----------|---------------------|
| Need only a subset of library functionality | **Slim Bindings** |
| Easier maintenance when SDK updates | **Slim Bindings** |
| Prefer working in Java/Kotlin for wrapper | **Slim Bindings** |
| Better isolation from breaking changes | **Slim Bindings** |
| Complex libraries with many dependencies | **Slim Bindings** |
| Need entire library API surface | Traditional Bindings |
| Creating bindings for third-party developers | Traditional Bindings |
| Already maintaining traditional bindings | Traditional Bindings |

# Inputs

| Parameter | Required | Example | Notes |
|-----------|----------|---------|-------|
| libraryName | yes | `FirebaseMessaging`, `OkHttp` | Name of the native Android library to bind |
| bindingProjectName | yes | `MyBinding.Android` | Name for the C# binding project |
| dependencySource | no | `maven`, `aar`, `jar` | How the native library is distributed |
| targetFrameworks | no | `net10.0-android` | Target frameworks (default: latest .NET Android) |
| exposedApis | no | List of specific APIs | Which native APIs to expose (helps scope the wrapper) |
| mavenCoordinates | no | `com.example:library:1.0.0` | Maven coordinates if library is from Maven repository |

# Project Structure

```
MyBinding/
 android/
 native/                              # Android Studio/Gradle project   
 app/      
 src/main/         
 java/com/example/mybinding/            
 DotnetMyBinding.java  # Java wrapper implementation                
 kotlin/com/example/mybinding/            
 DotnetMyBinding.kt    # Or Kotlin wrapper                
 build.gradle.kts         
 settings.gradle.kts      
 build.gradle.kts      
 MyBinding.Android.Binding/   
 MyBinding.Android.Binding.csproj       
 Transforms/       
 Metadata.xml           
 sample/
 MauiSample/                          # Sample MAUI app   
 MauiSample.csproj       
 MainPage.xaml.cs       
 README.md
```

# Step-by-step Process

> **For detailed code examples and full implementation walkthroughs, see [references/android-slim-bindings-implementation.md](references/android-slim-bindings-implementation.md).**

## Step 1: Analyze Dependencies First

Before creating any bindings, analyze the complete dependency tree of your target library. This is the most critical step and where most binding projects fail.

**Prerequisites:** JDK 17+, Android SDK with `ANDROID_HOME` set, .NET SDK 9+. You don't need Gradle installed globally; the Gradle Wrapper downloads the correct version automatically.

## Step 2: Create the Android Library Project

Choose one of these approaches:

- **Option A: Android Studio** (recommended for GUI-based projects: create a "No Activity" project, then add an Android Library module)
- **Option B: `gradle init` with Wrapper** (recommended for CLI-based projects: scaffold with Gradle using the Wrapper, then convert to an Android library)
- **Option C: CommunityToolkit NativeLibraryInterop template** (clone and use the CommunityToolkit NativeLibraryInterop template as your starting point)
- **Option D: Manual creation with Gradle Wrapper** (create the project structure manually using the Gradle Wrapper)

After project creation, configure `settings.gradle.kts`, root `build.gradle.kts`, and `app/build.gradle.kts` with your library dependency.

## Step 3: Analyze the Full Dependency Tree

```bash
./gradlew app:dependencies --configuration releaseRuntimeClasspath
```

 resolution strategy.

**Understanding the output:** `->` = version conflict resolution, `(*)` = deduplication, `(c)` = constraint.

## Step 4: Create the Wrapper Class

Create a Java or Kotlin wrapper in `app/src/main/java/` (or `kotlin/`) that exposes only the APIs you need. Key guidelines:

- Use simple, marshallable types (String, boolean, int, byte[], Context, View)
- Use callback interfaces for async operations (not Kotlin coroutines or lambdas)
- Prefix class names with `Dotnet` for clarity
- Add `@JvmStatic` on all Kotlin methods for static access from C#
- Use `object` in Kotlin for singleton pattern

## Step 5: Build the AAR

```bash
./gradlew :app:assembleRelease
# Output: app/build/outputs/aar/app-release.aar
```

## Step 6: Create the C# Binding Project

```bash
dotnet new androidbinding -n MyBinding.Android.Binding
```

Key `.csproj` elements:
- `< reference the built AARAndroidLibrary>` 
- `< NuGet packages for transitive dependenciesPackageReference>` 
- `<AndroidMavenLibrary Bind=" Maven deps without NuGet equivalentfalse">` 
- `< compile-time only dependenciesAndroidIgnoredJavaDependency>` 

Configure `Transforms/Metadata.xml` for namespace renaming and parameter names.

## Step 7: Resolve Dependencies

Build and resolve XA4241/XA4242 errors using this decision tree:

```
For each unsatisfied dependency:

 Is there a XA4242 suggestion?
 Install the suggested NuGet package

 Is the dependency needed from C#?
 Find/create a binding NuGet or create your own binding
 Use AndroidMavenLibrary with Bind="false"

 Is it a compile-time only dependency (annotations, processors)?
 Use AndroidIgnoredJavaDependency

 Create a separate binding project or include AAR/JAR directly
```

### Common Maven-to-NuGet Mappings

| Maven Artifact | NuGet Package |
|----------------|---------------|
| `androidx.annotation:annotation` | `Xamarin.AndroidX.Annotation` |
| `androidx.core:core` | `Xamarin.AndroidX.Core` |
| `androidx.core:core-ktx` | `Xamarin.AndroidX.Core.Core.Ktx` |
| `androidx.appcompat:appcompat` | `Xamarin.AndroidX.AppCompat` |
| `androidx.fragment:fragment` | `Xamarin.AndroidX.Fragment` |
| `androidx.activity:activity` | `Xamarin.AndroidX.Activity` |
| `androidx.recyclerview:recyclerview` | `Xamarin.AndroidX.RecyclerView` |
| `androidx.lifecycle:lifecycle-common` | `Xamarin.AndroidX.Lifecycle.Common` |
| `org.jetbrains.kotlin:kotlin-stdlib` | `Xamarin.Kotlin.StdLib` |
| `org.jetbrains.kotlinx:kotlinx-coroutines-core` | `Xamarin.KotlinX.Coroutines.Core` |
| `com.google.android.material:material` | `Xamarin.Google.Android.Material` |
| `com.squareup.okhttp3:okhttp` | `Square.OkHttp3` |
| `com.google.code.gson:gson` | `GoogleGson` |

### Finding NuGet Packages

```bash
dotnet package search "artifact=androidx.core:core" --source https://api.nuget.org/v3/index.json
```

## Step 8: Customize Bindings with Metadata

Edit `Transforms/Metadata.xml` to rename packages, classes, parameters, and remove internal types. Examine `obj/Debug/net10.0-android/api.xml` after building to construct correct XPath expressions.

## Step 9: Build and Verify

```bash
dotnet build -c Release
```

## Step 10: Use in Your MAUI App

Add a conditional `<ProjectReference>`, initialize in `MauiProgram.cs` using `ConfigureLifecycleEvents`, and implement callback interfaces by extending `Java.Lang.Object`.

# Updating Bindings When Native SDK Changes

1. Update native dependency version in `app/build.gradle.kts`
2. Re-analyze dependencies with `./gradlew app:dependencies`
3. Update the Java/Kotlin wrapper if APIs changed
4. Rebuild the AAR with `./gradlew :app:assembleRelease`
5. Update NuGet packages and Maven dependencies in binding `.csproj`
6. Rebuild C# binding and fix any new XA4241/XA4242 errors
7. Update `Metadata.xml` for new classes/methods
8. Test with sample app

> For detailed update instructions, see [references/android-slim-bindings-implementation.md](references/android-slim-bindings-implementation.md).

# Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| XA4241 / XA4242 | Missing transitive dependency | Use NuGet, `AndroidMavenLibrary Bind="false"`, or `AndroidIgnoredJavaDependency` |
| "Type is defined multiple times" | Duplicate dependency | Check NuGet/AAR overlap, use `Bind="false"` |
| "Class does not implement interface member" | Covariant return types | Add custom implementation in `Additions/` folder |
| "Cannot find symbol" | Missing Gradle dependency | Verify `build.gradle.kts` dependencies, rebuild AAR |
| `NoClassDefFoundError` (runtime) | Dependency ignored incorrectly | Remove from `AndroidIgnoredJavaDependency`, satisfy properly |
| `UnsatisfiedLinkError` (runtime) | Missing .so native library | Include via `<AndroidNativeLibrary>`, check ABI compatibility |
| Callback not invoked | Wrong thread / GC'd reference | Use `MainThread.BeginInvokeOnMainThread()`, hold strong reference |

> For detailed troubleshooting with code examples, see [references/android-slim-bindings-implementation.md](references/android-slim-bindings-implementation.md).

# Quick Reference

## Type Mapping

| Java/Kotlin Type | C# Type | Notes |
|------------------|---------|-------|
| `String` | `string` | Direct mapping |
| `boolean` / `Boolean` | `bool` | Direct mapping |
| `int` / `Integer` | `int` | Direct mapping |
| `long` / `Long` | `long` | Direct mapping |
| `double` / `Double` | `double` | Direct mapping |
| `float` / `Float` | `float` | Direct mapping |
| `byte[]` | `byte[]` | Direct mapping |
| `Context` | `Android.Content.Context` | Pass from platform |
| `View` | `Android.Views.View` | For view creation |
| `JSONObject` | `Org.Json.JSONObject` | For complex data |
| Callback interface | `IJavaObject` implementation | See callback pattern |

## Things to Avoid in Wrapper API

| Avoid | Use Instead |
|-------|-------------|
| Kotlin coroutines | Callback interfaces |
| Kotlin lambdas | Java-style interfaces |
| Kotlin data classes | Simple classes with getters |
| Kotlin sealed classes | Enums or class hierarchy |
| Generics (complex) | Specific types or Object |
| Rx/Flow observables | Callback interfaces |

## MSBuild Items Reference

| Item | Purpose |
|------|---------|
| `<AndroidLibrary>` | Include AAR/JAR in binding |
| `<AndroidMavenLibrary>` | Download and include from Maven |
| `<AndroidIgnoredJavaDependency>` | Ignore a dependency (compile-time only) |
| `<AndroidNativeLibrary>` | Include .so files |
| `<TransformFile>` | Metadata transform files |
| `<PackageReference>` | NuGet package reference |

## Common Attributes

| Attribute | Values | Purpose |
|-----------|--------|---------|
| `Bind` | `true`/`false` | Whether to generate C# bindings |
| `Pack` | `true`/`false` | Whether to include in NuGet package |
| `JavaArtifact` | `groupId:artifactId` | Declare what Maven artifact this satisfies |
| `JavaVersion` | `version` | Version of the Maven artifact |
| `Repository` | `Central`/`Google`/URL | Maven repository to download from |

# Resources

## Official Documentation
- [Binding a Java Library (Microsoft Docs)](https://learn.microsoft.com/dotnet/android/binding-libs/binding-java-libs/binding-java-library)
- [Binding from Maven (Microsoft Docs)](https://learn.microsoft.com/dotnet/android/binding-libs/binding-java-libs/binding-java-maven-library)
- [Java Dependency Verification](https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/java-dependency-verification)
- [Resolving Java Dependencies](https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/resolving-java-dependencies)
- [Java Bindings Metadata](https://learn.microsoft.com/dotnet/android/binding-libs/customizing-bindings/java-bindings-metadata)
- [Native Library Interop - .NET Community Toolkit](https://learn.microsoft.com/dotnet/communitytoolkit/maui/native-library-interop)

## Version Discovery
- [Android Gradle Plugin Releases](https://developer.android.com/build/releases/gradle-plugin)
- [Gradle Releases](https://gradle.org/releases/)
- [Kotlin Releases](https://kotlinlang.org/docs/releases.html)

## Templates and Examples
- [Maui.NativeLibraryInterop Repository](https://github.com/CommunityToolkit/Maui.NativeLibraryInterop)
- [Xamarin.AndroidX Repository](https://github.com/xamarin/AndroidX)

## Reference Files
- [android-bindings-guide.md](references/android-bindings-guide.md) — comprehensive binding reference
- [android-slim-bindings-implementation.md](references/android-slim-bindings-implementation.md) — detailed code examples, troubleshooting, and complete scripts
- For iOS bindings, use the **ios-slim-bindings** skill

# Output Format

When assisting with Android slim bindings, provide:

1. **Dependency analysis** - Full dependency tree and NuGet mapping strategy
2. **Project structure** - File/folder layout for the binding
3. **Wrapper code** - Complete Java or Kotlin implementation
4. **Gradle configuration** - build.gradle.kts with dependencies
5. **C# binding project** - `.csproj` with proper dependency references
6. **Metadata transforms** - Metadata.xml for namespace and parameter renaming
7. **Usage examples** - How to call the binding from MAUI/C#
8. **Troubleshooting guidance** - Common issues and solutions for the specific library

Always verify:
- All transitive dependencies are accounted for
- NuGet packages match required Maven versions
- Wrapper methods use simple, marshallable types
- Callback interfaces are properly designed
- Metadata.xml renames parameters to meaningful names
