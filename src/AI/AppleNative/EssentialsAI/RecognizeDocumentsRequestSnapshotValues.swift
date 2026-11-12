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
    ) throws -> RecognizeDocumentsRequestNodeSnapshot {
        return try RecognizeDocumentsRequestNodeSnapshot(
            kind: kind,
            path: path,
            parentPath: parentPath,
            text: text.transcript,
            polygon: polygon(text.boundingRegion),
            textAlignment: encodedCaseName(text.textAlignment),
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

    static func encodedJSON<T: Encodable>(_ value: T) throws -> Any {
        try JSONSerialization.jsonObject(
            with: JSONEncoder().encode(value),
            options: [.fragmentsAllowed]
        )
    }

    static func encodedCaseName<T: Encodable>(_ value: T) throws -> String {
        // Apple's payload-free enums encode as a single case key, e.g. {"qr": {}}.
        guard let object = try encodedJSON(value) as? [String: Any],
              object.count == 1, let name = object.keys.first else {
            throw EncodingError.invalidValue(
                value, .init(codingPath: [], debugDescription: "Expected a single encoded enum case.")
            )
        }
        return name
    }

    static func encodedCaseName<T: Encodable>(_ value: T?) throws -> String? {
        try value.map { try encodedCaseName($0) }
    }

    static func barcodeSymbology(
        named name: String
    ) throws -> BarcodeSymbology? {
        try BarcodeSymbology.allCases.first {
            try encodedCaseName($0).caseInsensitiveCompare(name) == .orderedSame
        }
    }

    static func detectedDataSnapshot(
        _ text: DocumentObservation.Container.Text
    ) throws -> [[String: Any]] {
        try text.detectedData.map { detected -> [String: Any] in
            var snapshot = try semanticDetailsSnapshot(detected.match.details)
            snapshot["polygon"] = polygon(detected.boundingRegion).map(\.doubleValue)
            snapshot["highlightStyle"] = try highlightStyleName(detected.match.preferredHighlightStyle)
            if let range = detected.match.range {
                snapshot["utf16Location"] =
                    range.lowerBound.utf16Offset(in: text.transcript)
                snapshot["utf16Length"] =
                    range.upperBound.utf16Offset(in: text.transcript)
                    - range.lowerBound.utf16Offset(in: text.transcript)
            }

            return snapshot
        }
    }

    static func semanticDetailsSnapshot(_ details: DataDetector.Match.SemanticDetails) throws -> [String: Any] {
        if #available(iOS 27.0, macCatalyst 27.0, macOS 27.0, tvOS 27.0, visionOS 27.0, *) {
            return ["details": try encodedJSON(details)]
        }
        return legacySemanticDetailsSnapshot(details)
    }

    // DataDetection's Codable conformances were introduced in OS 27.
    static func legacySemanticDetailsSnapshot(
        _ details: DataDetector.Match.SemanticDetails
    ) -> [String: Any] {
        var snapshot: [String: Any] = [:]
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
        return snapshot
    }

    private static func highlightStyleName(
        _ style: DataDetector.Match.HighlightStyle
    ) throws -> String {
        if #available(iOS 27.0, macCatalyst 27.0, macOS 27.0, tvOS 27.0, visionOS 27.0, *) {
            return try encodedCaseName(style)
        }
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
