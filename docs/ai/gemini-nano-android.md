# Gemini Nano Android implementation

`GeminiNanoChatClient` adapts `Microsoft.Extensions.AI.IChatClient` to Google's
ML Kit GenAI Prompt API. The managed adapter is in
`src/AI/Microsoft.Maui.Essentials.AI/Platform/Android/`; the Kotlin bridge is in
`src/AI/AndroidNative/EssentialsAI/`.

For installation and supported features, see the
[package README](../../src/AI/Microsoft.Maui.Essentials.AI/README.md).

## Request mapping

C# maps MEAI roles, ordered text/image content, and options to internal native
DTOs. It prepends `ChatOptions.Instructions` as a system message, following the
Apple adapter's convention. Unsupported content/options fail rather than being
silently discarded.

Kotlin constructs the Google request:

- System-message text is joined in order and passed as native
  `SystemInstruction`, after checking `isSystemPromptAvailable()`.
- A single user prompt is passed through unchanged. With assistant history,
  user/assistant messages receive ordinary `User:`/`Assistant:` text labels and
  are included in order. Text and image parts remain interleaved; images are not
  replaced with text placeholders.
- Temperature, TopK, Seed, and MaxOutputTokens map to the native builder.
  C# checks Seed's 32-bit representability; Google's builder validates native
  numeric constraints.
- `Reasoning.Output = ReasoningOutput.Full` enables thinking after checking
  `isThinkingModeAvailable()`. Effort levels and summaries have no native mapping.

There is no client-held conversation state. Callers supply the conversation on
each request. JSON response formats, tool content/options, and custom roles
remain unsupported.

## Request lifetime and callbacks

`NativeChatOperations` creates a model per request. Its coroutine supervisor
allows independent requests and cancellation; closing the client cancels and
joins the outstanding work.

Native streaming callbacks enter a per-request queue. Text/thought updates
carry the model name obtained before generation. Model cleanup and admitted
progress callbacks finish before terminal completion releases the managed JNI
callback. The C# adapter converts these events to `ChatResponseUpdate` values.

The final response reports the native finish reason, model name, and input
tokens from `countTokens`. Output token counts are not estimated.

## Android minimum and model availability

The package and samples retain the repository's API 24 minimum.
`GeminiNanoChatClient` requires API 26 and checks this before constructing its
native peer; Chat Playground also guards its DI registration.

The bridge, GenAI Common, and GenAI Prompt AARs declare minimum API 26.
Consumers with a minimum below API 26 must explicitly override those three
library manifests while keeping Gemini calls behind the runtime guard:

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          xmlns:tools="http://schemas.android.com/tools">
  <uses-sdk tools:overrideLibrary="com.microsoft.maui.essentials.ai,com.google.mlkit.genai.common,com.google.mlkit.genai.prompt" />
</manifest>
```

Leave minimum and target SDK selection to the project. The installed Android SDK
can omit `targetSdkVersion` when the template already has `<uses-sdk>`; see its
[manifest generation code](https://github.com/dotnet/android/blob/36.1.69/src/Xamarin.Android.Build.Tasks/Utilities/ManifestDocument.cs#L308-L329).
For these two samples, `Directory.Build.targets` supplies the merger's
`TARGET_SDK_VERSION` from `TargetPlatformVersion` to retain the framework default
instead of pinning another API level. If configuring another consumer affected
by that SDK behavior, the equivalent `Directory.Build.targets` setting is:

```xml
<Project>
  <PropertyGroup Condition="'$(TargetPlatformIdentifier)' == 'android'">
    <AndroidManifestMergerExtraArgs>$(AndroidManifestMergerExtraArgs) --property TARGET_SDK_VERSION=$([System.Version]::Parse('$(TargetPlatformVersion)').Major)</AndroidManifestMergerExtraArgs>
    <AndroidManifestMergerExtraArgs>$(AndroidManifestMergerExtraArgs.Trim())</AndroidManifestMergerExtraArgs>
  </PropertyGroup>
</Project>
```

This applies to consumers using the framework-default target SDK; keep an
intentional lower target if the app explicitly selects one.

OS version alone does not establish model availability. Kotlin checks the
native feature status before inference. System instructions require Nano V3+;
thinking requires Nano V4+. Unavailable capabilities return errors without
prompt-text or cloud fallbacks.

## Build and package

`AndroidGradleProject` invokes the checked-in Gradle wrapper, builds the bridge,
and runs its JVM tests. The SDK collects the bridge AAR and the pinned
Prompt/Common/Schema runtime archives. Shared Kotlin, coroutine, AndroidX, and
Google dependencies come from NuGet to avoid packaging duplicate Java classes.
Bindings for the bridge remain internal.

Java 17+ and the Android SDK are required when building from source. Kotlin and
Java emit Java 17 bytecode without requiring an exact Java 17 toolchain.

From the repository root:

```sh
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-android
```

To run the native tests directly, set `ANDROID_HOME` to the installed SDK and run:

```sh
cd src/AI/AndroidNative/EssentialsAI
./gradlew --no-daemon testReleaseUnitTest assembleRelease
```

## Validation boundaries

`NativeChatRequestTest` checks the Google request shape and capability errors.
`NativeChatOperationsTest` uses fake models to exercise concurrency,
cancellation, callback ordering, and cleanup. Managed tests cover DTO mapping
and playground behavior.

These tests do not establish successful model execution. A provisioned supported
physical device is needed to validate inference, conversation quality, image
decoding, thinking, and cancellation during real inference. An emulator can
exercise the unavailable-model error path but has no AICore model.
