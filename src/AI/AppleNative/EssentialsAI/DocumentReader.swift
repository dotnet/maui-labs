import Foundation
import ImageIO
import Vision

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, *)
@objc(VisionDocumentReaderNative)
public final class VisionDocumentReaderNative: NSObject {
    @objc public func recognize(
        imageData: Data,
        orientation: Int,
        onComplete: @escaping (Data?, NSError?) -> Void
    ) -> CancellationTokenNative {
        let task = Task {
            do {
                try Task.checkCancellation()
                let orientation = orientation > 0
                    ? CGImagePropertyOrientation(rawValue: UInt32(orientation))
                    : nil
                let observations = try await RecognizeDocumentsRequest().perform(
                    on: imageData, orientation: orientation)
                try Task.checkCancellation()
                let elements = observations.flatMap { Self.snapshot($0.document) }
                let data = try JSONSerialization.data(withJSONObject: elements)
                try Task.checkCancellation()
                // Never redispatch onto OperationQueue.current: a caller processing
                // sequential PDF pages may be awaiting this very completion.
                onComplete(data, nil)
            } catch {
                onComplete(nil, error as NSError)
            }
        }
        return CancellationTokenNative(task: task)
    }

    private static let limit = 20_000

    private struct Element {
        let kind: String
        let text: String
        let points: [NormalizedPoint]
        let extra: [String: Any]

        var json: [String: Any] {
            var value = extra
            value["kind"] = kind
            value["text"] = text
            return value
        }
    }

    private static func snapshot(_ root: DocumentObservation.Container) -> [[String: Any]] {
        var candidates: [Element] = []
        if let title = root.title {
            candidates.append(Element(kind: "title", text: title.transcript,
                                      points: title.boundingRegion.points, extra: [:]))
        }
        for table in root.tables.prefix(limit) {
            var cells: [[String: Any]] = []
            var seen = Set<String>()
            var rowCount = 0
            var columnCount = 0
            for row in table.rows where cells.count < limit {
                for cell in row {
                    guard cells.count < limit else { break }
                    let r = cell.rowRange.lowerBound
                    let c = cell.columnRange.lowerBound
                    guard r >= 0, c >= 0 else { continue }
                    guard seen.insert("\(r):\(c)").inserted else { continue }
                    rowCount = max(rowCount, cell.rowRange.upperBound + 1)
                    columnCount = max(columnCount, cell.columnRange.upperBound + 1)
                    cells.append(["row": r, "column": c,
                                  "rowSpan": cell.rowRange.count,
                                  "columnSpan": cell.columnRange.count,
                                  "text": cell.content.text.transcript])
                }
            }
            candidates.append(Element(kind: "table", text: "",
                                      points: table.boundingRegion.points,
                                      extra: ["cells": cells, "rows": rowCount,
                                              "columns": columnCount]))
        }

        var pending: [(DocumentObservation.Container, Int, Set<String>)] = [(root, 0, [])]
        var visitedCount = 0
        while let (container, depth, ancestors) = pending.popLast(), visitedCount < limit {
            visitedCount += 1
            guard depth < 64 else { continue }
            let fingerprint = "\(container.text.transcript)|\(container.paragraphs.count)|\(container.tables.count)|\(container.lists.count)"
            guard !ancestors.contains(fingerprint) else { continue }
            var childAncestors = ancestors
            childAncestors.insert(fingerprint)
            for list in container.lists {
                for item in list.items {
                    guard candidates.count + pending.count < limit else { break }
                    let element = Element(kind: "paragraph", text: item.itemString,
                                          points: item.content.boundingRegion.points, extra: [:])
                    if !element.text.isEmpty && !candidates.contains(where: {
                        $0.kind == element.kind && $0.text == element.text &&
                        overlap($0.points, element.points) >= 0.85 &&
                        overlap(element.points, $0.points) >= 0.85
                    }) {
                        candidates.append(element)
                    }
                    pending.append((item.content, depth + 1, childAncestors))
                }
            }
        }

        // Vision paragraphs already have a reading order (including columns).
        // Replace regions in that order instead of globally sorting by Y/X.
        var emitted = Set<Int>()
        var result: [[String: Any]] = []
        for paragraph in root.paragraphs.prefix(limit) {
            let points = paragraph.boundingRegion.points
            let match = candidates.indices.first { index in
                let element = candidates[index]
                switch element.kind {
                case "title":
                    return paragraph.transcript == element.text &&
                        overlap(points, element.points) >= 0.7
                case "table":
                    return overlap(points, element.points) >= 0.7
                default:
                    return overlap(points, element.points) >= 0.55 &&
                        paragraph.transcript.localizedCaseInsensitiveContains(element.text)
                }
            }
            if let match {
                if emitted.insert(match).inserted {
                    result.append(candidates[match].json)
                }
            } else if !paragraph.transcript.isEmpty {
                result.append(Element(kind: "paragraph", text: paragraph.transcript,
                                      points: points, extra: [:]).json)
            }
        }
        for index in candidates.indices where !emitted.contains(index) && result.count < limit {
            result.append(candidates[index].json)
        }
        return result
    }

    private static func overlap(_ a: [NormalizedPoint], _ b: [NormalizedPoint]) -> Double {
        guard let ax0 = a.map({ Double($0.x) }).min(),
              let ax1 = a.map({ Double($0.x) }).max(),
              let ay0 = a.map({ Double($0.y) }).min(),
              let ay1 = a.map({ Double($0.y) }).max(),
              let bx0 = b.map({ Double($0.x) }).min(),
              let bx1 = b.map({ Double($0.x) }).max(),
              let by0 = b.map({ Double($0.y) }).min(),
              let by1 = b.map({ Double($0.y) }).max() else { return 0 }
        let area = (ax1 - ax0) * (ay1 - ay0)
        guard area > 0 else { return 0 }
        return max(0, min(ax1, bx1) - max(ax0, bx0)) *
               max(0, min(ay1, by1) - max(ay0, by0)) / area
    }
}
