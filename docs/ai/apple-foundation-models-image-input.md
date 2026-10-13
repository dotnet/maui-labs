# Apple Foundation Models image input

`Microsoft.Maui.Essentials.AI`'s `AppleIntelligenceChatClient` accepts images
through existing `Microsoft.Extensions.AI` chat content. It analyzes images
on-device; it does not generate images or send them to Azure. The implementation
is in `src/AI/AppleNative/EssentialsAI/` and
`src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS/AppleIntelligenceChatClient.cs`.

## Availability and toolchain

Foundation Models' image attachment APIs require the Xcode 27 SDK. Image
inference requires iOS, Mac Catalyst, or macOS **27+**, a ready on-device
model, and `SystemLanguageModel.default.capabilities.contains(.vision)`.
Text chat still works on supported OS 26 devices. `ImageContentNative` itself
only uses older Foundation/CoreGraphics/CoreImage/CoreVideo/ImageIO APIs, so it can be constructed
on OS 26; Swift's `#available` checks prevent calls to the OS 27 Foundation
Models image APIs and return an explicit error for image requests. The
playground advertises image input only on OS 27+.

CI uses Xcode 27 and .NET workload `10.0.401`. With Xcode 27 selected as the
default, build against the 27.0 reference pack from the repository root:

```bash
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true
```

The official build also uses Xcode 27 for unsuffixed Apple target frameworks
with 26.x reference packs and passes `ValidateXcodeVersion=false` for that
combination; the command above targets 27.0 and needs no such override.

## Send an image

Put a `DataContent` with an `image/*` media type in `ChatMessage.Contents`,
alongside the text prompt. A `UriContent` pointing to a local `file://` URL
also works. Remote `http(s)` image URLs are **not** downloaded; they throw
`NotSupportedException` unless a supported native `RawRepresentation` supplies
the image instead. Unsupported media types are rejected, not
interpreted as images.

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

using var client = new AppleIntelligenceChatClient();
var message = new ChatMessage(ChatRole.User,
[
    new TextContent("Describe the object and its color."),
    new DataContent(pngBytes, "image/png"),
]);
var response = await client.GetResponseAsync([message]);
Console.WriteLine(response.Text);
```

To ask a follow-up about the same image, include the original image-bearing
user message and the prior assistant response in the next request's history.
Both `GetResponseAsync` and `GetStreamingResponseAsync` support image input.
History attachments are preserved at the protocol level, but native model recall
is not guaranteed. On a macOS 27.0.1 system model, a solid-color follow-up
incorrectly answered "Black" after correctly identifying red or green. The same
result was reproduced in an unmodified native `LanguageModelSession`, including
with its own transcript, and with the existing `CGImage` adapter path. This
native recall limitation is separate from current-turn recognition and
source-level ownership/history conversion.

`DataContent.RawRepresentation` and `UriContent.RawRepresentation` can optionally
hold a native `CGImage`, `CoreImage.CIImage`, `CoreVideo.CVPixelBuffer`, `UIImage`,
or `NSImage` on the corresponding Apple platform:

```csharp
var image = new DataContent(pngBytes, "image/png")
{
    RawRepresentation = cgImage
};
```

The Apple adapter prefers the native image handle, while the encoded bytes
remain usable by recordings and other providers. `UIImage` orientation and
encoded EXIF orientation are passed through the bridge.
`UIImage` backed only by `CIImage` uses the same orientation mapping.
Core Image and pixel-buffer inputs use Foundation Models' direct native
attachment constructors, without rendering or encoding intermediates.
Swift strongly retains the source objects across asynchronous generation and
history, without disposing the caller's borrowed objects or keeping a CPU pixel
lock during inference. Keep the source valid until the request has been
submitted; do not mutate pixel-buffer storage concurrently with inference.
The binding uses an optional `CGImagePropertyOrientation`: `nil` means use the
image's natural orientation, while explicit values match EXIF orientations 1–8.
There is no separate `AppleImage` helper or new public M.E.AI content type.

## How the native bridge handles history

`AppleIntelligenceChatClient.ToNative` maps the supported `AIContent` to
`ImageContentNative`, which holds a `CGImage`, `CIImage`, `CVPixelBuffer`,
encoded data, or a file URL.
`ImageContentNative.toAttachment()` produces the Foundation Models prompt
attachment. The current user turn combines ordered text and image fragments
in a `Prompt`; earlier user, assistant, and system messages use
`Transcript.AttachmentSegment`. Tool calls retain their existing ordering
relative to assistant content.

Image-bearing history must be represented as native transcript attachments,
not text placeholders: otherwise a follow-up question cannot refer to the
prior image. The reverse conversion reads `Transcript.ImageAttachment` into
M.E.AI `DataContent` with portable PNG bytes and a native `CGImage` handle.
When an attachment has EXIF orientation, its pixels are normalized before
returning either representation so recordings and subsequent requests agree.
Portable reverse conversion of Core Image or pixel-buffer holders renders a
`CGImage` and encodes PNG; that output path is not zero-copy and does not
silently drop native-only payloads. Input and history conversion remain native.

Unsupported content and unavailable image APIs produce explicit errors. On OS
27, a request containing images also checks the model's `.vision` capability.
This bridge does **not** implement `IImageGenerator` or Apple Image Playground;
the Chat Playground's Azure image generation is a separate feature. Apple and
Azure chat use the same existing `Microsoft.Extensions.AI` logging and
telemetry middleware, with sensitive payload capture disabled; it does not
add custom image metadata to logs.

## Try it and validate

Run the [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md),
select **Apple Intelligence**, choose **Add attachment > Use sample image**,
and ask what is depicted. A follow-up about the same image exercises
transcript-history conversion. If the model is unavailable or lacks vision,
the request fails rather than silently falling back to Azure.

The Mac Catalyst device-test project includes model-free conversion and
validation tests, including OS 26 rejection cases:

```bash
dotnet build tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-maccatalyst -p:TargetFrameworks=net10.0-maccatalyst -p:ValidateXcodeVersion=false
dotnet msbuild tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -t:_DeviceRunnersRunTests -p:TargetFramework=net10.0-maccatalyst \
  -p:TargetFrameworks=net10.0-maccatalyst -p:ValidateXcodeVersion=false \
  -p:VSTestLogger=trx -p:DeviceRunnersDataTimeout=600 \
  '-p:VSTestTestCaseFilter=FullyQualifiedName~AppleIntelligenceChatClientImageTests&RequiresModel!=true'
```

The `RequiresModel=true` cases send actual images to `SystemLanguageModel.default`
and are excluded from CI. They cover red Core Image and green pixel-buffer input
in both response modes, including disposing the managed source immediately after
submission. History conversion is tested separately; it does not establish
successful model recall. All seven live cases passed without skips on a macOS
27.0.1 host with a ready default system model, including the existing `CGImage`
recognition case. Model readiness varies by device;
the missing-vision error still requires a device whose model lacks `.vision`.
Compiling against the Xcode 27 SDK does not force the OS 26 availability
branch: `#available` checks the OS at runtime. All 42 model-free image cases
passed on an isolated iOS 26.5 simulator using binaries built against the
27 SDK and normal 26.x reference packs. This includes 12 actual rejection
cases: four existing image paths and eight Core Image/pixel-buffer combinations
covering current prompts, history, and both response modes:

```bash
# Boot an iOS 26 simulator first; use its UDID in place of <simulator-udid>.
dotnet build tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -f net10.0-ios -p:TargetFrameworks=net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 -p:ValidateXcodeVersion=false
dotnet msbuild tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj \
  -t:_DeviceRunnersRunTests -p:TargetFramework=net10.0-ios -p:TargetFrameworks=net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 -p:ValidateXcodeVersion=false \
  -p:DeviceRunnersDevice=<simulator-udid> \
  -p:VSTestLogger=trx \
  '-p:VSTestTestCaseFilter=FullyQualifiedName~AppleIntelligenceChatClientImageTests&FullyQualifiedName~OnOlderOS_ReportsUnsupported'
```

The standalone Swift regression uses the production native sources, a macOS 26
deployment target, and Xcode 27. On an OS 27 host it tests native prompt and
transcript attachments for both source types and all eight EXIF orientations,
including accessing transcript pixels after the holder and source scope ends.
It launches no model and is not an OS 26 runtime rejection test:

```bash
mkdir -p artifacts/ai-image-tests/module-cache
xcrun --sdk macosx swiftc -parse-as-library \
  -target "$(uname -m)-apple-macos26.0" \
  -module-cache-path artifacts/ai-image-tests/module-cache \
  src/AI/AppleNative/EssentialsAI/*.swift \
  tests/AI/AppleNative/NativeImageInputTests.swift \
  -o artifacts/ai-image-tests/native-image-tests
artifacts/ai-image-tests/native-image-tests
```

## API references

- [Foundation Models](https://developer.apple.com/documentation/foundationmodels)
- [Analyzing images with multimodal prompting](https://developer.apple.com/documentation/foundationmodels/analyzing-images-with-multimodal-prompting)
- [`Attachment`](https://developer.apple.com/documentation/foundationmodels/attachment)
- [`Transcript`](https://developer.apple.com/documentation/foundationmodels/transcript)
- [`SystemLanguageModel`](https://developer.apple.com/documentation/foundationmodels/systemlanguagemodel)
- [`Microsoft.Extensions.AI` abstractions](https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.Abstractions)
