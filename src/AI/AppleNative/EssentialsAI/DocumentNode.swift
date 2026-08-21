import Foundation
import DataDetection
import Vision

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(VisionDocumentNodeKindNative)
public enum VisionDocumentNodeKindNative: Int {
    case title = 0
    case paragraph = 1
    case table = 2
    case tableCell = 3
    case list = 4
    case listItem = 5
    case barcode = 6
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(VisionDocumentCapabilitiesNative)
public class VisionDocumentCapabilitiesNative: NSObject {
    @objc public let recognitionLanguages: [String]
    @objc public let barcodeSymbologies: [String]
    @objc public let revisions: [NSNumber]

    init(
        recognitionLanguages: [String],
        barcodeSymbologies: [String],
        revisions: [NSNumber]
    ) {
        self.recognitionLanguages = recognitionLanguages
        self.barcodeSymbologies = barcodeSymbologies
        self.revisions = revisions
    }
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(VisionDocumentNodeNative)
public class VisionDocumentNodeNative: NSObject {
    @objc public let kind: VisionDocumentNodeKindNative
    @objc public let path: String
    @objc public let parentPath: String?
    @objc public let text: String?
    @objc public let polygon: [NSNumber]
    @objc public let confidence: NSNumber?
    @objc public let rowIndex: NSNumber?
    @objc public let columnIndex: NSNumber?
    @objc public let rowSpan: NSNumber?
    @objc public let columnSpan: NSNumber?
    @objc public let itemString: String?
    @objc public let markerString: String?
    @objc public let markerType: String?
    @objc public let symbology: String?
    @objc public let payloadString: String?
    @objc public let payloadData: Data?
    @objc public let isGS1DataCarrier: NSNumber?
    @objc public let isColorInverted: NSNumber?
    @objc public let supplementalPayloadString: String?
    @objc public let supplementalPayloadData: Data?
    @objc public let supplementalCompositeType: String?
    @objc public let textAlignment: String?
    @objc public let recognitionLanguages: [String]?
    @objc public let detectedDataJson: Data?
    @objc public let candidatesJson: Data?

    private let rangeProvider: ((Int, Int) -> [NSNumber]?)?
    private let jsonLock = NSLock()
    private var cachedJson: Data?

    init(
        kind: VisionDocumentNodeKindNative,
        path: String,
        parentPath: String?,
        text: String? = nil,
        polygon: [NSNumber] = [],
        confidence: NSNumber? = nil,
        rowIndex: NSNumber? = nil,
        columnIndex: NSNumber? = nil,
        rowSpan: NSNumber? = nil,
        columnSpan: NSNumber? = nil,
        itemString: String? = nil,
        markerString: String? = nil,
        markerType: String? = nil,
        symbology: String? = nil,
        payloadString: String? = nil,
        payloadData: Data? = nil,
        isGS1DataCarrier: NSNumber? = nil,
        isColorInverted: NSNumber? = nil,
        supplementalPayloadString: String? = nil,
        supplementalPayloadData: Data? = nil,
        supplementalCompositeType: String? = nil,
        textAlignment: String? = nil,
        recognitionLanguages: [String]? = nil,
        detectedDataJson: Data? = nil,
        candidatesJson: Data? = nil,
        rangeProvider: ((Int, Int) -> [NSNumber]?)? = nil
    ) {
        self.kind = kind
        self.path = path
        self.parentPath = parentPath
        self.text = text
        self.polygon = polygon
        self.confidence = confidence
        self.rowIndex = rowIndex
        self.columnIndex = columnIndex
        self.rowSpan = rowSpan
        self.columnSpan = columnSpan
        self.itemString = itemString
        self.markerString = markerString
        self.markerType = markerType
        self.symbology = symbology
        self.payloadString = payloadString
        self.payloadData = payloadData
        self.isGS1DataCarrier = isGS1DataCarrier
        self.isColorInverted = isColorInverted
        self.supplementalPayloadString = supplementalPayloadString
        self.supplementalPayloadData = supplementalPayloadData
        self.supplementalCompositeType = supplementalCompositeType
        self.textAlignment = textAlignment
        self.recognitionLanguages = recognitionLanguages
        self.detectedDataJson = detectedDataJson
        self.candidatesJson = candidatesJson
        self.rangeProvider = rangeProvider
    }

    @objc public func encodeJson(
        _ errorPointer: AutoreleasingUnsafeMutablePointer<NSError?>?
    ) -> Data? {
        jsonLock.lock()
        defer { jsonLock.unlock() }

        if let cachedJson {
            return cachedJson
        }

        do {
            let json = try JSONSerialization.data(
                withJSONObject: snapshotObject(),
                options: [.sortedKeys]
            )
            cachedJson = json
            return json
        } catch {
            errorPointer?.pointee = error as NSError
            return nil
        }
    }

    @objc public var jsonData: Data? {
        encodeJson(nil)
    }

    @objc public func boundingRegion(
        forUtf16Location location: Int,
        length: Int
    ) -> [NSNumber]? {
        rangeProvider?(location, length)
    }

    func snapshotObject() -> [String: Any] {
        var snapshot: [String: Any] = [
            "kind": kindName,
            "path": path,
            "polygon": polygon.map(\.doubleValue),
        ]
        snapshot["parentPath"] = parentPath
        snapshot["text"] = text
        snapshot["confidence"] = confidence
        snapshot["rowIndex"] = rowIndex
        snapshot["columnIndex"] = columnIndex
        snapshot["rowSpan"] = rowSpan
        snapshot["columnSpan"] = columnSpan
        snapshot["itemString"] = itemString
        snapshot["markerString"] = markerString
        snapshot["markerType"] = markerType
        snapshot["symbology"] = symbology
        snapshot["payloadString"] = payloadString
        snapshot["payloadDataBase64"] = payloadData?.base64EncodedString()
        snapshot["isGS1DataCarrier"] = isGS1DataCarrier
        snapshot["isColorInverted"] = isColorInverted
        snapshot["supplementalPayloadString"] = supplementalPayloadString
        snapshot["supplementalPayloadDataBase64"] =
            supplementalPayloadData?.base64EncodedString()
        snapshot["supplementalCompositeType"] = supplementalCompositeType
        snapshot["textAlignment"] = textAlignment
        snapshot["recognitionLanguages"] = recognitionLanguages
        snapshot["detectedData"] = jsonObject(detectedDataJson)
        snapshot["candidates"] = jsonObject(candidatesJson)
        return snapshot
    }

    private var kindName: String {
        switch kind {
        case .title: return "title"
        case .paragraph: return "paragraph"
        case .table: return "table"
        case .tableCell: return "tableCell"
        case .list: return "list"
        case .listItem: return "listItem"
        case .barcode: return "barcode"
        @unknown default: return "unknown"
        }
    }

    private func jsonObject(_ data: Data?) -> Any? {
        guard let data else {
            return nil
        }
        return try? JSONSerialization.jsonObject(with: data)
    }
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(VisionDocumentObservationNative)
public class VisionDocumentObservationNative: NSObject {
    let observation: DocumentObservation

    @objc public let uuidString: String
    @objc public let confidence: Float
    @objc public let transcript: String
    @objc public let nodes: [VisionDocumentNodeNative]
    @objc public let structureTruncated: Bool
    @objc public let projectedNodeCount: Int
    @objc public let maximumTraversalDepth: Int
    @objc public let repeatedContainerCount: Int
    @objc public let firstRepeatedContainerPath: String?
    @objc public let firstRepeatedAncestorPath: String?

    private let jsonLock = NSLock()
    private var cachedJson: Data?

    init(_ observation: DocumentObservation) {
        self.observation = observation
        self.uuidString = observation.uuid.uuidString
        self.confidence = observation.confidence
        self.transcript = observation.document.text.transcript
        let buildResult = VisionDocumentNodeBuilder.build(
            observation.document,
            rootPath: "observations/\(observation.uuid.uuidString)/document"
        )
        self.nodes = buildResult.nodes
        self.structureTruncated = buildResult.truncated
        self.projectedNodeCount = buildResult.nodes.count
        self.maximumTraversalDepth = buildResult.maximumDepth
        self.repeatedContainerCount = buildResult.repeatedContainerCount
        self.firstRepeatedContainerPath = buildResult.firstRepeatedContainerPath
        self.firstRepeatedAncestorPath = buildResult.firstRepeatedAncestorPath
    }

    @objc public func encodeJson(
        _ errorPointer: AutoreleasingUnsafeMutablePointer<NSError?>?
    ) -> Data? {
        jsonLock.lock()
        defer { jsonLock.unlock() }

        if let cachedJson {
            return cachedJson
        }

        do {
            var snapshot: [String: Any] = [
                "uuid": uuidString,
                "confidence": Double(confidence),
                "transcript": transcript,
                "structureTruncated": structureTruncated,
                "projectedNodeCount": projectedNodeCount,
                "maximumTraversalDepth": maximumTraversalDepth,
                "repeatedContainerCount": repeatedContainerCount,
                "nodes": nodes.map { $0.snapshotObject() },
            ]
            snapshot["firstRepeatedContainerPath"] = firstRepeatedContainerPath
            snapshot["firstRepeatedAncestorPath"] = firstRepeatedAncestorPath
            let json = try JSONSerialization.data(
                withJSONObject: snapshot,
                options: [.sortedKeys]
            )
            cachedJson = json
            return json
        } catch {
            errorPointer?.pointee = error as NSError
            return nil
        }
    }

    @objc public var jsonData: Data? {
        encodeJson(nil)
    }
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(VisionDocumentResultNative)
public class VisionDocumentResultNative: NSObject, @unchecked Sendable {
    @objc public let observations: [VisionDocumentObservationNative]

    init(_ observations: [DocumentObservation]) {
        self.observations = observations.map(VisionDocumentObservationNative.init)
    }
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
enum VisionDocumentNodeBuilder {
    private static let maximumDepth = 64
    private static let maximumNodeCount = 20_000

    private struct ContainerFingerprint: Hashable {
        let transcript: String
        let title: String?
        let polygon: [Int]
        let paragraphCount: Int
        let tableCount: Int
        let listCount: Int
        let barcodeCount: Int
    }

    static func build(
        _ container: DocumentObservation.Container,
        rootPath: String
    ) -> (
        nodes: [VisionDocumentNodeNative],
        truncated: Bool,
        maximumDepth: Int,
        repeatedContainerCount: Int,
        firstRepeatedContainerPath: String?,
        firstRepeatedAncestorPath: String?
    ) {
        var nodes: [VisionDocumentNodeNative] = []
        var truncated = false
        var maximumDepthReached = 0
        var activeContainers: [ContainerFingerprint: String] = [:]
        var repeatedContainerCount = 0
        var firstRepeatedContainerPath: String?
        var firstRepeatedAncestorPath: String?
        append(
            container,
            path: rootPath,
            parentPath: nil,
            depth: 0,
            to: &nodes,
            truncated: &truncated,
            maximumDepthReached: &maximumDepthReached,
            activeContainers: &activeContainers,
            repeatedContainerCount: &repeatedContainerCount,
            firstRepeatedContainerPath: &firstRepeatedContainerPath,
            firstRepeatedAncestorPath: &firstRepeatedAncestorPath
        )
        return (
            nodes,
            truncated,
            maximumDepthReached,
            repeatedContainerCount,
            firstRepeatedContainerPath,
            firstRepeatedAncestorPath
        )
    }

    private static func append(
        _ container: DocumentObservation.Container,
        path: String,
        parentPath: String?,
        depth: Int,
        to nodes: inout [VisionDocumentNodeNative],
        truncated: inout Bool,
        maximumDepthReached: inout Int,
        activeContainers: inout [ContainerFingerprint: String],
        repeatedContainerCount: inout Int,
        firstRepeatedContainerPath: inout String?,
        firstRepeatedAncestorPath: inout String?
    ) {
        maximumDepthReached = max(maximumDepthReached, depth)
        guard depth <= maximumDepth, nodes.count < maximumNodeCount else {
            truncated = true
            return
        }

        let fingerprint = fingerprint(container)
        if let ancestorPath = activeContainers[fingerprint] {
            truncated = true
            repeatedContainerCount += 1
            if firstRepeatedContainerPath == nil {
                firstRepeatedContainerPath = path
                firstRepeatedAncestorPath = ancestorPath
            }
            return
        }

        activeContainers[fingerprint] = path
        defer {
            activeContainers.removeValue(forKey: fingerprint)
        }

        if let title = container.title {
            guard nodes.count < maximumNodeCount else {
                truncated = true
                return
            }
            let nodePath = "\(path)/title"
            nodes.append(textNode(
                title,
                kind: .title,
                path: nodePath,
                parentPath: parentPath
            ))
        }

        for (index, paragraph) in container.paragraphs.enumerated() {
            guard nodes.count < maximumNodeCount else {
                truncated = true
                return
            }
            let nodePath = "\(path)/paragraphs/\(index)"
            nodes.append(textNode(
                paragraph,
                kind: .paragraph,
                path: nodePath,
                parentPath: parentPath
            ))
        }

        for (index, table) in container.tables.enumerated() {
            guard nodes.count < maximumNodeCount else {
                truncated = true
                return
            }
            let tablePath = "\(path)/tables/\(index)"
            nodes.append(VisionDocumentNodeNative(
                kind: .table,
                path: tablePath,
                parentPath: parentPath,
                polygon: polygon(table.boundingRegion)
            ))

            var seenCells = Set<String>()
            for row in table.rows {
                for cell in row {
                    guard nodes.count < maximumNodeCount else {
                        truncated = true
                        return
                    }
                    let cellKey = "\(cell.rowRange.lowerBound):\(cell.columnRange.lowerBound)"
                    guard seenCells.insert(cellKey).inserted else {
                        continue
                    }

                    let cellPath = "\(tablePath)/cells/\(cellKey)"
                    nodes.append(VisionDocumentNodeNative(
                        kind: .tableCell,
                        path: cellPath,
                        parentPath: tablePath,
                        text: cell.content.text.transcript,
                        polygon: polygon(cell.content.boundingRegion),
                        rowIndex: NSNumber(value: cell.rowRange.lowerBound),
                        columnIndex: NSNumber(value: cell.columnRange.lowerBound),
                        rowSpan: NSNumber(value: cell.rowRange.count),
                        columnSpan: NSNumber(value: cell.columnRange.count),
                        rangeProvider: rangeProvider(cell.content.text)
                    ))
                    append(
                        cell.content,
                        path: "\(cellPath)/content",
                        parentPath: cellPath,
                        depth: depth + 1,
                        to: &nodes,
                        truncated: &truncated,
                        maximumDepthReached: &maximumDepthReached,
                        activeContainers: &activeContainers,
                        repeatedContainerCount: &repeatedContainerCount,
                        firstRepeatedContainerPath: &firstRepeatedContainerPath,
                        firstRepeatedAncestorPath: &firstRepeatedAncestorPath
                    )
                }
            }
        }

        for (index, list) in container.lists.enumerated() {
            guard nodes.count < maximumNodeCount else {
                truncated = true
                return
            }
            let listPath = "\(path)/lists/\(index)"
            nodes.append(VisionDocumentNodeNative(
                kind: .list,
                path: listPath,
                parentPath: parentPath,
                polygon: polygon(list.boundingRegion)
            ))

            for (itemIndex, item) in list.items.enumerated() {
                guard nodes.count < maximumNodeCount else {
                    truncated = true
                    return
                }
                let itemPath = "\(listPath)/items/\(itemIndex)"
                let itemText = item.content.text
                nodes.append(VisionDocumentNodeNative(
                    kind: .listItem,
                    path: itemPath,
                    parentPath: listPath,
                    text: item.itemString,
                    polygon: polygon(item.content.boundingRegion),
                    itemString: item.itemString,
                    markerString: item.markerString,
                    markerType: markerName(item.markerType),
                    textAlignment: alignmentName(itemText.textAlignment),
                    recognitionLanguages: recognitionLanguages(itemText),
                    detectedDataJson: detectedDataJson(itemText),
                    candidatesJson: candidatesJson(itemText),
                    rangeProvider: rangeProvider(itemText)
                ))
                append(
                    item.content,
                    path: "\(itemPath)/content",
                    parentPath: itemPath,
                    depth: depth + 1,
                    to: &nodes,
                    truncated: &truncated,
                    maximumDepthReached: &maximumDepthReached,
                    activeContainers: &activeContainers,
                    repeatedContainerCount: &repeatedContainerCount,
                    firstRepeatedContainerPath: &firstRepeatedContainerPath,
                    firstRepeatedAncestorPath: &firstRepeatedAncestorPath
                )
            }
        }

        for (index, barcode) in container.barcodes.enumerated() {
            guard nodes.count < maximumNodeCount else {
                truncated = true
                return
            }
            let barcodePath = "\(path)/barcodes/\(index)"
            nodes.append(VisionDocumentNodeNative(
                kind: .barcode,
                path: barcodePath,
                parentPath: parentPath,
                text: barcode.payloadString,
                polygon: polygon(barcode.boundingRegion),
                confidence: NSNumber(value: barcode.confidence),
                symbology: barcodeSymbologyName(barcode.symbology),
                payloadString: barcode.payloadString,
                payloadData: barcode.payloadData,
                isGS1DataCarrier: NSNumber(value: barcode.isGS1DataCarrier),
                isColorInverted: NSNumber(value: barcode.isColorInverted),
                supplementalPayloadString: barcode.supplementalPayloadString,
                supplementalPayloadData: barcode.supplementalPayloadData,
                supplementalCompositeType: compositeTypeName(barcode.supplementalCompositeType)
            ))
        }
    }

    private static func fingerprint(
        _ container: DocumentObservation.Container
    ) -> ContainerFingerprint {
        let polygon = container.boundingRegion.points.flatMap {
            [
                Int((Double($0.x) * 1_000_000).rounded()),
                Int((Double($0.y) * 1_000_000).rounded()),
            ]
        }
        return ContainerFingerprint(
            transcript: container.text.transcript,
            title: container.title?.transcript,
            polygon: polygon,
            paragraphCount: container.paragraphs.count,
            tableCount: container.tables.count,
            listCount: container.lists.count,
            barcodeCount: container.barcodes.count
        )
    }

    private static func textNode(
        _ text: DocumentObservation.Container.Text,
        kind: VisionDocumentNodeKindNative,
        path: String,
        parentPath: String?
    ) -> VisionDocumentNodeNative {
        return VisionDocumentNodeNative(
            kind: kind,
            path: path,
            parentPath: parentPath,
            text: text.transcript,
            polygon: polygon(text.boundingRegion),
            textAlignment: alignmentName(text.textAlignment),
            recognitionLanguages: recognitionLanguages(text),
            detectedDataJson: detectedDataJson(text),
            candidatesJson: candidatesJson(text),
            rangeProvider: rangeProvider(text)
        )
    }

    private static func recognitionLanguages(
        _ text: DocumentObservation.Container.Text
    ) -> [String]? {
        let languages = Array(Set(text.lines.flatMap {
            $0.recognitionLanguages.map(\.minimalIdentifier)
        })).sorted()
        return languages.isEmpty ? nil : languages
    }

    private static func candidatesJson(
        _ text: DocumentObservation.Container.Text
    ) -> Data? {
        let candidates = text.lines.map { line in
            line.topCandidates(10).map { candidate in
                [
                    "text": candidate.string,
                    "confidence": Double(candidate.confidence),
                ] as [String: Any]
            }
        }
        return try? JSONSerialization.data(
            withJSONObject: candidates,
            options: [.sortedKeys]
        )
    }

    private static func polygon(_ region: NormalizedRegion) -> [NSNumber] {
        var points = region.points
        guard points.count >= 3 else {
            return points.flatMap {
                [NSNumber(value: Double($0.x)), NSNumber(value: Double($0.y))]
            }
        }

        var signedArea = 0.0
        for index in points.indices {
            let next = points[(index + 1) % points.count]
            signedArea += Double(points[index].x * next.y - next.x * points[index].y)
        }
        if signedArea > 0 {
            points.reverse()
        }

        return points.flatMap {
            [NSNumber(value: Double($0.x)), NSNumber(value: Double($0.y))]
        }
    }

    private static func alignmentName(
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

    private static func markerName(
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

    private static func compositeTypeName(
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

    private static func detectedDataJson(
        _ text: DocumentObservation.Container.Text
    ) -> Data? {
        let matches = text.detectedData.map { detected -> [String: Any] in
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
        return try? JSONSerialization.data(
            withJSONObject: matches,
            options: [.sortedKeys]
        )
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

    private static func rangeProvider(
        _ text: DocumentObservation.Container.Text
    ) -> (Int, Int) -> [NSNumber]? {
        { location, length in
            guard location >= 0, length >= 0 else {
                return nil
            }

            let utf16 = text.transcript.utf16
            guard
                let utf16Start = utf16.index(
                    utf16.startIndex,
                    offsetBy: location,
                    limitedBy: utf16.endIndex
                ),
                let utf16End = utf16.index(
                    utf16Start,
                    offsetBy: length,
                    limitedBy: utf16.endIndex
                ),
                let start = String.Index(utf16Start, within: text.transcript),
                let end = String.Index(utf16End, within: text.transcript),
                let region = text.boundingRegion(for: start..<end)
            else {
                return nil
            }

            return polygon(region)
        }
    }
}
