import Foundation

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(RecognizeDocumentsRequestCapabilitiesNative)
public class RecognizeDocumentsRequestCapabilitiesNative: NSObject {
    @objc public let supportedRecognitionLanguages: [String]
    @objc public let supportedBarcodeSymbologies: [String]
    @objc public let supportedRevisions: [NSNumber]

    init(
        supportedRecognitionLanguages: [String],
        supportedBarcodeSymbologies: [String],
        supportedRevisions: [NSNumber]
    ) {
        self.supportedRecognitionLanguages = supportedRecognitionLanguages
        self.supportedBarcodeSymbologies = supportedBarcodeSymbologies
        self.supportedRevisions = supportedRevisions
    }
}
