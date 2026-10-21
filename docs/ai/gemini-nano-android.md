# Gemini Nano Android implementation

`GeminiNanoChatClient` adapts `Microsoft.Extensions.AI.IChatClient` to Google's
ML Kit GenAI Prompt API. The managed adapter is in
`src/AI/Microsoft.Maui.Essentials.AI/Platform/Android/`; the Kotlin bridge is in
`src/AI/AndroidNative/EssentialsAI/`.

For installation and supported features, see the
[package README](../../src/AI/Microsoft.Maui.Essentials.AI/README.md).

## Request mapping

C# maps MEAI roles, ordered text/image content, and options to internal native
DTOs. The [shared provider contract](local-chat-provider.md) defines content,
option, tool, state, and structured-output policies.

Kotlin constructs the Google request:

- Nonblank system-message text is concatenated within each message and joined
  with two LF characters between messages, options first, then passed as native
  `SystemInstruction`, after checking `isSystemPromptAvailable()`.
- A single user prompt is passed through unchanged. With assistant/tool history,
  messages receive ordinary `User:`/`Assistant:`/`Tool:` text labels and
  are included in order. Text and image parts remain interleaved; images are not
  replaced with text placeholders.
- Temperature, TopK, Seed, and MaxOutputTokens map to the native builder.
  C# checks known ranges and Seed's 32-bit representability; Google's builder
  also validates native numeric constraints.
- `Reasoning.Output = ReasoningOutput.Full` enables thinking after checking
  `isThinkingModeAvailable()`. Effort levels and summaries have no native mapping.

There is no client-held conversation state. Callers supply the conversation on
each request. Custom content and unknown roles (including `developer`) are
omitted; recognized unsupported content is rejected.

Tool replay uses one JSON object per content item, in encounter order:

```json
{"type":"function_call","callId":"id","name":"actual-tool-name","arguments":{"x":1}}
{"type":"function_result","callId":"id","result":{"value":2}}
```

No separators are inserted between content fragments within a message. Results
carry no invented function name. This is prompt history, not a native call or
an execution request. None/Auto tool modes omit registration; Required rejects.
Replay accepts JSON values (`JsonElement`/`JsonDocument`, primitives, string-keyed
dictionaries, and sequences) without reflection. Convert custom result objects
to `JsonElement` using the caller's serializer context before replaying them in
trimmed/AOT apps; unsupported object representations reject clearly.

## Request lifetime and callbacks

`GeminiNanoNativeClient` owns the coroutine supervisor and creates/closes the
ML Kit model directly for each request. Independent requests own independent
models; closing the client cancels and joins outstanding work.

`NativeChatEventQueue` is the horizontal concurrency boundary: it serializes
per-request text/thought callbacks and drains admitted progress before terminal
completion releases the managed JNI callback. Text/thought updates carry the
model name obtained before generation. The returned candidate is reconciled
with accumulated additional-text deltas: equal emits nothing, an extension
emits only its suffix, and divergence faults the stream. The C# adapter converts
events to `ChatResponseUpdate` values.

The final response reports the native finish reason, model name, and input
tokens from `countTokens`. Output token counts are not estimated.
Unavailable IDs, timestamps, and other usage counts remain null.

## Explicit download

```csharp
var status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
if (status.Availability == ChatModelAvailability.NotReady)
{
    await foreach (var update in GeminiNanoChatClient.DownloadModelAsync(cancellationToken))
        Console.WriteLine($"{update.State}: {update.BytesDownloaded ?? update.BytesToDownload}");
}
```

`DownloadModelAsync` directly collects ML Kit's cold download Flow, which starts
the download and reports `DownloadStarted`, `DownloadProgress`, and
`DownloadCompleted`. `DownloadFailed` becomes the native managed exception.
Stopping enumeration cancels this call's Flow collection; it does not promise to
cancel an AICore download shared with other apps.

The client does not poll readiness or join a download already reported as
DOWNLOADING. Callers own that policy: inspect `GetModelStatusAsync`, enumerate a
download only when appropriate, and query status again when deciding whether to
enable generation. Status is never cached. DOWNLOADABLE/DOWNLOADING map to
NotReady. ML Kit's UNAVAILABLE means either unsupported hardware **or** missing
AICore configuration; without more evidence it maps to Unknown, preserving that
native reason. Only a successful runtime `isThinkingModeAvailable` check
advertises Reasoning. Generation rejects non-AVAILABLE states without ever
starting a download.

## Android minimum and model availability

The package, samples, and device tests require Android API 26 or later. The
bridge, GenAI Common, and GenAI Prompt AARs use the same minimum, so consuming
apps must also set `SupportedOSPlatformVersion` to at least `26.0`. No manifest
override or runtime API-level guard is required.

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
`NativeChatEventQueueTest` exercises callback ordering, drain-before-terminal,
cancellation, and late-event suppression without abstracting the ML Kit model.
Status and download operations call ML Kit directly. Download Flow behavior,
model creation, and disposal remain device/model integration coverage rather
than introducing a fake model abstraction. `NativeStreamAccumulatorTest` covers
final-candidate reconciliation. Android binds the shared messages, options,
response, streaming, cancellation, usage, GetService, and schema suites;
provider-only tests cover interop/native policy.

EssentialsAI CI builds Chat Playground in a separate Android-only job. It is not
in the cross-platform shipping solution: the generic sample's pre-existing
Windows WinRT/AOT observable-field migration is not part of Gemini integration.
Portable playground tests remain in that solution. No AOT warnings are suppressed.

These tests do not establish successful model execution. A provisioned supported
physical device is needed to validate inference, conversation quality, image
decoding, thinking, and cancellation during real inference. An emulator can
exercise the unavailable-model error path but has no AICore model.
