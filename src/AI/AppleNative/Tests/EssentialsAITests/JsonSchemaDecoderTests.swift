import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

// Original schema policy is tested in managed code; these tests exercise only the native boundary.
final class JsonSchemaDecoderTests: XCTestCase {
    func testCanonicalNestedObjectArrayAndEnumMatchDirectNativeSchema() throws {
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"AppleSchema0","description":"Trip","properties":{
          "activities":{"type":"array","minItems":1,"maxItems":2,"items":{
            "type":"object","title":"AppleSchema1","properties":{
              "kind":{"type":"string","enum":["walk","visit"]},
              "name":{"type":"string","description":"Activity name"}
            },"required":["kind","name"],"additionalProperties":false}}
        },"required":["activities"],"additionalProperties":false}
        """))
        let activity = DynamicGenerationSchema(name: "AppleSchema1", properties: [
            .init(name: "kind", schema: .init(type: String.self, guides: [.anyOf(["walk", "visit"])]), isOptional: false),
            .init(name: "name", description: "Activity name", schema: .init(type: String.self), isOptional: false)
        ])
        let expected = try GenerationSchema(root: .init(name: "AppleSchema0", description: "Trip", properties: [
            .init(name: "activities", schema: .init(arrayOf: activity, minimumElements: 1, maximumElements: 2), isOptional: false)
        ]), dependencies: [])
        try assertEqual(actual, expected)
    }

    func testCanonicalNullableNestedValuesDoNotControlPropertyPresence() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"AppleSchema0","properties":{
          "count":{"title":"AppleSchema1","anyOf":[{"type":"integer"},{"type":"null"}]},
          "records":{"type":"array","items":{"title":"AppleSchema2","anyOf":[
            {"type":"object","title":"AppleSchema3","properties":{"label":{"type":"string"}},
             "required":[],"additionalProperties":false},{"type":"null"}]}}
        },"required":["count"],"additionalProperties":false}
        """))
        let record = DynamicGenerationSchema(name: "AppleSchema3", properties: [
            .init(name: "label", schema: .init(type: String.self), isOptional: true)
        ])
        try assertEqual(actual, GenerationSchema(root: .init(name: "AppleSchema0", properties: [
            .init(name: "count", schema: .init(name: "AppleSchema1", anyOf: [.init(type: Int.self), .null]), isOptional: false),
            .init(name: "records", schema: .init(arrayOf: .init(name: "AppleSchema2", anyOf: [record, .null])), isOptional: true)
        ]), dependencies: []))
    }

    func testCanonicalUnionIdentitiesRemainDistinctNativeDefinitions() throws {
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"title":"AppleSchema0","anyOf":[
          {"type":"object","title":"AppleSchema1","properties":{"a":{"type":"string"}},"required":["a"],"additionalProperties":false},
          {"type":"object","title":"AppleSchema2","properties":{"b":{"type":"integer"}},"required":["b"],"additionalProperties":false}
        ]}
        """))
        try assertEqual(actual, GenerationSchema(root: .init(name: "AppleSchema0", anyOf: [
            .init(name: "AppleSchema1", properties: [.init(name: "a", schema: .init(type: String.self), isOptional: false)]),
            .init(name: "AppleSchema2", properties: [.init(name: "b", schema: .init(type: Int.self), isOptional: false)])
        ]), dependencies: []))
    }

    func testCanonicalNumericGuidesMatchDirectNativeConstruction() throws {
        let cases: [(String, DynamicGenerationSchema)] = [
            (#"{"type":"integer","minimum":2}"#, .init(type: Int.self, guides: [.minimum(2)])),
            (#"{"type":"integer","maximum":8}"#, .init(type: Int.self, guides: [.maximum(8)])),
            (#"{"type":"integer","minimum":2,"maximum":8}"#, .init(type: Int.self, guides: [.range(2...8)])),
            (#"{"type":"number","minimum":1.25}"#, .init(type: Double.self, guides: [.minimum(1.25)])),
            (#"{"type":"number","maximum":2.5}"#, .init(type: Double.self, guides: [.maximum(2.5)])),
            (#"{"type":"number","minimum":0.1,"maximum":0.9}"#, .init(type: Double.self, guides: [.range(0.1...0.9)]))
        ]
        for (profile, native) in cases {
            try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(profile)), GenerationSchema(root: native, dependencies: []))
        }
    }

    func testCanonicalSafeIntegerEndpointsPreserveNativeEncodedBounds() throws {
        for value in [9_007_199_254_740_990, 9_007_199_254_740_991,
                      -9_007_199_254_740_990, -9_007_199_254_740_991] {
            for key in ["minimum", "maximum"] {
                let direct = DynamicGenerationSchema(type: Int.self, guides: key == "minimum" ? [.minimum(value)] : [.maximum(value)])
                let encoded = try JSONEncoder().encode(GenerationSchema(root: direct, dependencies: []))
                let token = try nativeBound(encoded, key: key)
                let profile = #"{"type":"integer","\#(key)":\#(value)}"#
                XCTAssertEqual(NSDecimalNumber(string: token), NSDecimalNumber(value: value))
                try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(profile)), GenerationSchema(root: direct, dependencies: []))
            }
        }
    }

    func testFiniteDoublesBeyondDecimalExponentRangeUseActualNativeTokenFidelity() throws {
        for (token, value) in [("1e200", 1e200), ("1e-200", 1e-200), ("5e-324", Double.leastNonzeroMagnitude)] {
            for key in ["minimum", "maximum"] {
                let direct = DynamicGenerationSchema(type: Double.self, guides: key == "minimum" ? [.minimum(value)] : [.maximum(value)])
                let native = try GenerationSchema(root: direct, dependencies: [])
                let bound = try nativeBound(JSONEncoder().encode(native), key: key)
                let profile = #"{"type":"number","\#(key)":\#(token)}"#
                // These one-digit coefficients have an unambiguous shortest token. A changed native
                // guide (including underflow to zero) must fail, not be hidden by Double decoding.
                let expected = token.replacingOccurrences(of: "e", with: "e+")
                let normalized = bound.lowercased().replacingOccurrences(of: ".0e", with: "e")
                if normalized == token || normalized == expected {
                    try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(profile)), native)
                } else {
                    assertRejected(profile, path: "$.\(key)", reason: "preserve this numeric bound exactly")
                }
            }
        }
    }

    func testPatternIsNotPartOfCanonicalProfile() {
        assertRejected(#"{"type":"string","pattern":"A"}"#, path: "$.pattern", reason: "compile the original schema in managed code")
        assertRejected(#"{"type":"string","pattern":"^A[0-9]{2}$"}"#, path: "$.pattern", reason: "compile the original schema in managed code")
    }

    func testCanonicalStringChoicesPreserveUnicodeScalarDifferences() throws {
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse(#"{"type":"string","enum":["\u00e9","e\u0301"]}"#))
        try assertEqual(actual, GenerationSchema(root: .init(type: String.self, guides: [.anyOf(["\u{e9}", "e\u{301}"])]), dependencies: []))
        let actualJSON = String(decoding: try JSONEncoder().encode(actual), as: UTF8.self)
        let values = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(actualJSON.utf8)) as? [String: Any])["enum"] as? [String]
        XCTAssertEqual(values?.count, 2)
        XCTAssertFalse(values![0].utf8.elementsEqual(values![1].utf8))
    }

    func testMalformedCanonicalBoundariesFailWithoutNormalizationOrFallback() {
        for (profile, path) in [
            (#"{"type":["string","null"]}"#, "$.type"),
            (#"{"type":"array"}"#, "$.items"),
            (#"{"type":"string","format":"uuid"}"#, "$.format"),
            (#"{"type":"object","properties":{},"required":[],"additionalProperties":false}"#, "$.title"),
            (#"{"type":"object","title":"AppleSchema0","properties":{},"required":[],"additionalProperties":true}"#, "$"),
            (#"{"title":"AppleSchema0","anyOf":[]}"#, "$.anyOf"),
            (#"{"title":"AppleSchema0","anyOf":[{"type":"string"}]}"#, "$.anyOf"),
            (#"{"type":"integer","minimum":1.25}"#, "$"),
            (#"{"type":"string","enum":[]}"#, "$"),
            (#"{"type":"string","enum":["x"],"const":"x"}"#, "$")
        ] {
            assertRejected(profile, path: path, reason: "")
        }
        XCTAssertThrowsError(try JsonSchemaDecoder.parse("{"))
    }

    func testCanonicalNoArgumentToolAndNativeErrorsRetainFunctionContext() throws {
        let profile = #"{"type":"object","title":"AppleSchema0","properties":{},"required":[],"additionalProperties":false}"#
        let tool = try ToolNative(SchemaTool(profile), nil, nil)
        try assertEqual(tool.parameters, GenerationSchema(root: .init(name: "AppleSchema0", properties: []), dependencies: []))
        XCTAssertThrowsError(try ToolNative(SchemaTool(#"{"type":"array"}"#), nil, nil)) { error in
            XCTAssertTrue(error.localizedDescription.contains("canonical_tool"))
            XCTAssertTrue(error.localizedDescription.contains("$.items"))
        }
    }

    private final class SchemaTool: NSObject, AIToolNative, @unchecked Sendable {
        let name = "canonical_tool"
        let desc = "Canonical tool fixture"
        let argumentsSchema: String
        init(_ profile: String) { argumentsSchema = profile }
        func call(arguments: String) async throws -> String { "{}" }
    }

    private func nativeBound(_ data: Data, key: String) throws -> String {
        let encoded = String(decoding: data, as: UTF8.self)
        let regex = try NSRegularExpression(pattern: #""\#(key)"\s*:\s*([^,}\s]+)"#)
        let match = try XCTUnwrap(regex.firstMatch(in: encoded, range: NSRange(encoded.startIndex..., in: encoded)))
        return String(encoded[Range(match.range(at: 1), in: encoded)!])
    }

    private func assertRejected(_ profile: String, path: String, reason: String, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse(profile), file: file, line: line) { error in
            guard let error = error as? JsonSchemaDecoder.SchemaError else {
                return XCTFail("Expected SchemaError, got \(error)", file: file, line: line)
            }
            XCTAssertEqual(error.path, path, file: file, line: line)
            XCTAssertTrue(reason.isEmpty || error.reason.contains(reason), error.reason, file: file, line: line)
        }
    }

    private func assertEqual(_ actual: GenerationSchema, _ expected: GenerationSchema, file: StaticString = #filePath, line: UInt = #line) throws {
        let actual = try JSONSerialization.jsonObject(with: JSONEncoder().encode(actual))
        let expected = try JSONSerialization.jsonObject(with: JSONEncoder().encode(expected))
        XCTAssertEqual(normalize(actual) as? NSDictionary, normalize(expected) as? NSDictionary, file: file, line: line)
    }

    private func normalize(_ value: Any) -> Any {
        if let object = value as? [String: Any] { return object.mapValues(normalize) }
        if let array = value as? [Any] {
            return array.map(normalize).sorted {
                String(describing: $0) < String(describing: $1)
            }
        }
        return value
    }
}
