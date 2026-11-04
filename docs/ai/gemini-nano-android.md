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
  `SystemInstruction`, after checking `Feature.SYSTEM_PROMPT` in
  `getAvailableFeatures()`.
- A single user prompt is passed through unchanged. With assistant/tool history,
  messages receive ordinary `User:`/`Assistant:`/`Tool:` text labels and
  are included in order. Text and image parts remain interleaved; images are not
  replaced with text placeholders.
- Temperature, TopK, Seed, and MaxOutputTokens map to the native builder.
  C# checks known ranges and Seed's 32-bit representability; Google's builder
  also validates native numeric constraints.
- `Reasoning.Output = ReasoningOutput.Full` enables thinking after checking
  `Feature.THINKING_MODE` in `getAvailableFeatures()`. Effort levels and summaries
  have no native mapping.

There is no client-held conversation state. Callers supply the conversation on
each request. Custom content and unknown roles (including `developer`) are
omitted; recognized unsupported content is rejected.

Without tool registration, historical calls/results retain the existing textual
replay, with one JSON object per content item in encounter order:

```json
{"type":"function_call","callId":"id","name":"actual-tool-name","arguments":{"x":1}}
{"type":"function_result","callId":"id","result":{"value":2}}
```

No separators are inserted between content fragments within a message. Results
carry no invented function name. This is prompt history, not a native call or
an execution request. Active tool requests use the native representation below.
Replay accepts JSON values (`JsonElement`/`JsonDocument`, primitives, string-keyed
dictionaries, sequences, and serializable managed objects) through standard
`JsonSerializer.Serialize` with `AIJsonUtilities.DefaultOptions`. There is no
provider-specific recursive serializer or requirement to pre-convert managed
objects to `JsonElement`. Standard System.Text.Json serialization and
trimming/AOT limitations apply to the supplied argument/result types.

Unsupported roles and representations reject with `NotSupportedException`
without validating payload details that ML Kit cannot consume. URI media,
non-image media, hosted files, misplaced tool content, and other recognized
unsupported MEAI content follow this path.

Once content is in a supported role and representation, malformed payloads
reject with `ArgumentException`: tool IDs/names must be nonblank, and in-memory
images must have nonempty data, a valid media type, and decodable bytes. Images
are supported only in User/Assistant history; System accepts text instructions
and Tool accepts function results.

## Native tool calling

ML Kit **genai-prompt/common 1.0.0-beta5** and **genai-schema 1.0.0-alpha2**
(published October 26, 2026) add native function declarations, function-call and
function-response parts, native conversation roles, and the `FUNCTION_CALLING`
capability. Beta4 did not expose these APIs. This implementation uses those APIs,
not JSON prompting, model self-reports, an AgentRunner-owned invocation loop, or
the experimental Windows tool-planning wrapper.

Check `GetModelStatusAsync(modelId)` for `ChatModelCapability.ToolCalling`.
Availability depends on the selected AICore model, not just Android version.
Google currently requires AICore Developer Preview enrollment for this feature.
An Auto request with registered functions fails explicitly on a model without
the capability; it never switches models or falls back to cloud inference.

```csharp
using IChatClient chat = new GeminiNanoChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var options = new ChatOptions
{
    ModelId = "gemini-nano-preview-fast",
    Tools = [AIFunctionFactory.Create(
        (string key) => $"Local value for {key}",
        name: "lookup_value",
        description: "Looks up a value by key in the local database.")],
};
var response = await chat.GetResponseAsync(
    "Use lookup_value to find the value for orchard.", options, cancellationToken);
```

The provider registers `AIFunction` names, descriptions, and parameter schemas.
Kotlin maps supported schema fields directly to Google's `FunctionSchema`;
unrepresentable constraints (including `$ref`, `const`, and `additionalProperties`)
are rejected, not removed. Return schemas do not constrain generation.
Parameterless functions omit the native parameter schema: supplying an empty
object schema caused AICore `countTokens` to fail with tokenization error 10003.
The shared function suite covers parameterless invocation in both response modes.
None omits registration; Required and named Required are rejected because beta5
has no corresponding mode. Auto does not guarantee a tool will be chosen.

With registered tools, User/Tool turns use native `Content.Role.USER` and
Assistant turns use `Content.Role.MODEL`, without role-label prompt text.
Calls become `FunctionCallPart`; results become `FunctionResponsePart`, with
names resolved from matching call IDs in the supplied history. Scalar results
are wrapped in a `result` property. Null arguments/results cannot be preserved
by beta5's native value conversion and are explicitly rejected during replay.

Native calls become MEAI `FunctionCallContent` with `ChatFinishReason.ToolCalls`.
The standard function-invocation middleware executes them and sends
`FunctionResultContent` back on the next turn. Native call IDs are preserved;
when absent, the bridge assigns an ID once for caller-side correlation. Duplicate
IDs, unregistered names, and multiple calls when `AllowMultipleToolCalls` is false
fail before dispatch.

**Streaming limitation:** on the tested beta5/AICore combination, `onNewText`
exposes raw `<ctrl42>call:...` tokens that the final candidate removes. Therefore
tool-enabled turns withhold text until completion and emit only the final clean
text and typed calls. Reasoning can still stream. Tool-free requests retain the
existing incremental text stream. This avoids leaking raw call syntax, executing
partial calls, or invoking a call twice from both streamed and final results.

### Physical-device findings

On a Pixel 10 with beta5:

| Profile | Native model | Finding |
|---|---|---|
| Stable/full (default) | `nano-v3` | Ready, but no native function-calling capability; registration rejects explicitly |
| Stable/fast | No model available | Native feature resolution rejects with ML Kit error 606 |
| Preview/fast | `nano-v4-fast` | Native calls, real execution, result continuation, and tool-enabled streaming work |
| Preview/full | Previously resolved as `nano-v4-full` | Not downloaded or tool-validated in this investigation |

The secret-lookup experiment invoked a real local function exactly once and
returned its otherwise unknown `ORCHARD_SECRET_7291` result in both response
modes. The shared tool suite additionally exercised multiple tools, complex and
enum arguments, multi-turn result history, call/result ordering, and activity
context. The final unfiltered Android assembly passed **270/270** tests on the
physical device, including parameterless-tool regressions. Some shared
Required-mode cases intentionally return early because that mode is unsupported.
The portable Essentials.AI suite passed **295/295**, Playground tests **209/209**,
and native Kotlin/JVM tests **28/28**.

Playground was exercised through DevFlow on the same device with all three local
tools enabled. Streaming requests executed `calculate` (137 x 29 = 3973),
`get_current_local_datetime`, and `get_connection_status`; a non-streaming request
executed `calculate` (1256 + 789 = 2045). Saved protocol recordings verified real
call/result IDs and returned values, not just generated text claiming execution.
Use an isolated `-p:MauiDevFlowPort=29225` if the default agent port is occupied.

Intensive test runs initially hit ML Kit error 9 despite the test app being
foreground and the phone unlocked. The Pixel Developer Preview app's documented
**Settings > Bypass quota limits** option permits sustained testing. This is a
device-wide development setting with possible system-health implications, not a
provider fallback or production fix. It must not be enabled by the library.

These findings do not establish native support for Nano V3 or successful
inference on the full V4 model.

References: [Google function calling](https://developers.google.com/ml-kit/genai/prompt/android/function-calling),
[model selection](https://developers.google.com/ml-kit/genai/prompt/android/select-model),
and [ML Kit release notes](https://developers.google.com/ml-kit/release-notes).

## Request lifetime and callbacks

`GeminiNanoNativeClient` owns the coroutine supervisor and creates/closes the
ML Kit model directly for each request. Independent requests own independent
models; closing the client cancels and joins outstanding work.

`NativeChatStream` delivers ML Kit's text and reasoning updates in order and
waits for queued updates before completing the managed callback. The C# adapter
converts these updates to `ChatResponseUpdate` values.

Trailing whitespace is buffered until more content or the final response arrives,
so extra newlines omitted by ML Kit's final response are not displayed.

The final response reports the native finish reason, model name, and input
tokens from `countTokens`. Output token counts are not estimated.
Unavailable IDs, timestamps, and other usage counts remain null.
A candidate without text or function calls is rejected explicitly. Native
function-call-only responses contain typed calls without invented answer text.

## Model selection

`ChatOptions.ModelId` selects a profile for one request without changing the
client registered in DI:

| Model ID | Release stage | Preference |
|---|---|---|
| `gemini-nano` or null | Stable | Full (default) |
| `gemini-nano-full` | Stable | Full |
| `gemini-nano-fast` | Stable | Fast |
| `gemini-nano-preview-full` | Preview | Full |
| `gemini-nano-preview-fast` | Preview | Fast |

Use the same ID with `GetModelStatusAsync(modelId, cancellationToken)`,
`PrepareModelAsync(modelId, cancellationToken)`, and
`DownloadModelAsync(modelId, cancellationToken)`. Unknown IDs are rejected.
Responses report the actual SDK model name, such as `nano-v4-fast`, rather than
the selection alias. Preview support depends on the device and AICore enrollment.
Status queries propagate errors if ML Kit cannot resolve the selected model.

## Explicit download

For an explicit download without progress reporting, await preparation and then
query status again:

```csharp
await GeminiNanoChatClient.PrepareModelAsync(cancellationToken);
var status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
```

When the app needs native download progress, enumerate the progress stream:

```csharp
var status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
if (status.Availability == ChatModelAvailability.NotReady)
{
    await foreach (var update in GeminiNanoChatClient.DownloadModelAsync(cancellationToken))
        Console.WriteLine($"{update.State}: {update.BytesDownloaded ?? update.BytesToDownload}");

    status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
}
```

`DownloadModelAsync` requests a download through ML Kit and forwards its start,
progress, and completion notifications as `GeminiNanoDownloadUpdate` values.
Download failures throw exceptions. Stopping enumeration cancels this
subscription, but may not stop an AICore download shared with other apps.

Always check `GetModelStatusAsync` after preparation before enabling generation.
Download completion does not guarantee readiness or recovery of earlier byte
progress. Status checks never start a download.

DOWNLOADABLE maps to `NotReady`, DOWNLOADING to `Downloading`, and UNAVAILABLE to
`Unavailable`. Unrecognized status codes map to `Unknown`. Reasoning is advertised
only when `Feature.THINKING_MODE` is present in `getAvailableFeatures()`.

## Android minimum and model availability

The package, samples, and device tests require Android API 26 or later. The
bridge, GenAI Common, and GenAI Prompt AARs use the same minimum, so consuming
apps must also set `SupportedOSPlatformVersion` to at least `26.0`. No manifest
override or runtime API-level guard is required.

OS version alone does not establish model availability. Kotlin checks the
native feature status before inference. System instructions require Nano V3+;
thinking requires Nano V4+. Native tool requests additionally check the
function-calling capability. Unavailable capabilities return errors without
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
`NativeChatStreamTest` exercises callback ordering, text/thought reconciliation,
drain-before-terminal, cancellation, and late-event suppression without
abstracting the ML Kit model.
Status and download operations call ML Kit directly. Download Flow behavior,
model creation, and disposal remain device/model integration coverage rather
than introducing a fake model abstraction. Android binds the shared messages,
options, response, streaming, cancellation, usage, GetService, and schema
suites; provider-only tests cover interop/native policy.

EssentialsAI CI builds Chat Playground in a separate Android-only job. It is not
in the cross-platform shipping solution: the generic sample's pre-existing
Windows WinRT/AOT observable-field migration is not part of Gemini integration.
Portable playground tests remain in that solution. Tool replay uses the same
localized IL2026/IL3050 warning suppressions as the Windows provider's standard
JSON serialization path; this does not guarantee arbitrary type preservation
in trimmed/AOT consumers.

These tests do not establish successful model execution. A provisioned supported
physical device is needed to validate inference, conversation quality, image
decoding, thinking, and cancellation during real inference. An emulator can
exercise the unavailable-model error path but has no AICore model.
