# Gemini Nano structured-output experiment

These opt-in Kotlin probes exercise the [official ML Kit structured-output API](https://developers.google.com/ml-kit/genai/prompt/android/structured-output)
with a compile-time schema and a bounded runtime JSON Schema adapter. Neither
is wired into `GeminiNanoChatClient` or Chat Playground. General-purpose .NET
JSON Schema support and typed streaming are not implemented.

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

## Runtime schema providers

`RuntimeStructuredOutputProbe.generate(prompt, schemaJson, modelId)` accepts
a supported runtime JSON schema and returns projected JSON, native input
tokens, and the actual model name. For example:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["destination", "travelers"],
  "properties": {
    "destination": { "type": "string" },
    "travelers": { "type": "integer", "minimum": 1, "maximum": 10 }
  }
}
```

The adapter uses Google's public
[`GenerableProvider`](https://developers.google.com/android/reference/com/google/mlkit/genai/schema/guided/GenerableProvider)
and `GenerableDetail` contracts. Its map-backed carrier classes have no
`@Generable` annotation or schema-specific fields. Handwritten service
providers supply field metadata compiled from the runtime schema. The SDK
builds the native response schema and parses matching JSON into those carriers;
the adapter then validates and projects their values back to JSON.
There are no private SDK hooks, runtime bytecode generation, or manually
injected schema prompts in this path.

The SDK caches provider instances, not their returned metadata. A coroutine
mutex therefore holds one immutable schema graph across feature checks,
token counting, inference, parsing, projection, and model cleanup. Different
runtime requests queue rather than replacing metadata during another request.
State is cleared on completion, cancellation, or failure. Nested objects use
distinct carrier identities, with an explicit maximum of four object shapes
per schema.

Handwritten service registrations are packaged in a resource-only JAR so
KSP's generated service-file copy cannot overwrite them. The opt-in APK merges
both registrations; the default AAR excludes all runtime providers and their
service JAR.

The current adapter supports object roots, nested objects, strings/string
enums, booleans, bounded Int32 integers, and arrays of strings, booleans, or
objects with length bounds. Every object property must be required and every
object must declare `additionalProperties=false`. Nullable/optional fields,
unions, references, floating-point numbers, nested arrays, integer arrays,
and other schema keywords are rejected before model use.

Integers require explicit minimum and maximum within the Int32 range. The
SDK's generic map deserializer uses `Double`, which cannot preserve arbitrary
Int64 values. Int32 values are exact and are projected to JSON integers only
after verifying integerness and declared bounds. The adapter also checks enum
membership, array lengths, and object keys: the SDK's fixture parser alone
does not enforce all of those constraints. Unsupported input or invalid output
fails instead of silently weakening the schema.

## Managed integration boundary

Beta5 has no public runtime JSON-schema setter and no typed streaming overload.
Its typed request still takes a Kotlin `KClass`, but that is not a fundamental
runtime-schema barrier: public providers can supply runtime metadata for a
generic carrier, as this probe demonstrates. This is a bounded adapter over
the provider interface, not a documented general-purpose JSON Schema entry point.

The new dynamic `FunctionSchema` API describes tools, not typed response
schemas. The managed build still consumes the normal AAR and rejects JSON
response formats; running the native opt-in command does not enable a .NET
chat capability.

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
SDK-parsed sentiment objects and runtime trip/order/itinerary schemas, including
nested objects and arrays. Concurrent requests with different schemas exercise
metadata isolation. Assertions cover projected values, bounds, native input
tokens, and actual model names. Unsupported or unprepared models fail explicitly;
there is no download, inference skip, or fallback.

The suites passed on a physical Pixel 10 running Android 16: stable/full
selected `nano-v3`, preview/fast selected `nano-v4-fast`, and both advertised
structured output and returned parsed fixed and runtime-schema objects with
valid projected values and typed token counts. This establishes the tested
schema subset on those profiles, not arbitrary schemas or all devices.

R8/minified provider discovery and parsing remain unverified. The opt-in
consumer keep rules preserve the annotated DTO, generated providers, and
annotation metadata, but must still be exercised in a minified host. Any
subsequent real-inference .NET tests must use
`[Trait(TestTraits.RequiresModel, TestTraits.True)]`.
