# Kotlin and JVM shapes

Bind the compiled Android/JVM artifact, not Kotlin source syntax or a multiplatform
metadata-only artifact. Verify the selected Maven variant and its dependencies.
Use the source, class files (`javap -p -s`), class-parse output, and generated C#
together. The generator's Kotlin support varies with the installed SDK.

## Inspect before transforming

| Kotlin feature | What to inspect and how to proceed |
|----------------|-----------------------------------|
| Top-level functions/extensions | Usually static methods on a `FileNameKt` facade (or a name set by `@file:JvmName`); the extension receiver is a JVM parameter |
| `object` / companion methods | Instance access through singleton/companion unless a static entry point was generated; `@JvmStatic` is not required on ordinary instance methods |
| Properties | Getter/setter methods, special `is...` naming, or exposed fields; inspect C# property generation before renaming |
| Default arguments | Full-arity JVM method and possible synthetic helpers; use real overloads, or add `@JvmOverloads` in owned native source when applicable |
| `internal` declarations | May be JVM-public yet intentionally hidden by Kotlin metadata; a diagnostic like `Kotlin: Hiding internal class` explains the omission |
| Generics and variance | Erasure, wildcards, bridges, and differing managed collection contracts; inspect precise generated types before casting |
| Nullable primitives | Can be boxed JVM types, not the same shape as primitive `int`/`boolean`; preserve null semantics rather than assuming every wrapper becomes a C# primitive |
| Value classes / mangled names | Look for actual JVM entry points and metadata support; a C# rename cannot make a nonexistent unmangled Java method callable |

An API filtered before `api.xml` cannot be restored just by `managedName` or
`visibility` on a nonexistent node. Prefer a public supported native API. If
exposing an internal member is unavoidable, verify SDK-specific support and
discuss the compatibility risk rather than promising a universal metadata switch.

## Async and functional APIs

Do not promise automatic idiomatic C# translations:

- A `suspend` method's JVM ABI uses a continuation and coroutine result conventions;
  it is not automatically a `Task<T>` just because binding generation succeeds.
- `Flow<T>` is not automatically `IAsyncEnumerable<T>` or a C# event.
- Kotlin function interfaces are not automatically C# delegates.
- Inline/reified Kotlin APIs may not offer an ordinary Java-callable entry point.

Some underlying JVM types may be bindable with compatible Kotlin/coroutine
dependencies. Distinguish **bindable bytecode** from **usable C# ergonomics** rather
than claiming all Kotlin async APIs are impossible to bind.

Prefer an existing library-provided Java-friendly callback API. If a facade is
needed, use `android-slim-bindings`: expose simple public methods and Java-style
listener interfaces while keeping coroutines/Flow implementation in Kotlin.
Use `@JvmStatic` only where a static object/companion entry point is intended.

Design async bridging explicitly: ownership and cancellation of the coroutine,
error delivery, callback thread, listener lifetime and cleanup, and completion
exactly once for one-shot operations. A callback-to-Task helper must preserve
those rules; do not implement async behavior as a blocking call on the UI thread.

## Consumer checks

Compile against the generated C# names rather than guessing them from Kotlin.
Exercise static/instance entry points, nullable and boxed values, callback
registration/unregistration, cancellation/errors, and dependency packaging in an
Android consumer. Verify Release behavior with the app's trimming configuration.

## Sources

- [Calling Kotlin from Java](https://kotlinlang.org/docs/java-to-kotlin-interop.html)
- [Missing types or members, including Kotlin visibility](https://github.com/dotnet/java-interop/wiki/Missing-Types-or-Members)
