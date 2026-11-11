import Foundation
import DataDetection
import Vision

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
extension RecognizeDocumentsRequestSnapshotBuilder {
    static func textNode(
        _ text: DocumentObservation.Container.Text,
        kind: RecognizeDocumentsRequestNodeKind,
        path: String,
        parentPath: String?
    ) -> RecognizeDocumentsRequestNodeSnapshot {
        return RecognizeDocumentsRequestNodeSnapshot(
            kind: kind,
            path: path,
            parentPath: parentPath,
            text: text.transcript,
            polygon: polygon(text.boundingRegion),
            textAlignment: alignmentName(text.textAlignment),
            recognitionLanguages: recognitionLanguages(text),
            detectedData: detectedDataSnapshot(text),
            candidates: candidatesSnapshot(text),
            words: wordsSnapshot(text)
        )
    }

    static func recognitionLanguages(
        _ text: DocumentObservation.Container.Text
    ) -> [String]? {
        let languages = Array(Set(text.lines.flatMap {
            $0.recognitionLanguages.map(\.minimalIdentifier)
        })).sorted()
        return languages.isEmpty ? nil : languages
    }

    static func candidatesSnapshot(
        _ text: DocumentObservation.Container.Text
    ) -> [[String: Any]] {
        text.lines.map { line in
            [
                "polygon": polygon(line.boundingRegion).map(\.doubleValue),
                "recognitionLanguages":
                    line.recognitionLanguages.map(\.minimalIdentifier),
                "candidates": line.topCandidates(10).map { candidate in
                    [
                        "text": candidate.string,
                        "confidence": Double(candidate.confidence),
                    ] as [String: Any]
                },
            ] as [String: Any]
        }
    }

    static func wordsSnapshot(
        _ text: DocumentObservation.Container.Text
    ) -> [[String: Any]]? {
        guard let words = text.words else {
            return nil
        }
        return words.map { word in
            [
                "polygon": polygon(word.boundingRegion).map(\.doubleValue),
                "recognitionLanguages":
                    word.recognitionLanguages.map(\.minimalIdentifier),
                "candidates": word.topCandidates(10).map { candidate in
                    [
                        "text": candidate.string,
                        "confidence": Double(candidate.confidence),
                    ] as [String: Any]
                },
            ] as [String: Any]
        }
    }

    static func polygon(_ region: NormalizedRegion) -> [NSNumber] {
        return region.points.flatMap {
            [NSNumber(value: Double($0.x)), NSNumber(value: Double($0.y))]
        }
    }

    static func alignmentName(
        _ alignment: DocumentObservation.Container.Text.Alignment?
    ) -> String? {
        switch alignment {
        case .center:
            return "center"
        case .leading:
            return "leading"
        case .trailing:
            return "trailing"
        case nil:
            return nil
        @unknown default:
            return String(describing: alignment)
        }
    }

    static func markerName(
        _ marker: DocumentObservation.Container.List.Marker?
    ) -> String? {
        switch marker {
        case .bullet:
            return "bullet"
        case .hyphen:
            return "hyphen"
        case .lowercaseLatin:
            return "lowercaseLatin"
        case .uppercaseLatin:
            return "uppercaseLatin"
        case .decimal:
            return "decimal"
        case .decorativeDecimal:
            return "decorativeDecimal"
        case .compositeDecimal:
            return "compositeDecimal"
        case nil:
            return nil
        @unknown default:
            return String(describing: marker)
        }
    }

    static func compositeTypeName(
        _ compositeType: BarcodeObservation.CompositeType?
    ) -> String? {
        switch compositeType {
        case .gs1TypeA:
            return "gs1TypeA"
        case .gs1TypeB:
            return "gs1TypeB"
        case .gs1TypeC:
            return "gs1TypeC"
        case .linked:
            return "linked"
        case nil:
            return nil
        @unknown default:
            return String(describing: compositeType)
        }
    }

    static func barcodeSymbologyName(_ symbology: BarcodeSymbology) -> String {
        switch symbology {
        case .aztec: return "aztec"
        case .code39: return "code39"
        case .code39Checksum: return "code39Checksum"
        case .code39FullASCII: return "code39FullASCII"
        case .code39FullASCIIChecksum: return "code39FullASCIIChecksum"
        case .code93: return "code93"
        case .code93i: return "code93i"
        case .code128: return "code128"
        case .dataMatrix: return "dataMatrix"
        case .ean8: return "ean8"
        case .ean13: return "ean13"
        case .i2of5: return "i2of5"
        case .i2of5Checksum: return "i2of5Checksum"
        case .itf14: return "itf14"
        case .pdf417: return "pdf417"
        case .qr: return "qr"
        case .upce: return "upce"
        case .codabar: return "codabar"
        case .gs1DataBar: return "gs1DataBar"
        case .gs1DataBarExpanded: return "gs1DataBarExpanded"
        case .gs1DataBarLimited: return "gs1DataBarLimited"
        case .microPDF417: return "microPDF417"
        case .microQR: return "microQR"
        case .msiPlessey: return "msiPlessey"
        @unknown default: return String(describing: symbology)
        }
    }

    static func barcodeSymbology(
        named name: String
    ) -> BarcodeSymbology? {
        BarcodeSymbology.allCases.first {
            barcodeSymbologyName($0).caseInsensitiveCompare(name) == .orderedSame
        }
    }

    static func detectedDataSnapshot(
        _ text: DocumentObservation.Container.Text
    ) -> [[String: Any]] {
        text.detectedData.map { detected -> [String: Any] in
            var snapshot: [String: Any] = [
                "polygon": polygon(detected.boundingRegion).map(\.doubleValue),
                "highlightStyle": highlightStyleName(
                    detected.match.preferredHighlightStyle
                ),
            ]
            if let range = detected.match.range {
                snapshot["utf16Location"] =
                    range.lowerBound.utf16Offset(in: text.transcript)
                snapshot["utf16Length"] =
                    range.upperBound.utf16Offset(in: text.transcript)
                    - range.lowerBound.utf16Offset(in: text.transcript)
            }

            addSemanticDetails(detected.match.details, to: &snapshot)
            return snapshot
        }
    }

    private static func addSemanticDetails(
        _ details: DataDetector.Match.SemanticDetails,
        to snapshot: inout [String: Any]
    ) {
        switch details {
        case .link(let link):
            snapshot["type"] = "link"
            snapshot["url"] = link.url.absoluteString
        case .emailAddress(let email):
            snapshot["type"] = "emailAddress"
            snapshot["value"] = email.emailAddress
            snapshot["label"] = email.label
        case .phoneNumber(let phone):
            snapshot["type"] = "phoneNumber"
            snapshot["value"] = phone.phoneNumber
            snapshot["label"] = phone.label
        case .postalAddress(let address):
            snapshot["type"] = "postalAddress"
            snapshot["value"] = address.fullAddress
            snapshot["street"] = address.street
            snapshot["city"] = address.city
            snapshot["state"] = address.state
            snapshot["postalCode"] = address.postalCode
            snapshot["region"] = address.region
            snapshot["regionCode"] = address.regionCode?.identifier
            snapshot["label"] = address.label
        case .calendarEvent(let event):
            snapshot["type"] = "calendarEvent"
            snapshot["allDay"] = event.allDay
            snapshot["startDate"] = event.startDate.map(iso8601)
            snapshot["startTimeZone"] = event.startTimeZone?.identifier
            snapshot["endDate"] = event.endDate.map(iso8601)
            snapshot["endTimeZone"] = event.endTimeZone?.identifier
        case .moneyAmount(let money):
            snapshot["type"] = "moneyAmount"
            snapshot["currency"] = money.currency.identifier
            snapshot["amount"] = NSDecimalNumber(decimal: money.amount).stringValue
        case .flightNumber(let flight):
            snapshot["type"] = "flightNumber"
            snapshot["airlineCode"] = flight.airlineCode
            snapshot["flightNumber"] = flight.flightNumber
        case .shipmentTrackingNumber(let shipment):
            snapshot["type"] = "shipmentTrackingNumber"
            snapshot["carrier"] = shipment.carrier
            snapshot["trackingNumber"] = shipment.trackingNumber
            snapshot["trackingURL"] = shipment.trackingURL?.absoluteString
        case .measurement(let measurement):
            snapshot["type"] = "measurement"
            snapshot["value"] = measurement.value
            snapshot["possibleDimensions"] =
                measurement.possibleDimensions.map { String(describing: $0) }
        case .paymentIdentifier(let payment):
            snapshot["type"] = "paymentIdentifier"
            snapshot["identifier"] = payment.identifier
            snapshot["paymentSystem"] = paymentSystemName(payment.type)
        @unknown default:
            snapshot["type"] = "unknown"
            snapshot["value"] = String(describing: details)
        }
    }

    private static func highlightStyleName(
        _ style: DataDetector.Match.HighlightStyle
    ) -> String {
        switch style {
        case .hidden: return "hidden"
        case .url: return "url"
        case .regular: return "regular"
        @unknown default: return "unknown"
        }
    }

    private static func paymentSystemName(
        _ system: DataDetector.Match.SemanticDetails.PaymentIdentifier.PaymentSystem
    ) -> String {
        switch system {
        case .unifiedPaymentsInterface:
            return "unifiedPaymentsInterface"
        @unknown default:
            return "unknown"
        }
    }

    private static func iso8601(_ date: Date) -> String {
        ISO8601DateFormatter().string(from: date)
    }
}
