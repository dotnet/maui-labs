import AppKit
import CoreImage
import CoreImage.CIFilterBuiltins
import Foundation

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: generate-barcodes.swift <output.png>")
}

let outputUrl = URL(fileURLWithPath: CommandLine.arguments[1])
let width = 1600
let height = 1200
let context = CIContext()
guard
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: width,
        pixelsHigh: height,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: width * 4,
        bitsPerPixel: 32
    ),
    let graphicsContext = NSGraphicsContext(bitmapImageRep: bitmap)
else {
    fatalError("Unable to create barcode fixture canvas.")
}
bitmap.size = NSSize(width: width, height: height)

func makeImage(_ image: CIImage) -> NSImage {
    guard let cgImage = context.createCGImage(image, from: image.extent) else {
        fatalError("Unable to render barcode image.")
    }
    return NSImage(cgImage: cgImage, size: image.extent.size)
}

func drawText(
    _ text: String,
    at point: NSPoint,
    size: CGFloat,
    weight: NSFont.Weight = .regular
) {
    text.draw(
        at: point,
        withAttributes: [
            .font: NSFont.systemFont(ofSize: size, weight: weight),
            .foregroundColor: NSColor.black,
        ]
    )
}

let qrPayload = "https://example.com/meai/vision-corpus"
let qrFilter = CIFilter.qrCodeGenerator()
qrFilter.message = Data(qrPayload.utf8)
qrFilter.correctionLevel = "M"
guard let qrOutput = qrFilter.outputImage else {
    fatalError("Unable to generate QR code.")
}
let qrImage = makeImage(qrOutput.transformed(by: CGAffineTransform(scaleX: 14, y: 14)))

let code128Payload = "MEAI-VISION-2026"
let code128Filter = CIFilter.code128BarcodeGenerator()
code128Filter.message = Data(code128Payload.utf8)
code128Filter.quietSpace = 20
guard let code128Output = code128Filter.outputImage else {
    fatalError("Unable to generate Code 128 barcode.")
}
let code128Image = makeImage(
    code128Output.transformed(by: CGAffineTransform(scaleX: 4, y: 7))
)

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = graphicsContext
NSColor.white.setFill()
NSBezierPath(rect: NSRect(x: 0, y: 0, width: width, height: height)).fill()

drawText("BARCODE RECOGNITION", at: NSPoint(x: 100, y: 1060), size: 64, weight: .bold)
drawText("QR CODE", at: NSPoint(x: 180, y: 930), size: 48, weight: .bold)
drawText("CODE 128", at: NSPoint(x: 850, y: 930), size: 48, weight: .bold)

qrImage.draw(
    in: NSRect(x: 130, y: 300, width: 620, height: 620),
    from: .zero,
    operation: .copy,
    fraction: 1,
    respectFlipped: true,
    hints: [.interpolation: NSImageInterpolation.none]
)
code128Image.draw(
    in: NSRect(x: 830, y: 500, width: 650, height: 300),
    from: .zero,
    operation: .copy,
    fraction: 1,
    respectFlipped: true,
    hints: [.interpolation: NSImageInterpolation.none]
)

drawText(qrPayload, at: NSPoint(x: 130, y: 220), size: 28)
drawText(code128Payload, at: NSPoint(x: 940, y: 430), size: 32)
NSGraphicsContext.restoreGraphicsState()

guard
    let png = bitmap.representation(using: .png, properties: [:])
else {
    fatalError("Unable to encode barcode fixture.")
}

try png.write(to: outputUrl, options: .atomic)
