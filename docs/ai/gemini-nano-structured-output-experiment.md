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

The opt-in adds the `structuredOutputProbe`, `structuredOutputProbeTest`, and
`structuredOutputProbeAndroidTest` source directories, applies KSP, and
resolves Google's schema compiler and Android instrumentation dependencies.
Its AAR and runtime exports go under `build/structured-output-probe/`;
the normal build uses `build/` and excludes the probe and its test host.
Do not copy the experimental AAR over the shipping artifact.

The build pins KSP **2.3.6** with Kotlin **2.4.0**,
Prompt/Common **1.0.0-beta5**, and Schema/Schema Compiler **1.0.0-alpha2**.
Google's alpha2 compiler itself depends on KSP 2.3.6 and Kotlin 2.4.0;
these versions were published in the
[October 26, 2026 release](https://developers.google.com/ml-kit/release-notes#october_26_2026).
Bytecode targets JVM 17 and minSdk
is 26. The schema compiler is build-only; exported runtime dependencies
remain Prompt, Common, and Schema.

## Fixed schema and native request

The source is
[`GeminiNanoStructuredOutputProbe.kt`](../../src/AI/AndroidNative/EssentialsAI/src/structuredOutputProbe/kotlin/com/microsoft/maui/essentials/ai/experimental/GeminiNanoStructuredOutputProbe.kt).
`SentimentProbe` is an `@Generable` class with two required `@Guide` fields:
`sentiment: String` restricted to `positive`, `negative`, or `neutral`, and
`confidence: Int` bounded to 0-100.

`GeminiNanoStructuredOutputProbe.generate(prompt, modelId)` reuses the native
bridge's model selection, checks readiness, then checks
`Feature.STRUCTURED_OUTPUT` in `getAvailableFeatures()`. It wraps the unchanged input in
`generateTypedContentRequest` for `SentimentProbe::class`. The SDK's default
`includeSchemaInPrompt=true` provides the schema guidance. Both `countTokens`
and `generateContent` receive the typed request. Only a non-null parsed
candidate with `TypedFinishReason.STOP` succeeds; other results fail with
the native finish reason. Native exceptions propagate, and each call closes
its model in `finally`.

Pinned beta5 exposes `STOP=0`, `MAX_TOKENS=1`, and `OTHER=-100`, not all
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
not arbitrary runtime JSON Schema. Beta5 exposes no typed streaming overload.
Its new dynamic `FunctionSchema` API describes tools, not typed response
schemas; using a tool as a JSON-response workaround would not prove generic
structured-output support. The fixed probe therefore does not establish a
generic MEAI adapter. The managed build still consumes the normal AAR and
rejects JSON response formats; running the native opt-in command does not
enable a .NET chat capability.

## Physical-device tests

The opt-in Android instrumentation suite provides a separate foreground
Activity; it does not replace Chat Playground or change AICore settings/data.
AICore blocks background inference, so a headless instrumentation test is
insufficient. Keep the device unlocked and the test Activity in the foreground.

From `src/AI/AndroidNative/EssentialsAI`, target an explicitly selected,
supported physical device:

```bash
# Replace SERIAL with the serial reported by adb devices.
ANDROID_SERIAL=SERIAL ./gradlew --no-daemon \
  -PenableGeminiStructuredOutputProbe=true \
  -Pandroid.testInstrumentationRunnerArguments.modelId=gemini-nano-preview-fast \
  connectedDebugAndroidTest

# Test the stable/full profile separately, without downloading/changing models.
ANDROID_SERIAL=SERIAL ./gradlew --no-daemon \
  -PenableGeminiStructuredOutputProbe=true \
  -Pandroid.testInstrumentationRunnerArguments.modelId=gemini-nano-full \
  connectedDebugAndroidTest
```

The suite reports native feature/readiness values for both profiles and asserts
SDK-parsed positive/negative sentiment objects, confidence bounds, native input
token counts, and nonempty actual model names. Unsupported or unprepared models
fail explicitly; there is no download, inference skip, or fallback.

On November 3, 2026, the suite passed on a physical Pixel 10 running Android 16:
stable/full selected `nano-v3`, preview/fast selected `nano-v4-fast`, and both
advertised structured output and returned parsed objects with valid constraints
and typed token counts. This establishes only this fixed schema on those
profiles, not arbitrary schemas or all supported devices.

R8/minified provider discovery and parsing remain unverified. The opt-in
consumer keep rules preserve the annotated DTO, generated providers, and
annotation metadata, but must still be exercised in a minified host. Any
subsequent real-inference .NET tests must use
`[Trait(TestTraits.RequiresModel, TestTraits.True)]`.
