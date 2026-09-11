# Multimodal Image Input for Microsoft.Maui.Essentials.AI — Design Spec

Status: **Draft / not yet implemented.** This spec covers adding **image input** (multimodal
prompting) to the Apple backend of `Microsoft.Maui.Essentials.AI`, mapped onto
`Microsoft.Extensions.AI` (M.E.AI) abstractions.

> Scope: **images in** only. Image *generation* (images out, via `ImagePlayground`) is a separate
> effort — it builds on today's toolchain and does not depend on anything here. It is intentionally
> out of scope for this document.

## Goal

Let callers attach images to a `ChatMessage` and have Apple's on-device model analyze them —
describe, classify, OCR/extract, compare — exposed through the existing
`AppleIntelligenceChatClient : IChatClient`. Support attaching a **real native image**
(`CGImage` / `UIImage` / `NSImage`) with zero byte round-tripping, while staying cross-platform-safe
(other platforms fall back to encoded bytes).

## TL;DR

- FoundationModels multimodal input is the `Attachment` API + a new `Transcript` attachment segment.
- **Both are `iOS/macOS 27.0` and still beta**, and are **absent from the Xcode 26.x SDK** (verified).
  So this feature requires building the Swift shim with the **Xcode 27 SDK**, gating usage with
  `if #available(… 27.0, *)`, and bumping CI's `xcode-version` to 27.x.
- Wire shape in M.E.AI is the standard `DataContent` / `UriContent` on `ChatMessage.Contents`, plus
  the `AIContent.RawRepresentation` native-handle fast path.
- Work: a new native `ImageContentNative`, its binding, three touch-points in `ChatClient.swift`
  (current prompt, history entry, history read-back), and image cases in
  `AppleIntelligenceChatClient.ToNative(AIContent)`.

---

## 1. Toolchain feasibility (verified)

Verified by grepping the `FoundationModels` `.swiftinterface` in the installed Xcode 26.6
(macOS 26.5) SDK and cross-checking Apple's docs. CI currently pins **Xcode 26.3**
(`.github/workflows/ci-essentialsai.yml`).

| Symbol | Availability | In 26.x SDK? |
|---|---|---|
| `FoundationModels.Attachment`, `ImageAttachmentContent` | `iOS/macOS 27.0`, **beta** | ❌ Not present (`grep` → not found) |
| `Transcript.Segment.attachment`, `Transcript.AttachmentSegment`, `Transcript.Attachment.image(_:)` | `iOS/macOS 27.0`, **beta** | ❌ Not present (26.x `Segment` is only `.text` / `.structure`) |

Consequence: `#available` alone is not enough — the symbols are missing from the 26.x SDK, so the
Swift shim will not compile there. The code below is written to compile under the **27.0 SDK** and
to throw a clear error at runtime below 27.0. **CI `xcode-version` must move to 27.x** (both the
`build-macos` and `device-tests-maccatalyst` jobs) when this lands. All the required symbols are
**confirmed present in the Xcode 27.0 Beta 4 SDK** (verified via the FoundationModels swiftinterface).

### Building against Xcode 27 side-by-side (no `xcode-select` switch)
Xcode 27 can be installed alongside the active 26.x. Point individual commands at it with the
`DEVELOPER_DIR` environment variable — this overrides the `xcode-select` default for just that
process:
```bash
export DEVELOPER_DIR=/Applications/Xcode-27.0.0-Beta.4.app/Contents/Developer
# e.g. build only the Apple TFMs of the native + managed library:
DEVELOPER_DIR=$DEVELOPER_DIR dotnet build src/AI/EssentialsAI.slnf -f net10.0-maccatalyst
```
Caveats when building (not just inspecting) against a beta SDK: the installed MAUI/`net10.0-*`
workload must accept the newer Xcode/SDK; you may need `<SupportedOSPlatformVersion>` / Info.plist
minimums that stay at 26.0 (deployment target) while the *SDK* is 27.0. Runtime `Attachment` use is
`#available`-gated regardless.

---

## 2. Apple API surface (from the 27.0 SDK docs)

### Prompt-time attachments (verified in Xcode 27.0 Beta 4 SDK)
```swift
struct Attachment<Content>                 // Content == ImageAttachmentContent for images
  init(_ cgImage: CGImage,           orientation: CGImagePropertyOrientation? = nil)
  init(_ ciImage: CIImage,           orientation: CGImagePropertyOrientation? = nil)
  init(_ pixelBuffer: CVPixelBuffer, orientation: CGImagePropertyOrientation? = nil)
  init(imageURL: URL,                orientation: CGImagePropertyOrientation? = nil)
  func label(_ text: String) -> Attachment<Content>
// Conforms to PromptRepresentable, InstructionsRepresentable.

let response = try await session.respond {
    "Describe this image:"
    Attachment(cgImage).label("image-0")
}
```
Note: there is **no `UIImage` / `NSImage` init** — funnel through `CGImage` (iOS: `UIImage.cgImage`;
macOS: `NSImage` → `CGImageSource`) or use `imageURL`. The framework auto-scales and color-converts;
no manual preprocessing needed.

### PromptBuilder mechanics (stable, from the 26.x SDK)
```swift
@resultBuilder struct PromptBuilder {
  static func buildBlock<each P: PromptRepresentable>(_: repeat each P) -> Prompt
  static func buildArray(_ prompts: [some PromptRepresentable]) -> Prompt
  static func buildExpression<P: PromptRepresentable>(_: P) -> P
  // + buildEither / buildOptional / buildLimitedAvailability
}
```
Strategy to build a mixed text+image prompt from a dynamic list: convert each content item to a
`Prompt` fragment (`Prompt(text)` or `Prompt { attachment }`), then combine with `buildArray` via a
`for` loop over `[Prompt]` (both `String` and `Attachment` are `PromptRepresentable`, and `Prompt`
itself is `PromptRepresentable`).

### Transcript-history attachments (verified in Xcode 27.0 Beta 4 SDK)
```swift
enum Transcript.Segment {
  case text(TextSegment)
  case structure(StructuredSegment)
  case attachment(AttachmentSegment)          // NEW in 27.0
  case custom(any CustomSegment)              // NEW in 27.0
}

struct Transcript.AttachmentSegment {
  init(id: String = UUID().uuidString, content: Transcript.Attachment, label: String? = nil)
  var content: Transcript.Attachment
  var label: String?
}

enum Transcript.Attachment { case image(Transcript.ImageAttachment) }

struct Transcript.ImageAttachment {
  init(_ cgImage: CGImage,           orientation: CGImagePropertyOrientation? = nil)
  init(_ ciImage: CIImage,           orientation: CGImagePropertyOrientation? = nil)
  init(_ pixelBuffer: CVPixelBuffer, orientation: CGImagePropertyOrientation? = nil)
  init(imageURL: URL,                orientation: CGImagePropertyOrientation? = nil)
  var cgImage: CGImage { get };  var ciImage: CIImage { get };  var url: URL? { get }
  var orientation: CGImagePropertyOrientation { get }
}
```
Prior-turn images are first-class and fully constructible: they replay as
`.attachment(AttachmentSegment(content: .image(ImageAttachment(cgImage, orientation:)), label:))`,
and read back via `imageAttachment.cgImage` / `.url` / `.orientation`.

### Capability detection & image references (verified in 27.0 SDK)
```swift
// SystemLanguageModel now advertises capabilities (27.0). Prefer this over pure OS gating —
// it reflects the actual on-device model version (a device/region may lack vision even on 27.0).
extension SystemLanguageModel /* : LanguageModel */ {
  var capabilities: LanguageModelCapabilities { get }
}
struct LanguageModelCapabilities { func contains(_ c: Capability) -> Bool }
extension LanguageModelCapabilities.Capability {
  static var vision: Self            // ← image input supported
  static var guidedGeneration: Self
  static var reasoning: Self
  static var toolCalling: Self
}

// A label you attach to a prompt image can be referenced back by the model in guided generation /
// tool calls, then resolved to the actual image:
struct ImageReference: Generable { let attachmentLabel: String; func resolve(in: Transcript) -> Transcript.ImageAttachment? }
```
So the correct gate for image input is `#available(… 27.0, *)` **and**
`model.capabilities.contains(.vision)`. And `Attachment.label(_:)` is not cosmetic — it is how the
model refers to a specific image (via `ImageReference`) when you ask it to pick among several.

Also note `Attachment` conforms to **`InstructionsRepresentable`** as well as `PromptRepresentable`,
so images may appear in system `Instructions`, not only in the user prompt (an edge case we can
support later).

---

## 3. Microsoft.Extensions.AI mapping

### Wire shape
`ChatMessage.Contents` already carries the standard multimodal shapes:
- `DataContent` — in-memory bytes + media type (`image/png`, `image/jpeg`, …), or a data URI.
- `UriContent` — a `Uri` + media type (we support `file://`; see Open Questions for remote URLs).

`AppleIntelligenceChatClient.ToNative(AIContent)` currently throws on anything that isn't
`TextContent`; we add image cases.

### Native-handle pass-through (the "attach a real CGImage/UIImage" feature)
Every `AIContent` (including `DataContent`) has:
```csharp
[JsonIgnore] public object? RawRepresentation { get; set; }
```
It exists to "store the original object from another object model." So the idiomatic pattern is:
```csharp
new DataContent(pngBytes, "image/png") { RawRepresentation = cgImage }
```
- Apple client fast-path: `if content.RawRepresentation is CGImage cg` → hand the native image
  straight to `Attachment(cg)` (zero-copy).
- Other providers ignore `RawRepresentation` (it is `[JsonIgnore]`) and use the bytes.
- Cross-platform-safe: Android has no `CGImage`; it just carries the bytes.

We also accept `UIImage` / `NSImage` in `RawRepresentation` (extract their `CGImage`). A caller who
only has bytes still works — the Swift shim decodes them to a `CGImage`.

### Where images flow in the existing pipeline
`ChatClient.swift` splits messages in `prepareSession`: the **last** user message becomes the
runtime `Prompt` (attachments via the `Prompt` builder); **earlier** messages replay as `Transcript`
entries (attachments via `Transcript.Segment.attachment`). Both paths are handled below. Chat
`options` are unchanged — images ride in messages, not options.

---

## 4. Design

1. New native content type `ImageContentNative : AIContentNative` carrying one of a `CGImage`
   (fast path), encoded `Data` + mime, or a file `URL`; plus optional EXIF `orientationRaw` and
   `label`.
2. `.NET ToNative(AIContent)` maps image `DataContent` / `UriContent` (and the native-handle fast
   path) → `ImageContentNative`.
3. Swift `ChatClient.swift`:
   - `toPrompt` → maps `ImageContentNative` to an `Attachment` in the current `Prompt`.
   - `toUserEntry` → maps `ImageContentNative` to `.attachment(AttachmentSegment(...))` for history.
   - `fromTranscriptSegment` → reads a `.attachment` segment back to an `ImageContentNative`.
4. All `Attachment` / transcript-attachment usage is gated with `if #available(… 27.0, *)`; below
   that the shim throws a clear `NSError`, surfaced on the .NET side as an `NSErrorException`.

### 4.1 Swift — new file `AppleNative/EssentialsAI/ImageContent.swift`
```swift
import Foundation
import CoreGraphics
import ImageIO
import FoundationModels

@objc(ImageContentNative)
public class ImageContentNative: AIContentNative {
    @objc public var cgImage: CGImage?      // native fast path
    @objc public var data: Data?            // encoded bytes (png/jpeg/…)
    @objc public var imageURL: URL?         // file URL
    @objc public var mimeType: String?
    @objc public var orientationRaw: Int32  // EXIF 1…8; 0 = unset
    @objc public var label: String?

    @objc public init(cgImage: CGImage, orientationRaw: Int32, label: String?) {
        self.cgImage = cgImage; self.orientationRaw = orientationRaw; self.label = label
        super.init()
    }
    @objc public init(data: Data, mimeType: String, orientationRaw: Int32, label: String?) {
        self.data = data; self.mimeType = mimeType; self.orientationRaw = orientationRaw; self.label = label
        super.init()
    }
    @objc public init(imageURL: URL, orientationRaw: Int32, label: String?) {
        self.imageURL = imageURL; self.orientationRaw = orientationRaw; self.label = label
        super.init()
    }

    var orientation: CGImagePropertyOrientation? {
        orientationRaw > 0 ? CGImagePropertyOrientation(rawValue: UInt32(orientationRaw)) : nil
    }

    func resolvedCGImage() -> CGImage? {
        if let cg = cgImage { return cg }
        if let d = data { return Self.decodeCGImage(from: d) }
        return nil
    }

    static func decodeCGImage(from data: Data) -> CGImage? {
        guard let src = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        return CGImageSourceCreateImageAtIndex(src, 0, nil)
    }
}

@available(iOS 27.0, macCatalyst 27.0, macOS 27.0, visionOS 27.0, *)
extension ImageContentNative {
    /// Builds a FoundationModels prompt attachment. Throws if there is no usable payload.
    func toAttachment() throws -> Attachment<ImageAttachmentContent> {
        var attachment: Attachment<ImageAttachmentContent>
        if let cg = cgImage {
            attachment = Attachment(cg, orientation: orientation)
        } else if let url = imageURL {
            attachment = Attachment(imageURL: url, orientation: orientation)
        } else if let cg = resolvedCGImage() {
            attachment = Attachment(cg, orientation: orientation)
        } else {
            throw NSError.chatError(.invalidContent, description: "Image content had no usable payload.")
        }
        if let label = label { attachment = attachment.label(label) }
        return attachment
    }

    /// Builds a transcript-history attachment (prior turns).
    func toTranscriptAttachment() throws -> Transcript.Attachment {
        if let url = imageURL {
            return .image(Transcript.ImageAttachment(imageURL: url, orientation: orientation))
        }
        guard let image = resolvedCGImage() else {
            throw NSError.chatError(.invalidContent, description: "Image content had no usable payload.")
        }
        return .image(Transcript.ImageAttachment(image, orientation: orientation))
    }
}
```
> `NSError.chatError(...)` is the existing helper in `ChatClient.swift`; keep it (or widen its access
> within the module).

### 4.2 Swift — modify `ChatClient.swift`

Current prompt (`toPrompt`):
```swift
private func toPrompt(message: ChatMessageNative) throws -> Prompt {
    guard message.role == .user else {
        throw NSError.chatError(.invalidRole, description: "Only user messages can be prompts. Found: \(message.role)")
    }

    // One Prompt fragment per content item, then combine.
    let fragments: [Prompt] = try message.contents.map { content in
        switch content {
        case let textContent as TextContentNative:
            return Prompt(textContent.text)

        case let imageContent as ImageContentNative:
            if #available(iOS 27.0, macCatalyst 27.0, macOS 27.0, visionOS 27.0, *) {
                let attachment = try imageContent.toAttachment()
                return Prompt { attachment }
            } else {
                throw NSError.chatError(.invalidContent,
                    description: "Image prompts require iOS/macCatalyst/macOS 27.0 or later.")
            }

        default:
            throw NSError.chatError(.invalidContent,
                description: "Unsupported content type in prompt. Found: \(type(of: content))")
        }
    }

    return Prompt {
        for fragment in fragments { fragment }   // PromptBuilder.buildArray([Prompt])
    }
}
```

History entry (`toUserEntry`):
```swift
private func toUserEntry(_ message: ChatMessageNative) throws -> Transcript.Entry {
    let segments: [Transcript.Segment] = try message.contents.map { content in
        switch content {
        case let textContent as TextContentNative:
            return .text(Transcript.TextSegment(content: textContent.text))

        case let imageContent as ImageContentNative:
            if #available(iOS 27.0, macCatalyst 27.0, macOS 27.0, visionOS 27.0, *) {
                let attachment = try imageContent.toTranscriptAttachment()
                return .attachment(Transcript.AttachmentSegment(content: attachment, label: imageContent.label))
            } else {
                throw NSError.chatError(.invalidContent, description: "Image history requires iOS/macOS 27.0+.")
            }

        default:
            throw NSError.chatError(.invalidContent,
                description: "Unsupported content type in user message: \(type(of: content))")
        }
    }
    return .prompt(Transcript.Prompt(segments: segments))
}
```

History read-back (`fromTranscriptSegment`): add an `.attachment` branch so the model's returned
transcript converts back to `ImageContentNative`:
```swift
private func fromTranscriptSegment(_ segment: Transcript.Segment) -> AIContentNative? {
    switch segment {
    case .text(let textSegment):
        return TextContentNative(text: textSegment.content)
    case .structure(let structuredSegment):
        return TextContentNative(text: structuredSegment.content.jsonString)
    case .attachment(let attachmentSegment):
        if #available(iOS 27.0, macCatalyst 27.0, macOS 27.0, visionOS 27.0, *) {
            switch attachmentSegment.content {
            case .image(let image):
                return ImageContentNative(
                    cgImage: image.cgImage,
                    orientationRaw: Int32(image.orientation.rawValue),
                    label: attachmentSegment.label)
            @unknown default:
                return nil
            }
        }
        return nil
    @unknown default:
        return nil
    }
}
```

### 4.3 Binding — add to `AppleNative/ApiDefinitions.cs`
```csharp
using CoreGraphics; // add at top

// @interface ImageContentNative : AIContentNative
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(AIContentNative))]
[DisableDefaultCtor]
[Internal]
interface ImageContentNative
{
    [NullAllowed, Export("cgImage")]
    CGImage CgImage { get; set; }

    [NullAllowed, Export("data")]
    NSData Data { get; set; }

    [NullAllowed, Export("imageURL", ArgumentSemantic.Copy)]
    NSUrl ImageUrl { get; set; }

    [NullAllowed, Export("mimeType")]
    string MimeType { get; set; }

    [Export("orientationRaw")]
    int OrientationRaw { get; set; }

    [NullAllowed, Export("label")]
    string Label { get; set; }

    [Export("initWithCgImage:orientationRaw:label:")]
    [DesignatedInitializer]
    NativeHandle Constructor(CGImage cgImage, int orientationRaw, [NullAllowed] string label);

    [Export("initWithData:mimeType:orientationRaw:label:")]
    NativeHandle Constructor(NSData data, string mimeType, int orientationRaw, [NullAllowed] string label);

    [Export("initWithImageURL:orientationRaw:label:")]
    NativeHandle Constructor(NSUrl imageURL, int orientationRaw, [NullAllowed] string label);
}
```
> The type stays `iOS 26.0` (it always compiles/instantiates). Actual `Attachment` / transcript
> usage is gated to 27.0 inside Swift; below 27.0 the shim throws a clear `NSError`.

### 4.4 .NET — modify `Platform/MaciOS/AppleIntelligenceChatClient.cs`

Add image cases to `ToNative(AIContent)`:
```csharp
private static IEnumerable<AIContentNative> ToNative(AIContent content, Dictionary<string, string>? callIdToName = null) =>
    content switch
    {
        TextContent textContent when textContent.Text is not null => [new TextContentNative(textContent.Text)],
        TextContent => Array.Empty<AIContentNative>(),

        // NEW: images (analyzed by the model on 27.0+; below that the native layer throws).
        DataContent data when IsImage(data.MediaType) => [ToImageNative(data)],
        UriContent uri  when IsImage(uri.MediaType)   => [ToImageNative(uri)],

        FunctionCallContent functionCall => /* unchanged */ ...,
        FunctionResultContent functionResult => /* unchanged */ ...,

        _ => throw new ArgumentException(
            $"The content type '{content.GetType().FullName}' is not supported by Apple Intelligence chat APIs.",
            nameof(content))
    };

private static bool IsImage(string? mediaType) =>
    mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

private static ImageContentNative ToImageNative(DataContent data)
{
    // Fast path: caller attached a native handle via RawRepresentation.
    switch (data.RawRepresentation)
    {
        case CGImage cg:
            return new ImageContentNative(cg, 0, null);
#if IOS || MACCATALYST || TVOS
        case UIKit.UIImage ui when ui.CGImage is { } uicg:
            return new ImageContentNative(uicg, 0, null);
#elif MACOS
        case AppKit.NSImage ns when ToCGImage(ns) is { } nscg:
            return new ImageContentNative(nscg, 0, null);
#endif
    }

    // Byte fallback (decoded to CGImage in Swift).
    var bytes = data.Data.ToArray();
    return new ImageContentNative(NSData.FromArray(bytes), data.MediaType ?? "image/png", 0, null);
}

private static ImageContentNative ToImageNative(UriContent uri)
{
    if (uri.RawRepresentation is CGImage cg)
        return new ImageContentNative(cg, 0, null);

    if (uri.Uri.IsFile)
        return new ImageContentNative(NSUrl.FromFilename(uri.Uri.LocalPath), 0, null);

    // v1: remote http(s) images are not fetched. Callers should pass DataContent bytes instead.
    throw new NotSupportedException(
        "Apple Intelligence image prompts require in-memory DataContent or a file:// UriContent. " +
        "Remote http(s) image URLs are not downloaded automatically.");
}
```
Helpers to add in the platform-specific file: `ToCGImage(NSImage)` (`AsTiff()` →
`CGImageSource…`) for macOS. (EXIF orientation mapping from `UIImage.Orientation` is an optional
refinement; v1 passes `0` = unset.)

### 4.5 Public convenience helper
```csharp
public static class AppleImage
{
    // Wraps a native image so it flows zero-copy to the model AND carries bytes for portability.
    public static DataContent AsAIContent(CGImage image, string mediaType = "image/png");
#if IOS || MACCATALYST || TVOS
    public static DataContent AsAIContent(UIKit.UIImage image, string mediaType = "image/png");
#elif MACOS
    public static DataContent AsAIContent(AppKit.NSImage image, string mediaType = "image/png");
#endif
}
```

### Usage
```csharp
var response = await client.GetResponseAsync(
    [new ChatMessage(ChatRole.User, [
        new TextContent("List every food item you can see."),
        AppleImage.AsAIContent(photo)          // native CGImage/UIImage, zero-copy on Apple
    ])]);

// Or portable (bytes only):
var msg = new ChatMessage(ChatRole.User, [
    new TextContent("Describe this image."),
    new DataContent(pngBytes, "image/png"),
]);
```

---

## 5. Gating, CI, packaging

- `AppleIntelligenceChatClient` stays `[SupportedOSPlatform("ios26.0")]` etc.; the chat client keeps
  working for text on 26.x. Image content requires 27.0 at runtime, enforced natively — document
  that a `NotSupportedException`-equivalent `NSErrorException` is thrown below 27.0.
- **Gate on capability, not just OS.** Before sending image content, the Swift shim should check
  `if #available(… 27.0, *), SystemLanguageModel.default.capabilities.contains(.vision)` and throw a
  clear "this model build does not support image input" error otherwise. Consider surfacing this to
  callers (e.g. an `AppleIntelligence.SupportsImageInput` bool) so apps can degrade gracefully.
- **Maintenance catch (surfaced by the 27 SDK):** `GenerationOptions(sampling:)` — which the current
  `ChatClient.swift` `prepareSession` already uses — is **deprecated in 27.0**, renamed
  `init(samplingMode:temperature:maximumResponseTokens:)`. It still compiles (a deprecation warning),
  but rename `sampling:` → `samplingMode:` when we move the build to Xcode 27.
- `.github/workflows/ci-essentialsai.yml`: bump `xcode-version` to **27.x** for `build-macos` and
  `device-tests-maccatalyst`. Without this the Swift shim will not compile (`Attachment` /
  `Transcript.Segment.attachment` are absent from the 26.x SDK).
- No new frameworks to link — `FoundationModels` is a system framework resolved by the Swift shim.
- Update `PublicAPI/**/PublicAPI.Unshipped.txt` for `AppleImage.*` (and any other new public API).
- `README.md` platform matrix: add an "Image input (27.0+)" row.

## 6. Tests

Managed-only (no model; run in CI):
- `ToNative` maps `DataContent` / `UriContent` (image mime) → `ImageContentNative` selecting the
  correct payload branch (cgImage vs data vs url).
- `RawRepresentation is CGImage / UIImage / NSImage` fast path chosen over bytes.
- Non-image `DataContent` still throws; remote http `UriContent` throws the documented error.
- `AppleImage.AsAIContent(...)` produces a `DataContent` with both bytes and
  `RawRepresentation` set.

Device tests (`RequiresModel=true`, gated to 27.0):
- Attach an image + "describe this image"; assert a non-empty response.
- Attach two images + "compare"; assert both are referenced.
- Multi-turn: send an image, then a follow-up question in the next turn that relies on it (exercises
  the `Transcript.Segment.attachment` history path).

## 7. Open questions

1. ~~`Transcript.ImageAttachment` constructor.~~ **Resolved** against the Xcode 27.0 Beta 4 SDK:
   `Transcript.ImageAttachment(_ cgImage: CGImage, orientation:)` (and `ciImage` / `pixelBuffer` /
   `imageURL` variants) exist, with `.cgImage` / `.url` / `.orientation` read-back accessors. The
   history helpers above are final.
2. **Remote image URLs.** v1 rejects http(s) `UriContent` (only in-memory bytes or `file://`). Do we
   want auto-download, or keep callers responsible for fetching bytes?
3. **EXIF orientation.** v1 passes `0` (unset). Map `UIImage.Orientation` → EXIF 1…8 as a follow-up
   if we see rotated-image issues.
4. **Extra fast-paths.** `CIImage` / `CVPixelBuffer` `RawRepresentation` fast paths (via the
   corresponding `Attachment` inits) can be added later; v1 funnels everything through `CGImage`.
5. **Vision tools.** `OCRTool` / `BarcodeReaderTool` (from the Vision framework) can be attached to a
   session for OCR/barcode workflows — a natural follow-up once image input lands.

## 8. Also in the 27.0 headers (noted, out of scope for images-in)

Surfaced while reading the FoundationModels swiftinterface — not needed for image input, but worth
tracking as future Essentials.AI work:

- **Availability & model config (already shipped in 26.x, still unused):** `SystemLanguageModel`
  `availability` / `isAvailable` / `Availability`; `init(useCase: .general, guardrails: .default)`
  with `UseCase` (`.contentTagging`) and `Guardrails`; `contextSize`, `supportedLanguages`,
  `supportsLocale(_:)`, `tokenCount(for:)`.
- **Token usage:** `LanguageModelSession.usage` (`Usage` with input/output token counts) → maps to
  M.E.AI `ChatResponse.Usage` / `UsageDetails`, which we currently don't populate.
- **Session lifecycle:** `prewarm(promptPrefix:)`, `isResponding`, `transcriptErrorHandlingPolicy`.
- **Tool-calling mode (new 27.0):** `GenerationOptions.ToolCallingMode` (`.allowed` / `.required` /
  `.disallowed`) → maps to M.E.AI `ChatOptions.ToolMode`.
- **Sampling (new 27.0):** `SamplingMode.random(probabilityThreshold:seed:)` (top-p) in addition to
  `random(top:seed:)`.
- **Context / metadata (new 27.0):** `ContextOptions` (incl. `includeSchemaInPrompt`) and a
  `metadata:` parameter on the newer `respond` / `streamResponse` overloads.
- **Feedback (new 27.0):** `LanguageModelFeedback` (`Sentiment`, `Issue.Category`) +
  `session.logFeedbackAttachment(...)`.
- **Advanced composition (new 27.0):** dynamic profiles / dynamic instructions
  (`init(profile:history:)`, `init(model:dynamicInstructions:history:)`), session properties
  (`@SessionPropertyEntry`), the pluggable `LanguageModel` protocol, and
  `PrivateCloudComputeLanguageModel` (server-side inference with quota).

## References (authoritative sources)

Everything in this doc is verified against Apple's declared API contract, not blog posts.

**Primary — the SDK `.swiftinterface` (the canonical contract we grepped):**
- `…/Xcode-27.0.0-Beta.4.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX27.0.sdk/System/Library/Frameworks/FoundationModels.framework/Versions/A/Modules/FoundationModels.swiftmodule/arm64e-apple-macos.swiftinterface`
- Inspect side-by-side without switching the active Xcode:
  `DEVELOPER_DIR=/Applications/Xcode-27.0.0-Beta.4.app/Contents/Developer xcrun swiftc -typecheck -sdk "$(xcrun --sdk macosx --show-sdk-path)" -target arm64-apple-ios26.0-macabi <files>`

**Apple Developer documentation:**
- [Foundation Models](https://developer.apple.com/documentation/foundationmodels)
- [Analyzing images with multimodal prompting](https://developer.apple.com/documentation/foundationmodels/analyzing-images-with-multimodal-prompting)
- [`Attachment`](https://developer.apple.com/documentation/foundationmodels/attachment) · [`ImageAttachmentContent`](https://developer.apple.com/documentation/foundationmodels/imageattachmentcontent) · [`ImageReference`](https://developer.apple.com/documentation/foundationmodels/imagereference)
- [`Transcript`](https://developer.apple.com/documentation/foundationmodels/transcript) → [`Segment`](https://developer.apple.com/documentation/foundationmodels/transcript/segment) / [`AttachmentSegment`](https://developer.apple.com/documentation/foundationmodels/transcript/attachmentsegment) / [`Attachment`](https://developer.apple.com/documentation/foundationmodels/transcript/attachment) / `ImageAttachment`
- [`SystemLanguageModel`](https://developer.apple.com/documentation/foundationmodels/systemlanguagemodel) · [`LanguageModelSession`](https://developer.apple.com/documentation/foundationmodels/languagemodelsession)
- [iOS & iPadOS release notes](https://developer.apple.com/documentation/ios-ipados-release-notes) (per-version model/API changes)

**Microsoft.Extensions.AI (the abstractions we map onto):** [`dotnet/extensions` — `Microsoft.Extensions.AI.Abstractions`](https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.Abstractions) (`DataContent`, `UriContent`, `AIContent.RawRepresentation`).
