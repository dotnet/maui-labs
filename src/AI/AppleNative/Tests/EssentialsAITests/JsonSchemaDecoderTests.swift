import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class JsonSchemaDecoderTests: XCTestCase {
    func testNestedObjectArrayAndEnumMatchNativeSchema() throws {
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {
          "type":"object","title":"Trip",
          "properties":{
            "activities":{
              "type":"array","minItems":1,"maxItems":2,
              "items":{"type":"object","title":"Activity","properties":{
                "name":{"type":"string"},
                "kind":{"type":"string","enum":["walk","visit"]}
              },"required":["name","kind"]}
            }
          },"required":["activities"]
        }
        """))

        let activity = DynamicGenerationSchema(name: "Activity", properties: [
            .init(name: "name", schema: .init(type: String.self), isOptional: false),
            .init(name: "kind", schema: .init(type: String.self, guides: [.anyOf(["walk", "visit"])]), isOptional: false)
        ])
        let expected = try GenerationSchema(root: DynamicGenerationSchema(name: "Trip", properties: [
            .init(name: "activities",
                  schema: .init(arrayOf: activity, minimumElements: 1, maximumElements: 2),
                  isOptional: false)
        ]), dependencies: [])

        let encoder = JSONEncoder()
        let actualJSON = try JSONSerialization.jsonObject(with: encoder.encode(decoded))
        let expectedJSON = try JSONSerialization.jsonObject(with: encoder.encode(expected))

        XCTAssertEqual(normalize(actualJSON) as? NSDictionary, normalize(expectedJSON) as? NSDictionary)
    }

    func testUnsupportedTypeReportsSchemaError() {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse(#"{"type":"null"}"#)) { error in
            XCTAssertTrue(error is JsonSchemaDecoder.SchemaError)
        }
    }

    func testArrayWithoutItemsReportsSchemaError() {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse(#"{"type":"array"}"#)) { error in
            guard case JsonSchemaDecoder.SchemaError.missingArrayItems = error else {
                return XCTFail("Expected missingArrayItems, got \(error)")
            }
        }
    }

    func testMalformedJSONDoesNotBecomeFallbackSchema() {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse("{"))
    }

    // Dictionary enumeration order is not part of the JSON Schema contract.
    private func normalize(_ value: Any) -> Any {
        if var object = value as? [String: Any] {
            object.removeValue(forKey: "x-order")
            if let required = object["required"] as? [String] {
                object["required"] = required.sorted()
            }
            return object.mapValues(normalize)
        }

        if let array = value as? [Any] {
            return array.map(normalize)
        }

        return value
    }
}
