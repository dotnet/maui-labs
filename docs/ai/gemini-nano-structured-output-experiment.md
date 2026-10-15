# Gemini Nano structured-output experiment

This opt-in Kotlin probe exercises the [official ML Kit structured-output API](https://developers.google.com/ml-kit/genai/prompt/android/structured-output)
with one compile-time schema. It is not wired into `GeminiNanoChatClient` or
the Chat Playground. Generic .NET JSON Schema support and typed streaming
are not implemented.

## Build the probe

From the repository root, with JDK 17+ and an Android SDK installed:

```bash
cd src/AI/AndroidNative/EssentialsAI
# For example, on macOS:
export JAVA_HOME="$(/usr/libexec/java_home)"
export ANDROID_HOME="$HOME/Library/Android/sdk"

# Include the probe and its deterministic JVM tests.
./gradlew --no-daemon -PenableGeminiStructuredOutputProbe=true testReleaseUnitTest assembleRelease exportMlKitRuntime

# Build the normal bridge without the probe.
./gradlew --no-daemon testReleaseUnitTest assembleRelease exportMlKitRuntime
```

The opt-in adds `src/structuredOutputProbe/kotlin` and
`src/structuredOutputProbeTest/kotlin` to the native source sets, applies KSP,
and resolves Google's schema compiler. Its AAR and runtime exports go under
`build/structured-output-probe/`; the normal build uses `build/`.
Do not copy the experimental AAR over the shipping artifact.

The build pins KSP **2.3.6** (the official guide's minimum) with Kotlin
**2.3.0**, Prompt/Common **1.0.0-beta4**, and
Schema/Schema Compiler **1.0.0-alpha1**. Bytecode targets JVM 17 and minSdk
is 26. The schema compiler is build-only; exported runtime dependencies
remain Prompt, Common, and Schema.

## Fixed schema and native request

The source is
[`GeminiNanoStructuredOutputProbe.kt`](../../src/AI/AndroidNative/EssentialsAI/src/structuredOutputProbe/kotlin/com/microsoft/maui/essentials/ai/experimental/GeminiNanoStructuredOutputProbe.kt).
`SentimentProbe` is an `@Generable` class with two required `@Guide` fields:
`sentiment: String` restricted to `positive`, `negative`, or `neutral`, and
`confidence: Int` bounded to 0-100.

`GeminiNanoStructuredOutputProbe.generate(prompt)` checks structured-output
availability and model readiness, then wraps the unchanged input in
`generateTypedContentRequest` for `SentimentProbe::class`. The SDK's default
`includeSchemaInPrompt=true` provides the schema guidance. Both `countTokens`
and `generateContent` receive the typed request. Only a non-null parsed
candidate with `TypedFinishReason.STOP` succeeds; other results fail with
the native finish reason. Native exceptions propagate, and each call closes
its model in `finally`.

Pinned beta4 exposes `STOP=0`, `MAX_TOKENS=1`, and `OTHER=-100`, not all
the finish constants listed in the current Google guide. Unknown and null
finish reasons also fail rather than being treated as success.

KSP emits `SentimentProbe_GeneratedProvider.kt` under
`build/structured-output-probe/generated/ksp/release/kotlin/` and a
`META-INF/services/com.google.mlkit.genai.schema.guided.GenerableProvider`
registration. JVM tests exercise provider discovery, generated field types
and constraints, request mapping, capability/readiness failures, and
constructed candidate fixtures. They do not run model inference.

## Managed integration boundary

The official typed API requires a Kotlin `KClass` and generated provider,
not arbitrary runtime JSON Schema. Beta4 exposes no typed streaming overload.
The fixed probe therefore does not establish a generic MEAI adapter.

The managed build deliberately consumes the normal AAR.
[Documented `AndroidGradleProject` metadata](https://learn.microsoft.com/dotnet/android/building-apps/build-items#androidgradleproject)
does not offer arbitrary Gradle argument forwarding; running the opt-in
Gradle command does not enable a .NET bridge or device test.

## Physical-device follow-up

Successful inference, schema adherence, and R8/minified provider discovery
and parsing remain unverified. Use a Kotlin host that explicitly includes
the experimental AAR on a supported, provisioned physical AICore device;
Android emulators cannot establish model support. Check capability/readiness,
successful parsed output, native failure paths, and minified packaging.
The opt-in consumer keep rules preserve the annotated DTO, generated providers,
and annotation metadata, but must still be verified in that host.

Decide whether a narrow managed test bridge is warranted after native
inference is demonstrated. Any subsequent real-inference .NET tests must use
`[Trait(TestTraits.RequiresModel, TestTraits.True)]`.
