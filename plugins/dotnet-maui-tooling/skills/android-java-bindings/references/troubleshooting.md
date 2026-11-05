# Diagnostic pipeline

Read this when an API is missing, a binding build fails, or a successful build
produces an incomplete consumer API.

## Establish evidence first

Use a diagnostic **rebuild**, not an incremental build that might skip generation.
Inspect the binary log with `binlogtool` and the accompanying diagnostic text log.
Record the SDK/workload, actual input archive versions, configuration, and first
causal diagnostic. Do not infer the archive's API from a different version's docs.

The usual intermediate sequence under `obj\<Configuration>\<TFM>` is:

| Artifact | What to inspect when the API first disappears |
|----------|----------------------------------------------|
| Input JAR / AAR's `classes.jar` | Wrong artifact/version/variant; member not present or not public |
| `api.xml.class-parse` | Class parser, Java visibility, Kotlin metadata/visibility |
| `api.xml` | Java resolution: missing base/interface/parameter types |
| `api.xml.fixed` | User metadata transforms, especially `remove-node` |
| `generated\src\*.cs` | Generator warnings, managed name collisions, unsupported shapes |
| Binding assembly / consuming project | C# compilation, wrong/stale assembly reference, incompatible target |

Locate actual files from build output rather than assuming every SDK emits every
intermediate at exactly these paths.

### Nothing from an artifact is generated

Check evaluated `AndroidLibrary`/`AndroidMavenLibrary` items and `Bind` metadata.
Verify the archive really contains JVM classes. `Bind="false"` intentionally
skips managed wrappers. Confirm the artifact wasn't duplicated or excluded.

### Removed during Java resolution

Inspect `java-resolution-report.log` beside the intermediates. Trace a missing
base type chain to its root rather than fabricating C# base classes.

For example, `WidgetActivity -> VendorActivity -> FragmentActivity` can disappear
because `androidx.fragment.app.FragmentActivity` is unresolved. A compatible
`Xamarin.AndroidX.Fragment` reference can restore the chain; verify the actual
reported dependency and versions first.

Supplying only runtime bytecode is not necessarily sufficient when the required
C# API inherits or exposes dependency types. Check managed reference availability
as well as the Java resolver inputs.

### Removed during metadata

Compare `api.xml` and `api.xml.fixed`; narrow or remove the unintended transform.
Do not remove an entire package just to silence an error on one required class.
If an API was never parsed, a renaming transform cannot bring it back.

### Removed during generation

Search warnings for the original Java name and generated managed name.
For example, BG8401 can report a nested `Builder` type colliding with a `BUILDER`
field normalized to `Builder`. Rename the field with `managedName`, preserving
the required type. BG8403 points to a type/namespace name collision.

Binding tools sometimes omit an API and issue only a warning so that the remaining
binding is usable. Do not equate "build succeeded" with complete coverage.
Triage warnings against required APIs; explain intentionally unbound optional
APIs instead of globally disabling warnings or blindly promoting every warning
to an error.

## Compiler-error triage

Error codes are hints, not unique diagnoses. Read both declarations.

| Symptom | Likely repair after signature comparison |
|---------|------------------------------------------|
| CS0535: interface parameter differs | Add a forwarding overload matching the interface, or a proven `managedType` transform |
| CS0738: interface return/property type differs | Explicit interface implementation forwarding to the typed API, or suitable `managedReturn` |
| CS0534: inherited abstract member unimplemented | Compare base contract and generated override; correct return mapping/modifier as needed |
| CS0111: duplicate methods | Targeted `managedName` for signatures that collapse in C# |
| CS0111/CS0102: duplicate `EventArgs` contents | Distinct `argsType` on each relevant listener method |
| CS0102: duplicate C# event | Distinct `eventName` or suppress only one event projection |
| CS0102: duplicate `...Handler` in implementor | Distinct `managedName` on the overloaded listener method |
| CS0115: no suitable method to override | Check inherited API, then targeted `managedOverride` |
| BG0000: generator exception | Check malformed transforms/types, then reduce a repro for the installed generator |

See [metadata recipes](metadata-recipes.md) for examples and guardrails.

## Runtime failures after compilation

- `NoClassDefFoundError`: inspect packaged classes and transitive dependencies,
  especially anything marked ignored or excluded from packaging.
- Duplicate Java classes during D8/R8: locate both owning JARs/AARs/packages and
  remove the redundant inclusion. `Bind="false"` still packages Java classes.
- `NoSuchMethodError`: compare packaged artifact versions, JVM descriptors, and
  generated JNI calls. A managed rename must not change the Java entry point.
- `UnsatisfiedLinkError`: inspect the library's native `.so` files, ABI and
  transitive native dependencies.
- Missing callbacks: verify listener registration, generated invokers/implementors,
  lifetime/unregistration, and the library's thread requirements. Implement Java
  listeners with a Java peer (typically deriving from `Java.Lang.Object`), not a
  plain C# object. Do not assume moving everything to the UI thread fixes dispatch.

## Source material

- [Missing types or members](https://github.com/dotnet/java-interop/wiki/Missing-Types-or-Members)
- [Troubleshooting index and warning policy](https://github.com/dotnet/java-interop/wiki/Troubleshooting-Android-Bindings-Issues)

The wiki includes Xamarin-era paths and build actions. Use it to understand the
pipeline, but verify current SDK behavior instead of copying old project items.
