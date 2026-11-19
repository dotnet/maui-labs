---
name: android-java-bindings
description: >-
  Write and repair C# bindings for Java/Kotlin Android libraries with .NET for
  Android. USE FOR: direct/full AAR or JAR bindings, AndroidMavenLibrary,
  Metadata.xml, Additions, missing generated APIs, Kotlin JVM signatures,
  binding-generator warnings, CS0535/CS0738/CS0534, duplicate members, EventArgs,
  listener Handler fields or events,
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

## Choose the entry point

- **New binding:** follow the workflow from artifact inspection.
- **An existing binding error:** start with the failing stage and repair recipes
  below. Do not scaffold a project or run Gradle unless dependency evidence calls
  for it.
- **A question with supplied signatures/errors:** give the concrete transform or
  addition and explain its limits using those facts. Do not search an empty
  workspace for hypothetical artifacts. Ask for missing declarations only when
  they affect the repair.

The core decisions and recipes are included here so routine troubleshooting does
not depend on extra file reads. The references at the end provide deeper examples
when needed; load only the relevant one, relative to this skill's base directory.
If a reference cannot be read, state that limitation and use the guidance below
rather than guessing metadata attributes or repeatedly trying unrelated paths.

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
Even dependency-report tasks execute Gradle build/plugin code. Before running an
unfamiliar native project, establish trust in its wrapper and build files or use
an isolated environment without developer/CI credentials. Verify the wrapper JAR
and distribution provenance separately: `distributionSha256Sum` checks the
downloaded Gradle distribution, not the wrapper JAR or project scripts. See
[Gradle wrapper verification](https://docs.gradle.org/current/userguide/gradle_wrapper.html#sec:verification).

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
is not necessarily its JVM name.

For Kotlin, select the JVM/Android artifact, not a metadata-only multiplatform
artifact. Kotlin metadata can intentionally hide `internal` declarations even
when `javap` shows them as JVM-public. A node absent from class-parse cannot be
restored by a `visibility` transform on that nonexistent node; prefer a supported
public API. `suspend` uses a continuation ABI, not an automatic C# `Task`, and
`Flow` is not automatically `IAsyncEnumerable`. Prefer an existing Java-friendly
callback API or, if the user owns a wrapper, expose callbacks with cancellation,
errors, lifetime, and threading handled explicitly. This is an ergonomics issue,
not proof that all coroutine JVM types are unbindable.

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

Locate the first artifact where a required API disappears:

| Last place the API exists | Next stage to investigate |
|--------------------------|---------------------------|
| Input JAR / AAR `classes.jar` only | Class parsing, native visibility, Kotlin metadata |
| `api.xml.class-parse`, but not `api.xml` | Java type resolution; inspect `java-resolution-report.log` |
| `api.xml`, but not `api.xml.fixed` | User transforms, especially overly broad `remove-node` |
| `api.xml.fixed`, but not `generated\src` | Generator warnings, C# collisions, unsupported type mappings |

Use actual intermediate paths from the build; they may be redirected or include a
platform version in the TFM. Trace unresolved base/interface/signature types to
the root cause. For example, if several base classes ultimately require
`androidx.fragment.app.FragmentActivity`, verify a compatible
`Xamarin.AndroidX.Fragment` binding reference rather than inventing the missing
classes in C#. A runtime-only JAR is insufficient if managed base types are needed.
Binding tools can omit APIs with warnings and still build: check required coverage.

### 5. Make the smallest source-level repair

Inspect `generated\src` and the XPath comments beside failing declarations, plus
the generated base/interface contract. Compare native signatures and managed
signatures before selecting a repair. For questions, use the declarations supplied
by the user and mark any missing contract details instead of fabricating them.

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

#### Pick metadata by the generated symbol that collides

These attributes are not interchangeable:

| Generated failure | Targeted repair |
|-------------------|-----------------|
| Duplicate `EventArgs` classes/constructors (CS0111/CS0102) | Distinct `argsType` values on the listener methods |
| Duplicate `...Handler` fields in a listener implementor | Distinct `managedName` on the overloaded listener method |
| Duplicate C# events from add/set listener registration | Distinct `eventName`, or empty `eventName` on only the redundant projection |
| Ordinary C# method/type name collision | Distinct `managedName` on the conflicting native nodes |
| CS0115 / incorrect virtual or override | Verify the base contract, then targeted `managedOverride` (`virtual`, `override`, or supported `none`) |

For duplicate EventArgs, an illustrative listener method transform is:

```xml
<attr path="/api/package[@name='com.example']/interface[@name='ConnectionListener']/method[@name='onError' and count(parameter)=1 and parameter[1][@type='int']]" name="argsType">ConnectionErrorEventArgs</attr>
```

Apply a distinct `argsType` to each colliding listener's method. Do not substitute
`managedName`, `eventName`, or an invented `managedEventName`: naming an event or
callback is not the same operation as naming its EventArgs type.

For Handler collisions, rename only the affected overload using `managedName`.
Select it by parameter count and types, not just the shared method name. Keep both
callbacks and their original JNI identities. Handler field names derive from the
managed callback names; changing only `argsType` does not rename those fields.
Leave a nonconflicting overload unchanged rather than adding a redundant rename.
After regeneration, check that both callbacks remain callable and the implementor
has distinct Handler fields.

#### Methods that collapse to the same C# signature

Java source does not permit overloads differing only by return type; JVM
descriptors include returns, and Kotlin/bridge/erased-generic shapes can still
collide when projected to C#. Keep required methods by giving them distinct C#
names with `managedName` in `Transforms\Metadata.xml`.

For example, distinguish `get` overloads by their preserved generic parameter:

```xml
<attr path="/api/package[@name='com.example']/class[@name='Store']/method[@name='get' and count(parameter)=1 and parameter[1][@type='com.example.Key&lt;java.lang.Boolean&gt;']]" name="managedName">GetBoolean</attr>
<attr path="/api/package[@name='com.example']/class[@name='Store']/method[@name='get' and count(parameter)=1 and parameter[1][@type='com.example.Key&lt;java.lang.Long&gt;']]" name="managedName">GetLong</attr>
```

Use the actual generic strings from `api.xml`, or the actual return attributes
when needed to disambiguate bytecode methods. Do not rewrite native names/returns,
remove a required overload, or propose changing third-party Kotlin as the only
solution to a managed naming collision.

#### Interface and base contracts

For CS0535, an erased interface parameter may differ from a typed generated
overload. Add a forwarding overload matching the interface in `Additions`, with a
conversion justified by the native generic contract. Do not use no-op callbacks.

For CS0738, preserve a useful typed public API with an explicit interface
implementation in `Additions`. For example, if generated `Record` derives from
`Java.Lang.Object`, implements vendor `IRecordFactory`, and exposes `Record Copy()`
while that interface declares `Java.Lang.Object Copy()`, add:

```csharp
namespace Example.Binding;

public partial class Record
{
    Java.Lang.Object IRecordFactory.Copy() => Copy();
}
```

Use the real vendor interface without redeclaring it. `Java.Lang.ICloneable` is
a marker interface and does not declare `Clone()`. Modern C# supports covariance
for class **overrides**, but that does not make a narrower method return satisfy
an ordinary interface implementation contract.

For properties, inspect the existing collection types before writing an explicit
interface property. A nongeneric `IList` need not implement `IList<string>`.
Forward real data through a verified projection/conversion, preserving nulls,
identity, mutability, and live-view semantics as required. Do not blindly cast or
return an empty collection. If preserving the existing public property is a
requirement, prefer this adapter over changing that property's declared type.

`managedReturn` changes a managed return contract, not the Java `return`/JNI
signature. Check the generated API and marshalling before using it as an alternative.
A BG0000 exception from a malformed generic type is not evidence that generics are
universally unsupported. In particular, `System.Collections.IList&gt;System.String&lt;`
is **valid XML containing an invalid type name**: the delimiters are reversed and
the generic namespace is missing. The correctly spelled XML text is
`System.Collections.Generic.IList&lt;System.String&gt;`. That spelling alone does not
prove support in the installed generator. Rebuild and inspect; if it still fails
or would change a public contract, consider the verified explicit interface
property above and capture a minimal generator repro.

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

For details beyond the core recipes, consult the relevant bundled reference:

- [Diagnostic pipeline and runtime failures](references/troubleshooting.md)
- [Metadata selectors, collection adapters, and additional recipes](references/metadata-recipes.md)
- [Kotlin properties, default arguments, and JVM shapes](references/kotlin.md)

This skill synthesizes these guides with current .NET guidance rather than copying
historical samples verbatim:

- [Android libraries development tips: troubleshooting](https://github.com/dotnet/android-libraries/blob/main/docs/development-tips.md#troubleshooting)
- [Java interop troubleshooting index](https://github.com/dotnet/java-interop/wiki/Troubleshooting-Android-Bindings-Issues)
- [Binding a Maven library](https://learn.microsoft.com/dotnet/android/binding-libs/binding-java-libs/binding-java-maven-library)
- [Resolving Java dependencies](https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/resolving-java-dependencies)
- [Java bindings metadata](https://learn.microsoft.com/dotnet/android/binding-libs/customizing-bindings/java-bindings-metadata)
