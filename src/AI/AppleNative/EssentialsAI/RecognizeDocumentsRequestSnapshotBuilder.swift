import Foundation
import Vision

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
enum RecognizeDocumentsRequestSnapshotBuilder {
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
        nodes: [RecognizeDocumentsRequestNodeSnapshot],
        truncated: Bool,
        maximumDepth: Int,
        repeatedContainerCount: Int,
        firstRepeatedContainerPath: String?,
        firstRepeatedAncestorPath: String?
    ) {
        var nodes: [RecognizeDocumentsRequestNodeSnapshot] = []
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
        to nodes: inout [RecognizeDocumentsRequestNodeSnapshot],
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
            nodes.append(RecognizeDocumentsRequestNodeSnapshot(
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
                    nodes.append(RecognizeDocumentsRequestNodeSnapshot(
                        kind: .tableCell,
                        path: cellPath,
                        parentPath: tablePath,
                        text: cell.content.text.transcript,
                        polygon: polygon(cell.content.boundingRegion),
                        rowIndex: NSNumber(value: cell.rowRange.lowerBound),
                        columnIndex: NSNumber(value: cell.columnRange.lowerBound),
                        rowSpan: NSNumber(value: cell.rowRange.count),
                        columnSpan: NSNumber(value: cell.columnRange.count)
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
            nodes.append(RecognizeDocumentsRequestNodeSnapshot(
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
                nodes.append(RecognizeDocumentsRequestNodeSnapshot(
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
                    detectedData: detectedDataSnapshot(itemText),
                    candidates: candidatesSnapshot(itemText),
                    words: wordsSnapshot(itemText)
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
            nodes.append(RecognizeDocumentsRequestNodeSnapshot(
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
}
