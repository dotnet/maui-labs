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
        assertRejected(#"{"type":"date"}"#, path: "$.type", reason: "date")
    }

    func testArrayWithoutItemsReportsSchemaError() {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse(#"{"type":"array"}"#)) { error in
            XCTAssertTrue(error is JsonSchemaDecoder.SchemaError)
            XCTAssertTrue(error.localizedDescription.contains("$.items"), error.localizedDescription)
            XCTAssertTrue(error.localizedDescription.contains("missing 'items'"), error.localizedDescription)
        }
    }

    func testMalformedJSONDoesNotBecomeFallbackSchema() {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse("{"))
    }

    // MEAI emits type arrays for C# nullable parameters, independently of the required list.
    func testNullableCSharpPrimitivesPreserveRequiredAndOptionalParameters() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"Parameters","properties":{
          "value":{"type":["integer","null"]},
          "label":{"type":["string","null"]},
          "ratio":{"type":["number","null"]},
          "enabled":{"type":["boolean","null"]},
          "count":{"type":"integer"}
        },"required":["value","ratio","enabled"],"additionalProperties":false}
        """))
        func nullable(_ name: String, _ value: DynamicGenerationSchema) -> DynamicGenerationSchema {
            .init(name: "$.properties.\(name)_Union", anyOf: [value, .null])
        }
        let expected = try GenerationSchema(root: DynamicGenerationSchema(name: "Parameters", properties: [
            .init(name: "value", schema: nullable("value", .init(type: Int.self)), isOptional: false),
            .init(name: "label", schema: nullable("label", .init(type: String.self)), isOptional: true),
            .init(name: "ratio", schema: nullable("ratio", .init(type: Double.self)), isOptional: false),
            .init(name: "enabled", schema: nullable("enabled", .init(type: Bool.self)), isOptional: false),
            .init(name: "count", schema: .init(type: Int.self), isOptional: true)
        ]), dependencies: [])
        try assertEqual(actual, expected)
    }

    // Nullable C# arrays, their elements, and inline records must be translated recursively.
    func testNullableArrayItemsAndNestedRecordsMatchNativeSchema() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"Parameters","properties":{
          "records":{"type":["array","null"],"items":{
            "type":["object","null"],"title":"Record","properties":{
              "name":{"type":["string","null"]},
              "scores":{"type":["array","null"],"items":{"type":["integer","null"]}}
            },"required":["name","scores"],"additionalProperties":false
          }}
        },"required":["records"]}
        """))
        let record = DynamicGenerationSchema(name: "Record", properties: [
            .init(name: "name", schema: .init(name: "$.properties.records.items.properties.name_Union",
                                            anyOf: [.init(type: String.self), .null]), isOptional: false),
            .init(name: "scores", schema: .init(name: "$.properties.records.items.properties.scores_Union", anyOf: [
                .init(arrayOf: .init(name: "$.properties.records.items.properties.scores.items_Union",
                                     anyOf: [.init(type: Int.self), .null])), .null
            ]), isOptional: false)
        ])
        let expected = try GenerationSchema(root: DynamicGenerationSchema(name: "Parameters", properties: [
            .init(name: "records", schema: .init(name: "$.properties.records_Union", anyOf: [
                .init(arrayOf: .init(name: "Record_Union", anyOf: [record, .null])), .null
            ]), isOptional: false)
        ]), dependencies: [])
        try assertEqual(actual, expected)
    }

    func testNullableStringEnumsExcludeNullFromStringGuide() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"Parameters","properties":{
          "requiredChoice":{"type":["string","null"],"enum":["walk","visit",null]},
          "optionalChoice":{"type":["string","null"],"enum":["walk","visit",null]}
        },"required":["requiredChoice"]}
        """))
        let string = DynamicGenerationSchema(type: String.self, guides: [.anyOf(["walk", "visit"])])
        let expected = try GenerationSchema(root: DynamicGenerationSchema(name: "Parameters", properties: [
            .init(name: "requiredChoice",
                  schema: .init(name: "$.properties.requiredChoice_Union", anyOf: [string, .null]), isOptional: false),
            .init(name: "optionalChoice",
                  schema: .init(name: "$.properties.optionalChoice_Union", anyOf: [string, .null]), isOptional: true)
        ]), dependencies: [])
        try assertEqual(actual, expected)
    }

    func testSimpleAnyOfMixedUnionAndRequiredNullableMatchNativeSchema() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let actual = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"Parameters","properties":{
          "value":{"anyOf":[{"type":"integer"},{"type":"null"}]},
          "mixed":{"anyOf":[{"type":"string"},{"type":"integer"},{"type":"boolean"}]},
          "typedMixed":{"type":["string","integer"]}
        },"required":["value","mixed","typedMixed"]}
        """))
        let expected = try GenerationSchema(root: DynamicGenerationSchema(name: "Parameters", properties: [
            .init(name: "value", schema: .init(name: "$.properties.value",
                                             anyOf: [.init(type: Int.self), .null]), isOptional: false),
            .init(name: "mixed", schema: .init(name: "$.properties.mixed",
                                             anyOf: [.init(type: String.self), .init(type: Int.self), .init(type: Bool.self)]),
                  isOptional: false),
            .init(name: "typedMixed", schema: .init(name: "$.properties.typedMixed_Union",
                                                  anyOf: [.init(type: String.self), .init(type: Int.self)]),
                  isOptional: false)
        ]), dependencies: [])
        try assertEqual(actual, expected)
    }

    func testSameTitledAnyOfObjectsRetainBothDistinctBranches() throws {
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"title":"Choice","anyOf":[
          {"type":"object","title":"Record","properties":{"a":{"type":"string"}},"required":["a"]},
          {"type":"object","title":"Record","properties":{"b":{"type":"integer"}},"required":["b"]}
        ]}
        """))
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(decoded)) as? [String: Any])
        XCTAssertEqual(json["title"] as? String, "Choice")
        let branches = try XCTUnwrap(json["anyOf"] as? [[String: Any]])
        XCTAssertEqual(branches.count, 2)
        let definitions = try XCTUnwrap(json["$defs"] as? [String: [String: Any]])
        let refs = try branches.map { try XCTUnwrap($0["$ref"] as? String) }
        XCTAssertNotEqual(refs[0], refs[1], "\(json)")
        for (index, property, type) in [(0, "a", "string"), (1, "b", "integer")] {
            let definition = try XCTUnwrap(definitions[String(refs[index].dropFirst("#/$defs/".count))])
            let properties = try XCTUnwrap(definition["properties"] as? [String: [String: Any]])
            XCTAssertEqual(Set(properties.keys), [property])
            XCTAssertEqual(properties[property]?["type"] as? String, type)
            XCTAssertEqual(definition["required"] as? [String], [property])
        }
    }

    func testGeneratedUnionNameDoesNotCollideWithUniqueObjectTitle() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"Parameters","properties":{
          "first":{"type":["object","null"],"title":"Record","properties":{"a":{"type":"string"}},"required":["a"]},
          "second":{"type":"object","title":"Record_Union","properties":{"b":{"type":"integer"}},"required":["b"]}
        },"required":["first","second"]}
        """))
        let expected = try GenerationSchema(root: DynamicGenerationSchema(name: "Parameters", properties: [
            .init(name: "first", schema: .init(name: "$.properties.first_Union_1", anyOf: [
                .init(name: "Record", properties: [
                    .init(name: "a", schema: .init(type: String.self), isOptional: false)
                ]), .null
            ]), isOptional: false),
            .init(name: "second", schema: .init(name: "Record_Union", properties: [
                .init(name: "b", schema: .init(type: Int.self), isOptional: false)
            ]), isOptional: false)
        ]), dependencies: [])
        try assertEqual(decoded, expected)
    }

    func testUnrepresentableSchemasReportNestedPathAndActionableReason() {
        for additional in ["true", #"{"type":"string"}"#] {
            assertRejected("""
            {"type":"object","properties":{"records":{"type":"array","items":{
              "type":"object","additionalProperties":\(additional)
            }}}}
            """, path: "$.properties.records.items.additionalProperties", reason: "named properties")
        }
        assertRejected(##"{"type":"object","properties":{"child":{"$ref":"#/$defs/Child"}},"$defs":{"Child":{"type":"string"}}}"##,
                       path: "$.properties.child.$ref", reason: "inline references")
        assertRejected(#"{"anyOf":[{"type":"string"},{"type":"object","additionalProperties":true}]}"#,
                       path: "$.anyOf[1].additionalProperties", reason: "named properties")
        for keyword in ["oneOf", "allOf", "not"] {
            assertRejected(#"{"type":"object","properties":{"child":{"\#(keyword)":[]}}}"#,
                           path: "$.properties.child.\(keyword)", reason: "not supported")
        }
        assertRejected(#"{"type":"string","anyOf":[{"type":"string"},{"type":"null"}]}"#,
                       path: "$", reason: "sibling constraints")
        assertRejected(#"{"anyOf":[]}"#, path: "$.anyOf", reason: "at least one")
        assertRejected(#"{"anyOf":[{"type":"string"},{"type":"string","minLength":2}]}"#,
                       path: "$.anyOf[1].minLength", reason: "not supported")
    }

    func testToolNativeRejectsInvalidArgumentsAndOutputInsteadOfSubstitutingString() throws {
        for role in ["arguments", "output"] {
            for invalid in ["{", #"{"type":"object","properties":{"value":{"type":"object","additionalProperties":{"type":"string"}}}}"#] {
                let tool = SchemaTool(argumentsSchema: role == "arguments" ? invalid : #"{"type":"object","properties":{}}"#,
                                      outputSchema: role == "output" ? invalid : #"{"type":"string"}"#)
                XCTAssertThrowsError(try ToolNative(tool, nil, nil)) { error in
                    XCTAssertTrue(error.localizedDescription.contains(tool.name), error.localizedDescription)
                    XCTAssertTrue(error.localizedDescription.contains("\(role) schema"), error.localizedDescription)
                    XCTAssertTrue(error.localizedDescription.contains("$"), error.localizedDescription)
                    if invalid != "{" {
                        XCTAssertTrue(error.localizedDescription.contains("$.properties.value.additionalProperties"), error.localizedDescription)
                    }
                }
            }
        }
        let valid = try ToolNative(SchemaTool(argumentsSchema: "{}", outputSchema: #"{"type":"integer"}"#), nil, nil)
        try assertEqual(valid.output, GenerationSchema(root: .init(type: Int.self), dependencies: []))
    }

    private final class SchemaTool: NSObject, AIToolNative, @unchecked Sendable {
        let name = "schemaTestTool"
        let desc = "Model-free schema test."
        let argumentsSchema: String
        let outputSchema: String

        init(argumentsSchema: String, outputSchema: String) {
            self.argumentsSchema = argumentsSchema
            self.outputSchema = outputSchema
        }

        func call(arguments: String) async throws -> String { #""unused""# }
    }

    private func assertRejected(_ json: String, path: String, reason: String,
                                file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertThrowsError(try JsonSchemaDecoder.parse(json), file: file, line: line) { error in
            XCTAssertTrue(error is JsonSchemaDecoder.SchemaError, file: file, line: line)
            XCTAssertTrue(error.localizedDescription.contains(path), error.localizedDescription, file: file, line: line)
            XCTAssertTrue(error.localizedDescription.contains(reason), error.localizedDescription, file: file, line: line)
        }
    }

    private func assertEqual(_ actual: GenerationSchema, _ expected: GenerationSchema,
                             file: StaticString = #filePath, line: UInt = #line) throws {
        let encoder = JSONEncoder()
        let actualJSON = try JSONSerialization.jsonObject(with: encoder.encode(actual))
        let expectedJSON = try JSONSerialization.jsonObject(with: encoder.encode(expected))
        XCTAssertEqual(normalize(actualJSON) as? NSDictionary, normalize(expectedJSON) as? NSDictionary,
                       file: file, line: line)
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
