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

    func testNumericBoundsMatchNativeMinimumMaximumAndRangeGuides() throws {
        let cases: [(String, DynamicGenerationSchema)] = [
            (#"{"type":"integer","minimum":2}"#, .init(type: Int.self, guides: [.minimum(2)])),
            (#"{"type":"integer","maximum":8}"#, .init(type: Int.self, guides: [.maximum(8)])),
            (#"{"type":"integer","minimum":2,"maximum":8}"#, .init(type: Int.self, guides: [.range(2...8)])),
            (#"{"type":"number","minimum":1.25}"#, .init(type: Double.self, guides: [.minimum(1.25)])),
            (#"{"type":"number","maximum":2.5}"#, .init(type: Double.self, guides: [.maximum(2.5)])),
            (#"{"type":"number","minimum":1.25,"maximum":2.5}"#,
             .init(type: Double.self, guides: [.range(1.25...2.5)])),
            (#"{"type":"number","minimum":0.1,"maximum":0.9}"#, .init(type: Double.self, guides: [.range(0.1...0.9)]))
        ]
        for (json, expected) in cases {
            try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(json)),
                            GenerationSchema(root: expected, dependencies: []))
        }
    }

    func testFractionalIntegerBoundsRoundInwardWithoutWeakeningConstraints() throws {
        for (json, range) in [
            (#"{"type":"integer","minimum":1.25,"maximum":4.75}"#, 2...4),
            (#"{"type":"integer","minimum":-4.75,"maximum":-1.25}"#, -4 ... -2)
        ] {
            try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(json)),
                            GenerationSchema(root: .init(type: Int.self, guides: [.range(range)]), dependencies: []))
        }
        let largestInt = try XCTUnwrap(JsonSchemaDecoder.parse(#"{"type":"integer","minimum":9223372036854775807}"#))
        try assertEqual(largestInt, GenerationSchema(root: .init(type: Int.self, guides: [.minimum(Int.max)]), dependencies: []))
    }

    func testUnrepresentableNumericBoundsAreRejectedRatherThanRoundedByNativeGuides() {
        for keyword in ["minimum", "maximum"] {
            assertRejected("""
            {"type":"object","properties":{"value":{"type":"number","\(keyword)":0.1234567890123456789012345678}}}
            """, path: "$.properties.value.\(keyword)", reason: "preserve this numeric bound exactly")
        }
    }

    func testNumericTokensCannotBeRoundedBeforeCheckingNativeBounds() {
        for (keyword, token) in [
            ("minimum", "1.0000000000000000000000000000000000000001"),
            ("maximum", "0.9999999999999999999999999999999999999999"),
            ("minimum", "10000000000000000000000000000000000000001e-40")
        ] {
            assertRejected("""
            {"type":"object","properties":{"value":{"type":"integer",
              "\(keyword)":\(token)
            }}}
            """, path: "$.properties.value.\(keyword)", reason: "exact numeric token")
        }
    }

    func testNumericTokenWalkHandlesEscapesNestedPathsAndAnnotations() throws {
        let schema = try XCTUnwrap(JsonSchemaDecoder.parse(#"""
        {"type":"object","title":"Parameters",
          "description":"\"minimum\":1.0000000000000000000000000000000000000001, } ] \\",
          "default":{"minimum":1.0000000000000000000000000000000000000001},
          "examples":[{"maximum":1.0000000000000000000000000000000000000001}],
          "properties":{
            "minimum":{"type":"string"},
            "value":{"type":"integer","mi\u006eimum":1.2500000000000000000000000000000000000000}
          },"required":["value"]}
        """#))
        let expected = DynamicGenerationSchema(name: "Parameters",
            description: "\"minimum\":1.0000000000000000000000000000000000000001, } ] \\", properties: [
            .init(name: "minimum", schema: .init(type: String.self), isOptional: true),
            .init(name: "value", schema: .init(type: Int.self, guides: [.minimum(2)]), isOptional: false)
        ])
        try assertEqual(schema, GenerationSchema(root: expected, dependencies: []))
        assertRejected(#"""
        {"type":"object","properties":{"rows":{"type":"array","items":{"anyOf":[
          {"type":"integer","mi\u006eimum":1.0000000000000000000000000000000000000001},
          {"type":"string"}
        ]}}}}
        """#, path: "$.properties.rows.items.anyOf[0].minimum", reason: "exact numeric token")
    }

    func testECMAPatternClassesDoNotAcquireSwiftUnicodeSemantics() {
        for suffix in ["", #","enum":["1","\u0661"]"#] {
            assertRejected("""
            {"type":"object","properties":{"value":{"type":"string","pattern":"^\\\\d$"\(suffix)}}}
            """, path: "$.properties.value.pattern", reason: "supported ASCII regex subset")
        }
    }

    func testEnumAndConstCompareExactScalarsRatherThanCanonicalStringEquality() {
        assertRejected(#"{"type":"string","enum":["\u00e9"],"const":"e\u0301"}"#,
                       path: "$", reason: "no common")
    }

    func testNativeStringChoicesRetainCanonicallyEquivalentButDistinctScalars() throws {
        let nfc = "\u{e9}"
        let nfd = "e\u{301}"
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse(#"{"type":"string","enum":["\u00e9","e\u0301"]}"#))
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(decoded)) as? [String: Any])
        let values = try XCTUnwrap(object["enum"] as? [String])
        XCTAssertEqual(values.map { Array($0.utf8) }, [Array(nfc.utf8), Array(nfd.utf8)])
        for constant in [nfc, nfd] {
            let json = try JSONSerialization.data(withJSONObject: ["type": "string", "enum": [nfc, nfd], "const": constant])
            let constrained = try XCTUnwrap(JsonSchemaDecoder.parse(String(decoding: json, as: UTF8.self)))
            let encoded = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(constrained)) as? [String: Any])
            let value = try XCTUnwrap((encoded["enum"] as? [String])?.first ?? encoded["const"] as? String)
            XCTAssertEqual(Array(value.utf8), Array(constant.utf8))
        }
    }

    func testImpossibleNumericBranchesDoNotRejectOtherUnionTypes() throws {
        let string = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":["integer","string"],"minimum":1.25,"maximum":1.75}
        """))
        try assertEqual(string, GenerationSchema(root: .init(type: String.self), dependencies: []))
        guard #available(macOS 26.4, *) else { return }
        let null = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":["number","null"],"minimum":2,"maximum":1}
        """))
        try assertEqual(null, GenerationSchema(root: .null, dependencies: []))
        for json in [
            #"{"anyOf":[{"type":"integer","minimum":1.25,"maximum":1.75},{"type":"string"}]}"#,
            #"{"anyOf":[{"type":"object","properties":{"value":{"type":"integer","minimum":1.25,"maximum":1.75}},"required":["value"]},{"type":"string"}]}"#
        ] {
            try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(json)),
                            GenerationSchema(root: .init(type: String.self), dependencies: []))
        }
        let mixed = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":["integer","number","string"],"minimum":1.25,"maximum":1.75}
        """))
        try assertEqual(mixed, GenerationSchema(root: .init(name: "Object_Union", anyOf: [
            .init(type: Double.self, guides: [.range(1.25...1.75)]), .init(type: String.self)
        ]), dependencies: []))
    }

    func testOptionalImpossiblePropertyDoesNotEraseAValidAnyOfObjectBranch() throws {
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"anyOf":[
          {"type":"object","title":"OptionalValue","properties":{
            "value":{"type":"integer","minimum":1.25,"maximum":1.75}
          }},
          {"type":"string"}
        ]}
        """))
        let expected = DynamicGenerationSchema(name: "Object", anyOf: [
            .init(name: "OptionalValue", properties: []), .init(type: String.self)
        ])
        try assertEqual(decoded, GenerationSchema(root: expected, dependencies: []))
    }

    func testArrayBoundsAndConstrainedElementsMatchNativeSchemasRecursively() throws {
        let cases: [(String, Int?, Int?)] = [
            (#""minItems":2"#, 2, nil),
            (#""maxItems":3"#, nil, 3),
            (#""minItems":2,"maxItems":3"#, 2, 3),
            (#""minItems":2,"maxItems":2"#, 2, 2)
        ]
        for (bounds, minimum, maximum) in cases {
            let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
            {"type":"array",\(bounds),"items":{"type":"integer","minimum":10,"maximum":20}}
            """))
            let expected = DynamicGenerationSchema(
                arrayOf: .init(type: Int.self, guides: [.range(10...20)]),
                minimumElements: minimum, maximumElements: maximum)
            try assertEqual(decoded, GenerationSchema(root: expected, dependencies: []))
        }
        guard #available(macOS 26.4, *) else { return }
        let nested = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":"object","title":"Parameters","properties":{
          "values":{"type":["array","null"],"minItems":2,"maxItems":3,
            "items":{"type":["number","null"],"minimum":1.25,"maximum":2.5}}
        },"required":["values"]}
        """))
        let expected = DynamicGenerationSchema(name: "Parameters", properties: [
            .init(name: "values", schema: .init(name: "$.properties.values_Union", anyOf: [
                .init(arrayOf: .init(name: "$.properties.values.items_Union", anyOf: [
                    .init(type: Double.self, guides: [.range(1.25...2.5)]), .null
                ]), minimumElements: 2, maximumElements: 3), .null
            ]), isOptional: false)
        ])
        try assertEqual(nested, GenerationSchema(root: expected, dependencies: []))
    }

    func testStringConstAndPatternMapToNativeGuidesWithoutDroppingEnums() throws {
        let regex = try Regex<AnyRegexOutput>("^[A-Z]{2}[0-9]{2}$").matchingSemantics(.unicodeScalar)
        for (json, expected) in [
            (#"{"type":"string","const":"fixed"}"#, DynamicGenerationSchema(type: String.self, guides: [.constant("fixed")])),
            (#"{"type":"string","pattern":"^[A-Z]{2}[0-9]{2}$"}"#,
             DynamicGenerationSchema(type: String.self, guides: [.pattern(regex)])),
            (#"{"type":"string","enum":["fixed","other"],"const":"fixed"}"#,
             DynamicGenerationSchema(type: String.self, guides: [.constant("fixed")])),
            (#"{"const":"fixed"}"#, DynamicGenerationSchema(type: String.self, guides: [.constant("fixed")])),
            (#"{"enum":["first","second"]}"#, DynamicGenerationSchema(type: String.self, guides: [.anyOf(["first", "second"])]))
        ] {
            try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(json)),
                            GenerationSchema(root: expected, dependencies: []))
        }
        guard #available(macOS 26.4, *) else { return }
        try assertEqual(XCTUnwrap(JsonSchemaDecoder.parse(#"{"const":null}"#)),
                        GenerationSchema(root: .null, dependencies: []))
    }

    func testNativePatternsEncodeScalarSemanticsAndExplicitASCIIClasses() throws {
        for pattern in ["abc", "^A[0-9]{2}$", "[A-Z0-9_-]+", "a?b*c+", "[a-z]{1,3}", "x{2,}", "^$"] {
            let json = try JSONSerialization.data(withJSONObject: ["type": "string", "pattern": pattern])
            let schema = try XCTUnwrap(JsonSchemaDecoder.parse(String(decoding: json, as: UTF8.self)))
            let object = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(schema)) as? [String: Any])
            let nativePattern = pattern == "[A-Z0-9_-]+" ? "[A-Z0-9_\\-]+" : pattern
            XCTAssertEqual(object["pattern"] as? String, "(?u:\(nativePattern))")
        }
        for (pattern, input, matches) in [("^[0-9]$", "1", true), ("^[0-9]$", "\u{661}", false), ("e", "e\u{301}", true)] {
            let json = try JSONSerialization.data(withJSONObject: ["type": "string", "pattern": pattern])
            let schema = try XCTUnwrap(JsonSchemaDecoder.parse(String(decoding: json, as: UTF8.self)))
            let object = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(schema)) as? [String: Any])
            let encodedPattern = try XCTUnwrap(object["pattern"] as? String)
            XCTAssertEqual(encodedPattern, "(?u:\(pattern))")
            XCTAssertEqual(try Regex<AnyRegexOutput>(pattern).matchingSemantics(.unicodeScalar).firstMatch(in: input) != nil, matches)
        }
    }

    func testPatternsOutsideTheScalarASCIISubsetAndCompoundIntersectionsAreRejected() throws {
        for pattern in ["\\w", "\\s", "\\p{L}", "\u{e9}", ".", "[^0-9]", "(a)", "a|b", "(?i:a)", "(?=a)", "a*?", "a++", "[a-z--b]", "a{2,1}", "a{02}"] {
            let json = try JSONSerialization.data(withJSONObject: [
                "type": "object", "properties": ["value": ["type": "string", "pattern": pattern]]
            ])
            assertRejected(String(decoding: json, as: UTF8.self),
                           path: "$.properties.value.pattern", reason: "supported ASCII regex subset")
        }
        for literal in [#""enum":["A12","B34"]"#, #""const":"A12""#] {
            assertRejected("""
            {"type":"string","pattern":"^[A-Z][0-9]{2}$",\(literal)}
            """, path: "$.pattern", reason: "Compound pattern intersections")
        }
    }

    func testAnnotationsDoNotReplaceOrWeakenNativeConstraints() throws {
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"$schema":"https://json-schema.org/draft/2020-12/schema","title":"Count",
         "description":"A bounded integer","default":99,"examples":[-1,99],
         "type":"integer","minimum":2,"maximum":4}
        """))
        try assertEqual(decoded, GenerationSchema(root: .init(type: Int.self, guides: [.range(2...4)]), dependencies: []))
    }

    func testMixedTypeConstraintsStayOnTheirMatchingUnionBranches() throws {
        guard #available(macOS 26.4, *) else { throw XCTSkip("Explicit null requires OS 26.4.") }
        let decoded = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":["integer","string","null"],"minimum":2,"maximum":4,"pattern":"^A$"}
        """))
        let expected = DynamicGenerationSchema(name: "Object_Union", anyOf: [
            .init(type: Int.self, guides: [.range(2...4)]),
            .init(type: String.self, guides: [.pattern(try Regex<AnyRegexOutput>("^A$").matchingSemantics(.unicodeScalar))]),
            .null
        ])
        try assertEqual(decoded, GenerationSchema(root: expected, dependencies: []))
    }

    func testUnsupportedConstraintsAreRejectedAtTheirPropertyPaths() {
        let constraints: [(String, String)] = [
            ("exclusiveMinimum", "1"), ("exclusiveMaximum", "10"), ("multipleOf", "2"),
            ("minLength", "1"), ("maxLength", "10"),
            ("minProperties", "1"), ("maxProperties", "2"), ("uniqueItems", "true"),
            ("contains", #"{"type":"string"}"#), ("minContains", "1"), ("maxContains", "2"),
            ("prefixItems", #"[{"type":"string"}]"#), ("patternProperties", #"{"x":{"type":"string"}}"#),
            ("propertyNames", #"{"pattern":"x"}"#), ("dependentRequired", #"{"a":["b"]}"#),
            ("dependentSchemas", #"{"a":{"type":"object"}}"#), ("unevaluatedProperties", "false"),
            ("unevaluatedItems", "false"), ("if", #"{"type":"string"}"#), ("then", #"{"type":"string"}"#),
            ("else", #"{"type":"string"}"#), ("$dynamicRef", ##""#item""##)
        ]
        for (keyword, value) in constraints {
            assertRejected("""
            {"type":"object","properties":{"value":{"type":"string","\(keyword)":\(value)}}}
            """, path: "$.properties.value.\(keyword)", reason: "not supported")
        }
        for keyword in ["enum", "const"] {
            let value = keyword == "enum" ? "[1,2]" : "1"
            assertRejected("""
            {"type":"object","properties":{"value":{"type":"integer","\(keyword)":\(value)}}}
            """, path: "$.properties.value.\(keyword)", reason: "no numeric")
        }
    }

    func testInvalidOrAmbiguouslyTypedConstraintsAreRejectedInsteadOfIgnored() {
        for constraint in [#""minimum":1"#, #""maximum":2"#, #""pattern":"x""#, #""minItems":1"#] {
            let keyword = String(constraint.split(separator: ":")[0].dropFirst().dropLast())
            assertRejected("""
            {"type":"object","properties":{"value":{\(constraint)}}}
            """, path: "$.properties.value.\(keyword)", reason: "explicit type")
        }
        let cases: [(String, String, String)] = [
            (#"{"type":"string","minimum":2}"#, "$.minimum", "Numeric bounds"),
            (#"{"type":"integer","pattern":"x"}"#, "$.pattern", "string type"),
            (#"{"type":"number","minimum":4,"maximum":2}"#, "$", "minimum must not exceed"),
            (#"{"type":"integer","minimum":1.25,"maximum":1.75}"#, "$", "No integer"),
            (#"{"type":"integer","minimum":9223372036854775808}"#, "$.minimum", "native Int"),
            (#"{"type":"array","minItems":3,"maxItems":2,"items":{"type":"string"}}"#, "$", "item limits"),
            (#"{"type":"string","pattern":"["}"#, "$.pattern", "supported ASCII regex subset"),
            (#"{"type":"string","enum":["a"],"const":"b"}"#, "$", "no common"),
            (#"{"type":"string","enum":["a"],"pattern":"^b$"}"#, "$.pattern", "Compound pattern intersections"),
            (#"{"type":"string","const":"a","pattern":"^b$"}"#, "$.pattern", "Compound pattern intersections"),
            (#"{"anyOf":[{"type":"integer"}],"minimum":1}"#, "$", "sibling constraints")
        ]
        for (json, path, reason) in cases { assertRejected(json, path: path, reason: reason) }
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

    func testFormatAnnotationsDoNotConstrainNativeStringSchemas() throws {
        for format in ["uuid", "date-time", "date", "time", "uri"] {
            let schema = try XCTUnwrap(JsonSchemaDecoder.parse("""
            {"type":"object","properties":{"value":{"type":"string","format":"\(format)"}},"required":["value"]}
            """))
            let expected = DynamicGenerationSchema(name: "Object", properties: [
                .init(name: "value", schema: .init(type: String.self), isOptional: false)
            ])
            try assertEqual(schema, GenerationSchema(root: expected, dependencies: []))
        }
        XCTAssertNoThrow(try JsonSchemaDecoder.parse("""
        {"anyOf":[{"type":"string"}],"format":"uuid"}
        """))
        guard #available(macOS 26.4, *) else { return }
        let nullable = try XCTUnwrap(JsonSchemaDecoder.parse("""
        {"type":["string","null"],"format":"date-time"}
        """))
        try assertEqual(nullable, GenerationSchema(root: .init(name: "Object_Union", anyOf: [
            .init(type: String.self), .null
        ]), dependencies: []))
    }

    func testToolNativeRejectsInvalidArgumentsInsteadOfSubstitutingString() throws {
        for invalid in ["{", #"{"type":"object","properties":{"value":{"type":"object","additionalProperties":{"type":"string"}}}}"#] {
            let tool = SchemaTool(argumentsSchema: invalid, outputSchema: #"{"type":"string"}"#)
            XCTAssertThrowsError(try ToolNative(tool, nil, nil)) { error in
                XCTAssertTrue(error.localizedDescription.contains(tool.name), error.localizedDescription)
                XCTAssertTrue(error.localizedDescription.contains("arguments schema"), error.localizedDescription)
                XCTAssertTrue(error.localizedDescription.contains("$"), error.localizedDescription)
                if invalid != "{" {
                    XCTAssertTrue(error.localizedDescription.contains("$.properties.value.additionalProperties"), error.localizedDescription)
                }
            }
        }
    }

    func testToolNativeDoesNotGateRegistrationOnUnusedOutputSchemas() throws {
        let arguments = #"{"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}"#
        for output in [
            "{",
            #"{"type":"string","format":"date-time"}"#,
            #"{"type":"object","additionalProperties":{"type":"string"}}"#,
            #"{"type":["array","null"],"items":{"oneOf":[{"type":"string"},{"type":"integer"}]}}"#
        ] {
            let tool = try ToolNative(SchemaTool(argumentsSchema: arguments, outputSchema: output), nil, nil)
            try assertEqual(tool.parameters, XCTUnwrap(JsonSchemaDecoder.parse(arguments)))
        }
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
