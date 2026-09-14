import Foundation
import CoreGraphics
import ImageIO
import FoundationModels

@objc(AIContentNative)
public class AIContentNative: NSObject {}

@objc(TextContentNative)
public class TextContentNative: AIContentNative {
    @objc public init(text: String) {
        self.text = text
    }
    @objc public var text: String
}

@objc(FunctionCallContentNative)
public class FunctionCallContentNative: AIContentNative {
    @objc public var callId: String
    @objc public var name: String
    @objc public var arguments: String  // JSON string
    
    @objc public init(callId: String, name: String, arguments: String) {
        self.callId = callId
        self.name = name
        self.arguments = arguments
        super.init()
    }
}

@objc(FunctionResultContentNative)
public class FunctionResultContentNative: AIContentNative {
    @objc public var callId: String
    @objc public var name: String
    @objc public var result: String
    
    @objc public init(callId: String, name: String, result: String) {
        self.callId = callId
        self.name = name
        self.result = result
        super.init()
    }
}

@objc(ImageContentNative)
public class ImageContentNative: AIContentNative {
    /// Native image handle (fast path, zero-copy).
    @objc public var cgImage: CGImage?
    /// Encoded image bytes (png/jpeg/…), decoded to a CGImage on demand.
    @objc public var data: Data?
    /// File URL pointing at an image on disk.
    @objc public var imageURL: URL?
    @objc public var mimeType: String?
    /// EXIF orientation (1…8); 0 means unset.
    @objc public var orientationRaw: Int32 = 0
    @objc public var label: String?

    @objc public init(cgImage: CGImage, orientationRaw: Int32, label: String?) {
        self.cgImage = cgImage
        self.orientationRaw = orientationRaw
        self.label = label
        super.init()
    }

    @objc public init(data: Data, mimeType: String, orientationRaw: Int32, label: String?) {
        self.data = data
        self.mimeType = mimeType
        self.orientationRaw = orientationRaw
        self.label = label
        super.init()
    }

    @objc public init(imageURL: URL, orientationRaw: Int32, label: String?) {
        self.imageURL = imageURL
        self.orientationRaw = orientationRaw
        self.label = label
        super.init()
    }

    var orientation: CGImagePropertyOrientation? {
        orientationRaw > 0 ? CGImagePropertyOrientation(rawValue: UInt32(orientationRaw)) : nil
    }

    func resolvedCGImage() -> CGImage? {
        if let cgImage { return cgImage }
        if let data { return ImageContentNative.decodeCGImage(from: data) }
        return nil
    }

    static func decodeCGImage(from data: Data) -> CGImage? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        return CGImageSourceCreateImageAtIndex(source, 0, nil)
    }
}

@available(iOS 27.0, macOS 27.0, visionOS 27.0, *)
extension ImageContentNative {
    /// Builds a FoundationModels prompt attachment. Throws if there is no usable payload.
    func toAttachment() throws -> Attachment<ImageAttachmentContent> {
        var attachment: Attachment<ImageAttachmentContent>
        if let cgImage {
            attachment = Attachment(cgImage, orientation: orientation)
        } else if let imageURL {
            attachment = Attachment(imageURL: imageURL, orientation: orientation)
        } else if let cg = resolvedCGImage() {
            attachment = Attachment(cg, orientation: orientation)
        } else {
            throw NSError.chatError(.invalidContent, description: "Image content had no usable payload.")
        }
        if let label {
            attachment = attachment.label(label)
        }
        return attachment
    }

    /// Builds a transcript-history image attachment (for prior turns).
    func toTranscriptAttachment() throws -> Transcript.Attachment {
        if let imageURL {
            return .image(Transcript.ImageAttachment(imageURL: imageURL, orientation: orientation))
        }
        guard let cg = resolvedCGImage() else {
            throw NSError.chatError(.invalidContent, description: "Image content had no usable payload.")
        }
        return .image(Transcript.ImageAttachment(cg, orientation: orientation))
    }
}
