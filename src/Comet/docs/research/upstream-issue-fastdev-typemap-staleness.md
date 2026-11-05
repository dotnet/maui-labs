# DRAFT upstream issue for dotnet/android — fast-dev APK skips libxamarin-app.so changes → "no Java peer type found"

Status: draft, not yet filed. Verified against Microsoft.Android.Sdk.Darwin
36.99.0-preview.5.308 (.NET 11.0.100-preview.5). Repo workaround lives in
`src/Comet/Directory.Build.targets` (`_CometFixFastDevApkInputs`) — remove it when
the SDK fix ships.

## Title

Incremental Debug build: `_BuildApkFastDevStaticInputs` omits `@(_ApplicationSharedLibrary)`, deploying stale MVID-keyed typemaps → `NotSupportedException: no Java peer type found`

## Description

**Repro.** App project → library project containing `internal` `Java.Lang.Object`
subclasses (e.g. a Compose binding facade full of internal bridge lambdas).
Debug build with fast deployment (`EmbedAssembliesIntoApk=false`,
`AndroidPackageFormat=apk`):

1. Clean build, `-t:Run` — app runs.
2. Make an **implementation-only** edit in the library (add a private field to an
   internal JLO subclass; anything that does not change the ref assembly).
3. Incremental `dotnet build -t:Run`.
4. App crashes at startup:

```
W monodroid-assembly: typemap: unable to look up assembly name for type 'AndroidX.Compose.ComposableLambda2', trying without it.
E AndroidRuntime: FATAL EXCEPTION: main
E AndroidRuntime: android.runtime.JavaProxyThrowable: [System.NotSupportedException]: Cannot create instance of type 'AndroidX.Compose.ComposableLambda2': no Java peer type found.
```

**Cause (from the binlog).** On step 3 the build does the right work almost
everywhere:

- `_GenerateJavaStubs` re-runs (JCWs fine),
- `_CompileNativeAssemblySources` re-runs and `_CreateApplicationSharedLibraries`
  relinks `app_shared_libraries/<abi>/libxamarin-app.so` with the new MVID-keyed
  debug typemaps,

but `_BuildApkFastDev` is **skipped** ("all output files are up-to-date"), because
`_BuildApkFastDevStaticInputs` does not list the application shared library:

```xml
<_BuildApkFastDevStaticInputs>
    @(_AndroidMSBuildAllProjects)
    ;@(_ShrunkFrameworkAssemblies)
    ;@(_AndroidNativeLibraryForFastDev)
    ;@(_DexFileForFastDevInput)
    ;$(_AndroidBuildPropertiesCache)
    ;$(_AdbPropertiesCache)
    ;$(_PackagedResources)
</_BuildApkFastDevStaticInputs>
```

The embed-path property right above it **does** include it:

```xml
<_BuildApkEmbedInputs>
    ...
    ;@(_ApplicationSharedLibrary)
</_BuildApkEmbedInputs>
```

`_Upload` then fast-deploys only assemblies (`_FastDevFiles` contains .dlls only),
so the device ends up with the **new-MVID** library assembly plus the **old-MVID**
typemaps inside the installed APK's `libxamarin-app.so`. The managed→Java typemap
lookup is MVID-keyed in Debug, so constructing any JLO subclass from that assembly
throws.

Why it bites hardest with internal types: any library rebuild changes the MVID, but
teams using ref assemblies (`ProduceReferenceAssembly`, SDK default) typically only
see downstream rebuild activity on impl-only edits — everything looks incremental
and healthy, and the failure appears only at runtime. Users learn to "always clean
rebuild after editing that library" (which is what we did for months).

**Suggested fix.** Add `;@(_ApplicationSharedLibrary)` to
`_BuildApkFastDevStaticInputs` (populated via `_PrepareApplicationSharedLibraryItems`,
as the target already does for the task call at `ApplicationSharedLibraries=`).

**Workaround** (project/repo level):

```xml
<Target Name="_FixFastDevApkInputs"
        BeforeTargets="_BuildApkFastDev"
        DependsOnTargets="_DefineBuildTargetAbis;_PrepareApplicationSharedLibraryItems">
  <PropertyGroup>
    <_BuildApkFastDevStaticInputs>$(_BuildApkFastDevStaticInputs);@(_ApplicationSharedLibrary)</_BuildApkFastDevStaticInputs>
  </PropertyGroup>
</Target>
```

Two traps hit while landing the workaround (both cost a re-verification):

1. `_PrepareApplicationSharedLibraryItems` expands `%(_BuildTargetAbis.Identity)`,
   populated by `_DefineBuildTargetAbis`. Without that dependency the item — and
   thus the appended input — is silently EMPTY. Our copy warns when it detects this.
2. MSBuild stops at the NEAREST `Directory.Build.targets`; a sample-level file
   shadows the repo-level one, so the fix must live in a file imported by every
   Android app project (`src/Comet/eng/FastDevApkFix.targets`, imported from both).

Verified end-to-end: impl-only library edit → incremental `-t:Run` → `.so` relinks
→ APK rebuilds + reinstalls → app survives (three consecutive edit cycles, both a
facade edit and Comet.dll edits). Without the workaround the same edit crashes as
above.
