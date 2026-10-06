import Foundation
import ImageIO
import Vision

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(RecognizeDocumentsRequestErrorNative)
public enum RecognizeDocumentsRequestErrorNative: Int {
    case cancelled = 1
    case invalidRevision = 2
    case invalidRegionOfInterest = 3
    case unsupportedBarcodeSymbology = 4
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(RecognizeDocumentsRequestNative)
public class RecognizeDocumentsRequestNative: NSObject {
    private static let errorDomain = "RecognizeDocumentsRequestNative"

    @objc public static func capabilities() -> RecognizeDocumentsRequestCapabilitiesNative {
        let request = RecognizeDocumentsRequest()
        return RecognizeDocumentsRequestCapabilitiesNative(
            supportedRecognitionLanguages: request.supportedRecognitionLanguages
                .map(\.minimalIdentifier)
                .sorted(),
            supportedBarcodeSymbologies: request.supportedBarcodeSymbologies
                .map { RecognizeDocumentsRequestSnapshotBuilder.barcodeSymbologyName($0) }
                .sorted(),
            supportedRevisions: RecognizeDocumentsRequest.supportedRevisions.map {
                NSNumber(value: revisionNumber($0))
            }
        )
    }

    @objc public func perform(
        imageData: Data,
        orientation: Int,
        options: RecognizeDocumentsRequestOptionsNative?,
        onComplete: @escaping (RecognizeDocumentsRequestSnapshotNative?, NSError?) -> Void
    ) -> CancellationTokenNative? {
        let task = Task {
            do {
                try Task.checkCancellation()
                let request = try makeRequest(options)
                let imageOrientation = orientation > 0
                    ? CGImagePropertyOrientation(rawValue: UInt32(orientation))
                    : nil
                let observations = try await request.perform(
                    on: imageData,
                    orientation: imageOrientation
                )
                try Task.checkCancellation()

                let result = try RecognizeDocumentsRequestSnapshotNative(observations)
                try Task.checkCancellation()
                onComplete(result, nil)
            } catch is CancellationError {
                let error = makeError(
                    .cancelled,
                    description: "The document recognition request was cancelled."
                )
                onComplete(nil, error)
            } catch {
                let nativeError = Task.isCancelled
                    ? makeError(
                        .cancelled,
                        description: "The document recognition request was cancelled."
                    )
                    : error as NSError
                onComplete(nil, nativeError)
            }
        }

        return CancellationTokenNative(task: task)
    }

    private func makeRequest(
        _ options: RecognizeDocumentsRequestOptionsNative?
    ) throws -> RecognizeDocumentsRequest {
        let revision: RecognizeDocumentsRequest.Revision?
        switch options?.revision?.intValue {
        case nil:
            revision = nil
        case 1:
            revision = .revision1
        default:
            throw makeError(
                .invalidRevision,
                description: "Only RecognizeDocumentsRequest revision 1 is supported."
            )
        }

        var request = RecognizeDocumentsRequest(revision)

        if let options {
            var textOptions = request.textRecognitionOptions
            if let recognitionLanguages = options.recognitionLanguages {
                textOptions.recognitionLanguages = recognitionLanguages.map {
                    Locale.Language(identifier: $0)
                }
            }
            if let customWords = options.customWords {
                textOptions.customWords = customWords
            }
            if let useLanguageCorrection = options.useLanguageCorrection {
                textOptions.useLanguageCorrection = useLanguageCorrection.boolValue
            }
            if let automaticallyDetectLanguage = options.automaticallyDetectLanguage {
                textOptions.automaticallyDetectLanguage = automaticallyDetectLanguage.boolValue
            }
            if let maximumCandidateCount = options.maximumCandidateCount {
                textOptions.maximumCandidateCount = maximumCandidateCount.intValue
            }
            if let minimumTextHeightFraction = options.minimumTextHeightFraction {
                textOptions.minimumTextHeightFraction = minimumTextHeightFraction.floatValue
            }
            request.textRecognitionOptions = textOptions

            var barcodeOptions = request.barcodeDetectionOptions
            if let barcodeDetectionEnabled = options.barcodeDetectionEnabled {
                barcodeOptions.enabled = barcodeDetectionEnabled.boolValue
            }
            if let coalesceCompositeSymbologies = options.coalesceCompositeSymbologies {
                barcodeOptions.coalesceCompositeSymbologies =
                    coalesceCompositeSymbologies.boolValue
            }
            if let barcodeSymbologies = options.barcodeSymbologies {
                barcodeOptions.symbologies = try barcodeSymbologies.map { name in
                    guard let symbology = RecognizeDocumentsRequestSnapshotBuilder.barcodeSymbology(named: name) else {
                        throw makeError(
                            .unsupportedBarcodeSymbology,
                            description: "Unsupported barcode symbology '\(name)'."
                        )
                    }
                    return symbology
                }
            }
            request.barcodeDetectionOptions = barcodeOptions

            if let region = options.regionOfInterest {
                guard region.count == 4 else {
                    throw makeError(
                        .invalidRegionOfInterest,
                        description: "The region of interest must contain x, y, width, and height."
                    )
                }
                request.regionOfInterest = NormalizedRect(
                    x: region[0].doubleValue,
                    y: region[1].doubleValue,
                    width: region[2].doubleValue,
                    height: region[3].doubleValue
                )
            }
        }

        return request
    }

    private static func revisionNumber(
        _ revision: RecognizeDocumentsRequest.Revision
    ) -> Int {
        switch revision {
        case .revision1:
            return 1
        @unknown default:
            return -1
        }
    }

    private func makeError(
        _ code: RecognizeDocumentsRequestErrorNative,
        description: String
    ) -> NSError {
        NSError(
            domain: Self.errorDomain,
            code: code.rawValue,
            userInfo: [NSLocalizedDescriptionKey: description]
        )
    }
}
