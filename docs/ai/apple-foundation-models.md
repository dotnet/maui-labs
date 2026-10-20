# Apple Foundation Models

`AppleIntelligenceChatClient` implements `Microsoft.Extensions.AI.IChatClient`
with Apple's on-device Foundation Models framework. This document covers Apple
toolchain, runtime, and interop details. Shared message, tool, structured-output,
streaming, and error behavior is defined in the
[chat provider implementation guide](local-chat-provider.md).

## Platform support

- Text chat requires iOS, Mac Catalyst, or macOS 26+ with Apple Intelligence
  available and enabled.
- OS 27 adds model capability discovery, image input, native token usage metadata,
  and explicit tool-calling modes.
- The system model reports availability as available, device not eligible,
  Apple Intelligence not enabled, or model not ready.
- Status reports the provider's effective MEAI-facing capabilities, not every
  feature reported by the native model. Text/streaming are always advertised;
  OS 26 retains known structured/tool support, while OS 27 gates structured output,
  tools, and images on guided-generation, tool-calling, and vision flags.
  Token usage requires OS 27; reasoning is not advertised until MEAI options map.
- The provider never silently switches to a cloud model.

## Toolchain and build

The OS 27 Foundation Models APIs require Xcode 27 and the corresponding .NET
reference packs. Build from the repository root:

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Use `net10.0-ios27.0` or `net10.0-macos27.0` for those platforms.
`UseXcode27Preview` selects reference-pack targets; it does not install or
select Xcode. The official pipeline also builds unsuffixed Apple target
frameworks against Xcode 27 with its validated compatibility settings.

## Registration

Register the client only on a supported runtime:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

if (OperatingSystem.IsIOSVersionAtLeast(26) ||
    OperatingSystem.IsMacCatalystVersionAtLeast(26) ||
    OperatingSystem.IsMacOSVersionAtLeast(26))
{
    builder.Services.AddSingleton<IChatClient>(new AppleIntelligenceChatClient());
}
```

## Model status

```csharp
var status = await AppleIntelligenceChatClient.GetModelStatusAsync();
if (status.Availability != ChatModelAvailability.Available)
    Console.WriteLine(status.UnavailableReason);

var supportsImages = status.Capabilities?.Contains(ChatModelCapability.ImageInput) == true;
```

## Image input

Image input requires OS 27+ and a system model that reports vision support.
Use existing MEAI content types:

```csharp
var message = new ChatMessage(ChatRole.User,
[
    new TextContent("Describe the object and its color."),
    new DataContent(pngBytes, "image/png"),
]);

var response = await chat.GetResponseAsync([message]);
```

Supported representations include:

- encoded `DataContent` with an ImageIO-supported `image/*` media type;
- local `file://` `UriContent`;
- `CGImage`, `UIImage`, or `NSImage` in `RawRepresentation`, while retaining
  encoded bytes for recording and cross-provider use.

Remote HTTP image URLs and unsupported `RawRepresentation` types are rejected
with `NotSupportedException`, as are PDF/audio media and hosted files. Empty or
undecodable image bytes and missing/invalid local image files fail with
`ArgumentException`. Unsupported OS versions or unavailable vision capability
also fail explicitly.

Unknown/custom `AIContent` items are omitted individually; unknown-only turns
follow the blank-message rules. Standard MEAI content is never silently dropped:
System/User/Assistant accept text and images, Assistant also accepts function
calls, and Tool accepts only function results.

The native bridge preserves text/image ordering in the current prompt and in
prior transcript entries. Image-bearing history uses native transcript
attachments so follow-up questions can refer to earlier images. Reverse
conversion returns portable PNG bytes and a native `CGImage`.

`UIImage` orientation and encoded EXIF orientation are preserved through the
bridge. Explicit EXIF values 1–8 map to `CGImagePropertyOrientation`; missing
orientation uses the image's natural orientation. Pixels are normalized when
native transcript attachments are converted back to MEAI content.

This provider implements chat image input only. It does not implement
`IImageGenerator` or Apple Image Playground.

## Native tools and tracing

Foundation Models invokes registered `AIFunction` tools inside the native
session. Native callbacks run outside the managed request thread, so the bridge
captures and restores the managed execution context. Tool execution, logs, and
child activities remain correlated with the MEAI chat activity across awaits.
Caller cancellation remains cancellation even when Foundation Models wraps a
managed tool callback failure.

OS 26 uses the native default tool behavior. OS 27 exposes explicit allowed,
required, and disallowed modes. Required uses a `LanguageModelSession.DynamicProfile`
and a session-local tool-call counter: it starts required, then becomes allowed
after the first tool call so generation can finish with text. Request generation
options do not override that dynamic mode. See the shared guide for the MEAI mode mapping.

## Native schema mapping

Response and tool-parameter schemas preserve nullable values independently of parent
`required` properties. Native guides carry string constants/enums, a scalar ASCII
regex subset, numeric bounds, and array/item bounds. Unrepresentable constraints,
references, dictionaries, and compound enum/const-plus-pattern intersections fail
with schema/property context rather than being dropped. The current system model
can reject regex guides with `unsupportedGenerationGuide`; that error is propagated
without substituting another schema.
JSON Schema `format` is annotation metadata, not a native constraint. Tool return
schemas are not used for generation and do not gate registration; parameter
schemas remain strict.

## Streaming and token usage

Foundation Models emits cumulative plain-text snapshots. The Swift bridge
converts them into fail-closed append-only deltas before crossing into managed
code, comparing exact UTF-8 prefixes and resetting the text epoch before each
tool-call event. Structured generation uses native completion metadata to produce
append-only JSON deltas.

OS 27 exposes input, output, total, cached-input, and reasoning token counts.
Earlier OS versions return no token usage metadata. These counts map to
`UsageDetails` on `ChatResponse.Usage` or a final streaming `UsageContent`.

## Validation

Run the native Swift suite on a supported Mac:

```bash
swift test --package-path src/AI/AppleNative
```

Run live Foundation Models coverage when the system model is ready:

```bash
ESSENTIALSAI_LIVE_TESTS=1 swift test --package-path src/AI/AppleNative
```

Run managed Mac Catalyst device tests:

```bash
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

Model-dependent tests use `RequiresModel=true`. Runtime availability checks use
the actual OS and system model, not only the compile target.

## Native API references

- [Foundation Models](https://developer.apple.com/documentation/foundationmodels)
- [SystemLanguageModel](https://developer.apple.com/documentation/foundationmodels/systemlanguagemodel)
- [LanguageModelSession](https://developer.apple.com/documentation/foundationmodels/languagemodelsession)
- [Transcript](https://developer.apple.com/documentation/foundationmodels/transcript)
- [Analyzing images with multimodal prompting](https://developer.apple.com/documentation/foundationmodels/analyzing-images-with-multimodal-prompting)
