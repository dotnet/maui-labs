import Foundation

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(VisionDocumentOptionsNative)
public class VisionDocumentOptionsNative: NSObject {
    @objc public var recognitionLanguages: [String]?
    @objc public var customWords: [String]?
    @objc public var useLanguageCorrection: NSNumber?
    @objc public var automaticallyDetectLanguage: NSNumber?
    @objc public var maximumCandidateCount: NSNumber?
    @objc public var minimumTextHeightFraction: NSNumber?
    @objc public var barcodeDetectionEnabled: NSNumber?
    @objc public var barcodeSymbologies: [String]?
    @objc public var coalesceCompositeSymbologies: NSNumber?
    @objc public var regionOfInterest: [NSNumber]?
    @objc public var revision: NSNumber?
}
