import Foundation
import Vision

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
final class RecognizeDocumentsRequestObservationSnapshot {
    let uuidString: String
    let confidence: Float
    let transcript: String
    let polygon: [Double]
    let nodes: [RecognizeDocumentsRequestNodeSnapshot]
    let structureTruncated: Bool
    let projectedNodeCount: Int
    let maximumTraversalDepth: Int
    let repeatedContainerCount: Int
    let firstRepeatedContainerPath: String?
    let firstRepeatedAncestorPath: String?

    init(_ observation: DocumentObservation) throws {
        self.uuidString = observation.uuid.uuidString
        self.confidence = observation.confidence
        self.transcript = observation.document.text.transcript
        self.polygon = observation.document.boundingRegion.points.flatMap {
            [Double($0.x), Double($0.y)]
        }
        let buildResult = try RecognizeDocumentsRequestSnapshotBuilder.build(
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

    func snapshotObject() -> [String: Any] {
        var snapshot: [String: Any] = [
            "uuid": uuidString,
            "confidence": Double(confidence),
            "transcript": transcript,
            "polygon": polygon,
            "structureTruncated": structureTruncated,
            "projectedNodeCount": projectedNodeCount,
            "maximumTraversalDepth": maximumTraversalDepth,
            "repeatedContainerCount": repeatedContainerCount,
            "nodes": nodes.map { $0.snapshotObject() },
        ]
        snapshot["firstRepeatedContainerPath"] = firstRepeatedContainerPath
        snapshot["firstRepeatedAncestorPath"] = firstRepeatedAncestorPath
        return snapshot
    }
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
@objc(RecognizeDocumentsRequestSnapshotNative)
public class RecognizeDocumentsRequestSnapshotNative: NSObject, @unchecked Sendable {
    @objc public let jsonData: Data

    init(_ observations: [DocumentObservation]) throws {
        // Apple's Codable graph can re-enter Cell.content and List.Item.content
        // for framework-produced observations, so encode only the bounded snapshot.
        let snapshots = try observations
            .map(RecognizeDocumentsRequestObservationSnapshot.init)
            .map { $0.snapshotObject() }
        self.jsonData = try JSONSerialization.data(
            withJSONObject: snapshots,
            options: [.sortedKeys]
        )
    }
}
