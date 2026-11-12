import DataDetection
import Foundation
import Vision
import XCTest
@testable import EssentialsAI

final class DocumentLeafEncodingTests: XCTestCase {
    func testNativeEnumNamesKeepExistingSnapshotSpelling() throws {
        let names = [
            try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(BarcodeSymbology.qr),
            try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(DocumentObservation.Container.Text.Alignment.leading),
            try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(DocumentObservation.Container.List.Marker.bullet),
            try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(BarcodeObservation.CompositeType.linked),
        ]
        XCTAssertEqual(names, ["qr", "leading", "bullet", "linked"])
        let absent: BarcodeObservation.CompositeType? = nil
        XCTAssertNil(try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(absent))
    }

    func testEveryNativeSymbologyRoundTripsWithoutAnApplicationCaseList() throws {
        let capabilities = try RecognizeDocumentsRequestNative.capabilities()
        XCTAssertFalse(capabilities.supportedBarcodeSymbologies.isEmpty)
        for symbology in BarcodeSymbology.allCases {
            let name = try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(symbology)
            XCTAssertEqual(
                try RecognizeDocumentsRequestSnapshotBuilder.barcodeSymbology(named: name.uppercased()),
                symbology
            )
        }
        XCTAssertNil(try RecognizeDocumentsRequestSnapshotBuilder.barcodeSymbology(named: "not-a-symbology"))
    }

    func testEncodedCaseNameDoesNotDependOnKnownCases() throws {
        enum FutureCase: Encodable { case addedByTheFramework }
        XCTAssertEqual(
            try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName(FutureCase.addedByTheFramework),
            "addedByTheFramework"
        )
        XCTAssertThrowsError(try RecognizeDocumentsRequestSnapshotBuilder.encodedCaseName("not-an-enum"))
    }

    func testEncodedLeafRetainsUnlistedCasesAndPayloadFields() throws {
        struct Payload: Encodable {
            let newField: String
            let nested: [Int]
        }
        enum FutureLeaf: Encodable { case newKind(Payload) }
        let encoded = try RecognizeDocumentsRequestSnapshotBuilder.encodedJSON(
            FutureLeaf.newKind(Payload(newField: "preserved", nested: [1, 2]))
        )
        let root = try XCTUnwrap(encoded as? [String: Any])
        let kind = try XCTUnwrap(root["newKind"] as? [String: Any])
        let payload = try XCTUnwrap(kind["_0"] as? [String: Any])
        XCTAssertEqual(payload["newField"] as? String, "preserved")
        XCTAssertEqual(payload["nested"] as? [Int], [1, 2])
    }

    func testSemanticDetailsPreserveApplesWholeEncodedPayload() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("DataDetection's native Codable conformances require OS 27.")
        }
        let input = "Visit https://example.com or email someone@example.com. Call +1-415-555-0123. Pay $12.50."
        var types = Set<String>()
        for await match in input.dataDetectorMatches() {
            let snapshot = try RecognizeDocumentsRequestSnapshotBuilder.semanticDetailsSnapshot(match.details)
            let details = try XCTUnwrap(snapshot["details"] as? [String: Any])
            let expected = try RecognizeDocumentsRequestSnapshotBuilder.encodedJSON(match.details)
            XCTAssertEqual(
                try JSONSerialization.data(withJSONObject: details, options: [.sortedKeys]),
                try JSONSerialization.data(withJSONObject: expected, options: [.sortedKeys])
            )
            types.formUnion(details.keys)
            if let money = details["moneyAmount"] as? [String: Any],
               let value = money["_0"] as? [String: Any] {
                XCTAssertEqual(value["amount"] as? Double, 12.5)
                XCTAssertEqual(value["currency"] as? String, "USD")
            }
        }
        XCTAssertTrue(types.isSuperset(of: ["link", "emailAddress", "phoneNumber", "moneyAmount"]))
    }

    func testLegacySemanticDetailsRemainAvailableForOS26() async throws {
        for await match in "Visit https://example.com.".dataDetectorMatches(.link) {
            let snapshot = RecognizeDocumentsRequestSnapshotBuilder.legacySemanticDetailsSnapshot(match.details)
            XCTAssertEqual(snapshot["type"] as? String, "link")
            XCTAssertEqual(snapshot["url"] as? String, "https://example.com")
            return
        }
        XCTFail("The synthetic URL should be detected.")
    }
}
