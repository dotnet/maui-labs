---
name: android-java-bindings
description: >-
  Write and repair C# bindings for Java/Kotlin Android libraries with .NET for
  Android. USE FOR: direct/full AAR or JAR bindings, AndroidMavenLibrary,
  Metadata.xml, Additions, missing generated APIs, Kotlin JVM signatures,
  binding-generator warnings, CS0535/CS0738/CS0534, duplicate members or events,
  and binding dependency errors XA4241/XA4242. DO NOT USE FOR: XAML data binding,
  AI tool bindings, iOS/Swift bindings, desktop JVM hosting, or designing a
  Java/Kotlin slim wrapper (use android-slim-bindings).
---

# Java and Kotlin bindings for C#

## Purpose and scope

Create maintainable .NET for Android binding libraries from existing Java/Kotlin
artifacts. Preserve the native contract and the requested C# API, not merely a
successful build. These bindings can be consumed by .NET Android or the Android
target of a MAUI app; they do not make an Android SDK cross-platform.

Use this workflow for direct bindings and generated-code troubleshooting. If the
user only needs a small facade around a complex SDK, explain the tradeoff and use
`android-slim-bindings` for wrapper construction. Do not silently replace an
existing full binding with a smaller wrapper or remove required APIs.

## Inputs

Determine from the workspace, artifacts, and user:

- Exact Maven coordinates/version/repository, or local AAR/JAR and its provenance.
- Required types, methods, callbacks, and whether an existing public API must stay
  compatible. Distinguish required coverage from optional APIs.
- Binding project, SDK/workload/TFM, configuration, and supported Android versions.
- For repairs: first error, generator warnings, diagnostic build log, metadata,
  additions, and generated declarations on both sides of the failing contract.
- For Kotlin: JVM/Android artifact variant, Kotlin version, and source if available.

Ask for missing artifacts or API requirements rather than inventing signatures.
Preserve the repository's SDK pins, package management, feeds, and conventions.

## Workflow

### 1. Inspect the actual library

Check the POM and, if available, the native project's resolved dependency graph.
For a Gradle Android library on Windows:

```powershell
.\gradlew.bat :library:dependencies --configuration releaseRuntimeClasspath
```

Replace the module/configuration with those in the project. On Unix use `./gradlew`.
Inspect the archive with `jar tf`; an AAR normally contains `classes.jar`, may
contain embedded JARs and native libraries, and generally does not bundle all Maven
dependencies. Extract to a dedicated scratch directory when needed.

Use source/decompilation and `javap -classpath classes.jar -p -s com.example.Widget`
to verify visibility, inheritance, and JVM descriptors. A source-level method name
is not necessarily its JVM name. Read [Kotlin and JVM shapes](references/kotlin.md)
for Kotlin libraries before choosing transforms.

### 2. Create or preserve the binding project

For a new project use the installed Android binding template:

```powershell
dotnet new android-bindinglib -n Example.Binding
```

Check `dotnet new list` if the template is missing. Match the consumer's supported
Android TFM; do not upgrade the repository merely to use this skill.

For .NET 9+ Maven input, this is a concrete example (choose the user's artifact,
not this example, for their project):

```xml
<ItemGroup>
  <AndroidMavenLibrary Include="de.hdodenhof:circleimageview" Version="3.1.0" />
</ItemGroup>
```

For local AAR/JAR input use `AndroidLibrary`. Modern projects implicitly include
local archives: use `Update` to change metadata on an already included file and
`Include` for an input not already included. Inspect evaluated items to avoid
including the same archive twice. Prefer current items over legacy Xamarin
`EmbeddedJar`, `InputJar`, or `LibraryProjectZip`.

`Bind="false"` suppresses C# generation, not Java packaging. `Pack="false"` is a
separate packaging choice. Neither flag is a general fix for duplicate Java
classes. Do not include both a binding NuGet and the same native artifact.

### 3. Satisfy dependencies deliberately

For XA4241/XA4242 or missing referenced Java types:

1. Prefer a compatible existing binding NuGet, especially a package suggested by
   XA4242. Check its native artifact version; NuGet and Maven versions can differ.
2. If a dependency appears in required public signatures, base classes, or
   interfaces, provide its managed types via a binding package/project or bind it.
3. If only Java code needs it at runtime, include the artifact with
   `AndroidMavenLibrary` or `AndroidLibrary`, normally `Bind="false"`.
4. Use `AndroidIgnoredJavaDependency` only with evidence the dependency is
   compile-time-only or is otherwise supplied. It does not supply runtime classes.

`AndroidMavenLibrary` downloads the named artifact and verifies dependencies; do
not assume it downloads the entire transitive graph. Account for each dependency.
When a reference lacks artifact metadata, `JavaArtifact` takes
`groupId:artifactId:version`, including the version, on a package/project/library
reference. Follow Central Package Management where present.

For redistributable bindings, prefer separate packages/references for shared
native dependencies so NuGet can resolve them instead of bundling duplicates.

### 4. Capture a diagnostic rebuild and locate the failing stage

For example, substituting the actual project and TFM:

```powershell
dotnet build .\Example.Binding\Example.Binding.csproj -t:Rebuild -c Debug -f net10.0-android -v:diag -bl:binding.binlog *> binding-diagnostic.log
```

Check the exit code and read the first causal error. Use `binlogtool` to inspect
`.binlog` files; consult its installed help for supported commands. Do not try to
read a binary log as text. Logs can contain local paths and sensitive build data;
keep them local unless sharing is authorized.

Follow [the diagnostic pipeline](references/troubleshooting.md) for missing APIs.
Use the actual intermediate paths from the build; they may be redirected or
include a platform version in the TFM.

### 5. Make the smallest source-level repair

Inspect `generated\src` and the XPath comments beside failing declarations, plus
the generated base/interface contract. Compare native signatures and managed
signatures before selecting a fix from
[metadata and additions recipes](references/metadata-recipes.md).

- Persist transforms in `Transforms\Metadata.xml` and hand-written partial types
  in `Additions`. Never fix generated `.cs` or intermediate `api.xml` in place.
- Use precise XPath selectors from the real API: package, type, method, parameter
  count/types, and return type when needed to distinguish bytecode methods.
- Verify each XPath matches the intended node count, and inspect regeneration.
  A transform matching zero nodes is not a repair.
- Rename C# types/methods with `managedName`, not native `name`. Preserve JNI
  identities; metadata changes cannot invent missing Java implementations.
- Do not hide errors with empty stubs, default returns, unchecked casts, broad
  removals, or global warning suppression.

### 6. Validate required behavior

Rebuild the affected binding and compile a consumer exercising the requested API.
Confirm no required type/member disappeared and no metadata selector stopped
matching. Repeat for supported configurations/TFMs affected by the change.

Run an Android consumer smoke test, including relevant callback/interface
dispatch, generic arguments/results, null cases, and native dependencies. Include
Release with the consumer's trimming settings; a binding-only build does not prove
the APK contains everything or that JNI dispatch works.

For distributable packages, pack and inspect contents/dependencies, then consume
the package from a sample rather than relying only on a project reference.
Document intentional public API changes. If artifacts, tools, or a device are
unavailable, report exactly which validation remains blocked.

## Handoff

Report the diagnosed stage and evidence, persistent files changed, retained API
coverage, and concrete validation results/limitations. Distinguish a compiled
binding from a runtime-verified binding. For design-only requests, provide the
project items, targeted transforms/additions, and consumer usage without claiming
they were executed.

## Sources

The references synthesize these guides with current .NET guidance rather than
copying historical samples verbatim:

- [Android libraries development tips: troubleshooting](https://github.com/dotnet/android-libraries/blob/main/docs/development-tips.md#troubleshooting)
- [Java interop troubleshooting index](https://github.com/dotnet/java-interop/wiki/Troubleshooting-Android-Bindings-Issues)
- [Binding a Maven library](https://learn.microsoft.com/dotnet/android/binding-libs/binding-java-libs/binding-java-maven-library)
- [Resolving Java dependencies](https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/resolving-java-dependencies)
- [Java bindings metadata](https://learn.microsoft.com/dotnet/android/binding-libs/customizing-bindings/java-bindings-metadata)
