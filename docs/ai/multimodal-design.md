# Apple Foundation Models image input

`AppleIntelligenceChatClient` accepts images through the existing
`Microsoft.Extensions.AI` (`M.E.AI`) chat abstractions. This is **image input**
for on-device analysis, not image generation. The implementation is in
`src/AI/AppleNative/EssentialsAI/` and
`src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS/AppleIntelligenceChatClient.cs`.
Live vision inference still requires validation on an OS 27 device with a ready,
vision-capable model; compiling against the SDK does not establish that result.

## Availability and toolchain

Foundation Models' `Attachment<ImageAttachmentContent>` and
`Transcript.Segment.attachment` are present in the Xcode 27 SDK, but not the
Xcode 26 SDK. The Swift shim must therefore **compile with Xcode 27**, even
though its existing text-chat path remains available on eligible OS 26 devices.
Image requests require iOS, Mac Catalyst, or macOS 27 or later **and**
`SystemLanguageModel.default.capabilities.contains(.vision)`. The Swift bridge
reports unsupported image requests explicitly; it does not silently turn
images into text or fall back to a cloud provider.

The native build jobs use the Xcode 27 runner and .NET 10 workload `10.0.401`.
Unsuffixed Apple target frameworks in that workload use 26.5 reference packs,
so those Xcode 27 builds pass `ValidateXcodeVersion=false`; device tests opt
into the 27.0 Mac Catalyst target framework instead. For a local preview build,
select Xcode 27 for just the command:

```bash
DEVELOPER_DIR=/Applications/Xcode-27.0.0.app/Contents/Developer \
  dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj \
  -f net10.0-maccatalyst27.0 -p:UseXcode27Preview=true -p:ValidateXcodeVersion=false
```

## M.E.AI input

Pass an image in `ChatMessage.Contents` as `DataContent` with an `image/*`
media type, or as `UriContent` pointing to a **local file**. Remote image URLs
are not downloaded and throw `NotSupportedException`. Non-image data is not
silently accepted as an image.

```csharp
var message = new ChatMessage(ChatRole.User, [
    new TextContent("Describe this image."),
    new DataContent(pngBytes, "image/png")
]);

var response = await client.GetResponseAsync([message]);
```

`AIContent.RawRepresentation` can also hold a native `CGImage`, `UIImage`, or
`NSImage` on the corresponding Apple platform:

```csharp
var image = new DataContent(pngBytes, "image/png")
{
    RawRepresentation = cgImage
};
```

The Apple adapter prefers the native image for the current request, avoiding
an unnecessary managed byte decode; the encoded bytes remain usable by
providers and recordings that do not understand the native handle. `UIImage`
orientation and encoded EXIF orientation are passed through the bridge.
There is no separate `AppleImage` helper or new public M.E.AI content type.

## Native conversion and transcript history

`AppleIntelligenceChatClient.ToNative` maps the supported `AIContent` to
`ImageContentNative`, which holds a `CGImage`, encoded data, or a file URL.
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

Unsupported content and unavailable image APIs produce explicit errors. A
request with image content on OS 27 also checks the model's `.vision`
capability, since OS availability alone does not guarantee image understanding.
This bridge does **not** implement `IImageGenerator` or Apple Image Playground;
the playground's Azure image generation is a separate feature.

## Validation boundary

Model-free Apple device tests cover bytes, native handles, local-file URLs,
orientation, image read-back, OS 26 rejection of image-bearing history,
streaming failure paths, and unsupported input. A separately gated test sends
an image to a real model; it requires an OS 27 device with the vision model
installed and is not part of CI. The playground's manual image-input checklist
is in `samples/AIExtensions.Sample.ChatPlayground/README.md`. Neither a successful
Xcode 27 build nor an advertised device capability proves live recognition.

## API references

- [Foundation Models](https://developer.apple.com/documentation/foundationmodels)
- [Analyzing images with multimodal prompting](https://developer.apple.com/documentation/foundationmodels/analyzing-images-with-multimodal-prompting)
- [`Attachment`](https://developer.apple.com/documentation/foundationmodels/attachment)
- [`Transcript`](https://developer.apple.com/documentation/foundationmodels/transcript)
- [`SystemLanguageModel`](https://developer.apple.com/documentation/foundationmodels/systemlanguagemodel)
- [`Microsoft.Extensions.AI` abstractions](https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.Abstractions)
