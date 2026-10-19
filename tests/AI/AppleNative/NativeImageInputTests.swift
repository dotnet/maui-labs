import CoreGraphics
import CoreImage
import CoreVideo
import Foundation
import FoundationModels
import ImageIO

@main
struct NativeImageInputTests {
    static func main() async throws {
        if #available(macOS 27.0, *) {
            try await checkAttachments()
        } else {
            fatalError("Attachment conversion tests require OS 27; this is not an OS 26 runtime test.")
        }
    }

    @available(macOS 27.0, *)
    static func checkAttachments() async throws {
        var count = 0
        for pixelBuffer in [false, true] {
            for value in 1...8 {
                let orientation = CGImagePropertyOrientation(rawValue: UInt32(value))!
                let history: Transcript.Attachment = try autoreleasepool {
                    let ci = CIImage(color: CIColor(red: 1, green: 0, blue: 0))
                        .cropped(to: CGRect(x: 0, y: 0, width: 3, height: 2))
                    let holder: ImageContentNative
                    if pixelBuffer {
                        var buffer: CVPixelBuffer?
                        precondition(CVPixelBufferCreate(
                            kCFAllocatorDefault, 3, 2, kCVPixelFormatType_32BGRA,
                            nil, &buffer) == kCVReturnSuccess)
                        CIContext().render(ci, to: buffer!)
                        holder = ImageContentNative(
                            pixelBuffer: buffer!, orientationValue: NSNumber(value: value), label: nil)
                        buffer = nil
                        precondition(holder.pixelBuffer != nil && holder.ciImage == nil)
                    } else {
                        holder = ImageContentNative(
                            ciImage: ci, orientationValue: NSNumber(value: value), label: nil)
                        precondition(holder.ciImage === ci && holder.pixelBuffer == nil)
                    }
                    precondition(holder.cgImage == nil && holder.data == nil && holder.imageURL == nil)
                    let prompt = try Prompt { try holder.toAttachment() }
                    _ = prompt
                    return try holder.toTranscriptAttachment()
                }

                // Only the transcript attachment survives the holder and source scope.
                await Task.yield()
                guard case .image(let image) = history else {
                    fatalError("Native image was not preserved as a history attachment.")
                }
                precondition(image.orientation == orientation)
                let cg = image.cgImage
                precondition(cg.width == 3 && cg.height == 2)
                let bytes = cg.dataProvider!.data! as Data
                precondition(!bytes.isEmpty)
                let transcript = Transcript(entries: [
                    .prompt(Transcript.Prompt(segments: [
                        .attachment(Transcript.AttachmentSegment(content: history))
                    ]))
                ])
                precondition(transcript.count == 1)
                count += 1
            }
        }
        print("Passed \(count) model-free native image/history ownership and orientation cases.")
    }
}
