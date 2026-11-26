import Foundation
import XCTest
@testable import EssentialsAI

final class RecognizeDocumentsRequestSnapshotTests: XCTestCase {
    func testDefaultNodeOmitsNilFieldsButKeepsRequiredEmptyArrays() throws {
        let node = RecognizeDocumentsRequestNodeSnapshot(
            kind: .paragraph,
            path: "document/paragraphs/0",
            parentPath: nil
        )

        XCTAssertEqual(
            try json(node.snapshotObject()),
            #"{"candidates":[],"detectedData":[],"kind":"paragraph","path":"document\/paragraphs\/0","polygon":[]}"#
        )
    }

    func testPopulatedNodePreservesEveryFieldAndJSONValueType() throws {
        let node = RecognizeDocumentsRequestNodeSnapshot(
            kind: .barcode,
            path: "document/barcodes/0",
            parentPath: "document",
            text: "text",
            polygon: [0.1, 0.9, 0.8, 0.7, 0.6, 0.2, 0.3, 0.4],
            confidence: 0.75,
            rowIndex: 0,
            columnIndex: 2,
            rowSpan: 3,
            columnSpan: 4,
            itemString: "item",
            markerString: "1.",
            markerType: "decimal",
            symbology: "qr",
            payloadString: "payload",
            payloadData: Data([0, 255, 128, 65]),
            isGS1DataCarrier: NSNumber(value: true),
            isColorInverted: NSNumber(value: false),
            supplementalPayloadString: "supplement",
            supplementalPayloadData: Data([1, 2, 3]),
            supplementalCompositeType: "linked",
            textAlignment: "leading",
            recognitionLanguages: ["en", "fr"],
            detectedData: [["type": "link", "url": "https://example.com", "utf16Location": 2]],
            candidates: [["candidates": [["text": "candidate", "confidence": 0.5]]]],
            words: [["recognitionLanguages": ["en"], "candidates": []]]
        )
        let expected: [String: Any] = [
            "kind": "barcode",
            "path": "document/barcodes/0",
            "parentPath": "document",
            "text": "text",
            "polygon": [0.1, 0.9, 0.8, 0.7, 0.6, 0.2, 0.3, 0.4],
            "confidence": 0.75,
            "rowIndex": 0,
            "columnIndex": 2,
            "rowSpan": 3,
            "columnSpan": 4,
            "itemString": "item",
            "markerString": "1.",
            "markerType": "decimal",
            "symbology": "qr",
            "payloadString": "payload",
            "payloadDataBase64": "AP+AQQ==",
            "isGS1DataCarrier": true,
            "isColorInverted": false,
            "supplementalPayloadString": "supplement",
            "supplementalPayloadDataBase64": "AQID",
            "supplementalCompositeType": "linked",
            "textAlignment": "leading",
            "recognitionLanguages": ["en", "fr"],
            "detectedData": [["type": "link", "url": "https://example.com", "utf16Location": 2]],
            "candidates": [["candidates": [["text": "candidate", "confidence": 0.5]]]],
            "words": [["recognitionLanguages": ["en"], "candidates": []]],
        ]

        XCTAssertEqual(try json(node.snapshotObject()), try json(expected))
    }

    func testExplicitEmptyOptionalValuesAreNotOmittedOrEncodedAsNull() throws {
        let node = RecognizeDocumentsRequestNodeSnapshot(
            kind: .listItem,
            path: "",
            parentPath: "",
            text: "",
            payloadData: Data(),
            supplementalPayloadData: Data(),
            recognitionLanguages: [],
            words: []
        )
        let expected: [String: Any] = [
            "kind": "listItem",
            "path": "",
            "parentPath": "",
            "text": "",
            "polygon": [],
            "payloadDataBase64": "",
            "supplementalPayloadDataBase64": "",
            "recognitionLanguages": [],
            "detectedData": [],
            "candidates": [],
            "words": [],
        ]

        XCTAssertEqual(try json(node.snapshotObject()), try json(expected))
    }

    func testNodeKindNamesAndRawValuesStayStable() {
        let kinds: [(RecognizeDocumentsRequestNodeKind, String)] = [
            (.title, "title"), (.paragraph, "paragraph"), (.table, "table"),
            (.tableCell, "tableCell"), (.list, "list"), (.listItem, "listItem"),
            (.barcode, "barcode"),
        ]
        for (index, entry) in kinds.enumerated() {
            let node = RecognizeDocumentsRequestNodeSnapshot(
                kind: entry.0, path: "document", parentPath: nil
            )
            XCTAssertEqual(entry.0.rawValue, index)
            XCTAssertEqual(node.snapshotObject()["kind"] as? String, entry.1)
        }
    }

    func testEmptyObservationListEncodesAsAnEmptyJSONArray() throws {
        let snapshot = try RecognizeDocumentsRequestSnapshotNative([])
        XCTAssertEqual(String(decoding: snapshot.jsonData, as: UTF8.self), "[]")
    }

    private func json(_ object: [String: Any]) throws -> String {
        String(decoding: try JSONSerialization.data(withJSONObject: object, options: [.sortedKeys]),
               as: UTF8.self)
    }
}
