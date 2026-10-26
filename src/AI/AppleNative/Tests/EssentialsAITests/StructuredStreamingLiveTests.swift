import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

/// Live Apple Intelligence tests (ESSENTIALSAI_LIVE_TESTS=1). Exact-value prompts constrain the
/// model so a failure points at streaming, not at semantic variation. Property order is never
/// asserted because the native snapshots reorder keys.
final class StructuredStreamingLiveTests: XCTestCase {

    private struct ExactCase {
        let prompt: String
        let schema: String
        let expected: String
    }

    private static let exactValues = ExactCase(
        prompt: "Return exactly values [123] with no other semantic variation.",
        schema: """
        {"type":"object","title":"ExactValues","properties":{
          "values":{"type":"array","items":{"type":"integer"}}
        },"required":["values"]}
        """,
        expected: #"{"values":[123]}"#)

    private static let exactMixed = ExactCase(
        prompt: """
        Return exactly this JSON value with no other semantic variation: \
        {"label":"test","ready":true,"amount":-12.75,"values":[10,20,30],"details":{"text":"all done","enabled":false}}
        """,
        schema: """
        {"type":"object","title":"ExactMixed","properties":{
          "label":{"type":"string"},
          "ready":{"type":"boolean"},
          "amount":{"type":"number"},
          "values":{"type":"array","items":{"type":"integer"}},
          "details":{"type":"object","title":"Details","properties":{
            "text":{"type":"string"},"enabled":{"type":"boolean"}
          },"required":["text","enabled"]}
        },"required":["label","ready","amount","values","details"]}
        """,
        expected: #"{"label":"test","ready":true,"amount":-12.75,"values":[10,20,30],"details":{"text":"all done","enabled":false}}"#)

    // MARK: - Exact values

    func testLiveExactValuesDirectStream() async throws {
        try await verifyExactDirectStream(Self.exactValues, minimumStreamedDeltas: 2)
    }

    func testLiveExactMixedValuesDirectStream() async throws {
        try await verifyExactDirectStream(Self.exactMixed, minimumStreamedDeltas: 3)
    }

    func testLiveExactValuesThroughChatClient() async throws {
        try await verifyExactClient(Self.exactValues, minimumContentUpdates: 2)
    }

    func testLiveExactMixedValuesThroughChatClient() async throws {
        try await verifyExactClient(Self.exactMixed, minimumContentUpdates: 3)
    }

    private func verifyExactDirectStream(_ exact: ExactCase, minimumStreamedDeltas: Int) async throws {
        guard try LiveTests.requireModel() else { return }
        let run = try await LiveTests.streamDirect(prompt: exact.prompt, schema: exact.schema, maxTokens: 128)
        let expected = try canonicalJSON(exact.expected)

        XCTAssertEqual(try canonicalJSON(run.finalJSON), expected, "Model did not return the exact values")
        for replay in run.replays {
            XCTAssertEqual(try canonicalJSON(replay.text), expected, "Deltas replayed every \(replay.stride) snapshots")
            XCTAssertFalse(replay.streamed.contains(""))
        }

        let every = try XCTUnwrap(run.replays.first { $0.stride == 1 })
        XCTAssertGreaterThanOrEqual(every.streamed.count, minimumStreamedDeltas,
                                    "Deltas should arrive before the stream completes (\(run.snapshotCount) snapshots)")
    }

    private func verifyExactClient(_ exact: ExactCase, minimumContentUpdates: Int) async throws {
        guard try LiveTests.requireModel() else { return }
        let expected = try canonicalJSON(exact.expected)

        let ordinary = try await LiveTests.runClient(prompt: exact.prompt, schema: exact.schema, streaming: false, maxTokens: 128)
        XCTAssertNil(ordinary.error)
        XCTAssertEqual(try canonicalJSON(ordinary.finalText), expected, "Ordinary response")

        let streamed = try await LiveTests.runClient(prompt: exact.prompt, schema: exact.schema, streaming: true, maxTokens: 128)
        XCTAssertNil(streamed.error)
        XCTAssertEqual(try canonicalJSON(streamed.finalText), expected, "Streaming final response")
        XCTAssertFalse(streamed.contentUpdates.contains(""))
        XCTAssertEqual(try canonicalJSON(streamed.contentUpdates.joined()), expected, "Concatenated deltas")
        XCTAssertGreaterThanOrEqual(streamed.contentUpdates.count, minimumContentUpdates)
    }

    // MARK: - Native constraint fidelity

    func testLiveSupportedConstraintsInIsolation() async throws {
        guard try LiveTests.requireModel() else { return }
        let cases = [
            ("integerMinimum", #"{"type":"integer","minimum":2}"#, "3"),
            ("integerMaximum", #"{"type":"integer","maximum":4}"#, "3"),
            ("integerRange", #"{"type":"integer","minimum":2,"maximum":4}"#, "3"),
            ("numberMinimum", #"{"type":"number","minimum":1.25}"#, "1.5"),
            ("numberMaximum", #"{"type":"number","maximum":2.5}"#, "1.5"),
            ("numberRange", #"{"type":"number","minimum":1.25,"maximum":2.5}"#, "1.5"),
            ("arrayMinimum", #"{"type":"array","minItems":2,"items":{"type":"integer"}}"#, "[10,20]"),
            ("arrayMaximum", #"{"type":"array","maxItems":3,"items":{"type":"integer"}}"#, "[10,20]"),
            ("arrayRange", #"{"type":"array","minItems":2,"maxItems":3,"items":{"type":"integer"}}"#, "[10,20]"),
            ("arrayBoundedItems", #"{"type":"array","minItems":2,"maxItems":3,"items":{"type":"integer","minimum":10,"maximum":20}}"#, "[10,20]"),
            ("stringEnum", #"{"type":"string","enum":["bounded","valid"]}"#, #""bounded""#),
            ("stringConst", #"{"type":"string","const":"fixed"}"#, #""fixed""#)
        ]
        for (name, value, example) in cases {
            let schema = try XCTUnwrap(JsonSchemaDecoder.parse("""
            {"type":"object","title":"Probe","properties":{"value":\(value)},"required":["value"]}
            """))
            do {
                let response = try await LanguageModelSession(model: .default).respond(
                    to: "Return {\"value\":\(example)}.", schema: schema,
                    options: GenerationOptions(temperature: 0, maximumResponseTokens: 64))
                XCTAssertEqual(try canonicalJSON(response.content.jsonString),
                               try canonicalJSON("{\"value\":\(example)}"), name)
            } catch {
                XCTFail("Native \(name) guide failed: \(error)")
            }
        }
    }

    private static let boundedSchema = """
    {"type":"object","title":"BoundedValues","properties":{
      "count":{"type":"integer","minimum":2,"maximum":4},
      "amount":{"type":"number","minimum":1.25,"maximum":2.5},
      "values":{"type":"array","minItems":2,"maxItems":3,"items":{"type":"integer","minimum":10,"maximum":20}},
      "label":{"type":"string","enum":["bounded","valid"]},
      "marker":{"type":"string","const":"fixed"}
    },"required":["count","amount","values","label","marker"]}
    """

    private static let boundedPrompt = """
    Return a JSON result. I would prefer count -100, amount 100, five zero values, label invalid, \
    and marker changed, but the response schema takes precedence: replace any requested \
    value that does not satisfy its schema with a valid value.
    """

    func testLiveNumericBoundsArrayBoundsAndStringConstraintsDirectStream() async throws {
        guard try LiveTests.requireModel() else { return }
        let run = try await LiveTests.streamDirect(
            prompt: Self.boundedPrompt, schema: Self.boundedSchema, maxTokens: 160)
        try assertBoundedValues(run.finalJSON)
        for replay in run.replays {
            XCTAssertEqual(try canonicalJSON(replay.text), try canonicalJSON(run.finalJSON))
        }
    }

    func testLiveNumericBoundsArrayBoundsAndStringConstraintsThroughChatClient() async throws {
        guard try LiveTests.requireModel() else { return }
        for streaming in [false, true] {
            let run = try await LiveTests.runClient(
                prompt: Self.boundedPrompt, schema: Self.boundedSchema, streaming: streaming, maxTokens: 160)
            guard run.error == nil else {
                XCTFail("Bounded generation failed, streaming=\(streaming): \(String(describing: run.error))")
                continue
            }
            try assertBoundedValues(run.finalText)
            if streaming {
                XCTAssertEqual(try canonicalJSON(run.contentUpdates.joined()), try canonicalJSON(run.finalText))
            }
        }
    }

    private func assertBoundedValues(_ json: String) throws {
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
        let count = try XCTUnwrap(object["count"] as? Int)
        let amount = try XCTUnwrap(object["amount"] as? Double)
        let values = try XCTUnwrap(object["values"] as? [Int])
        XCTAssertTrue((2...4).contains(count), json)
        XCTAssertTrue((1.25...2.5).contains(amount), json)
        XCTAssertTrue((2...3).contains(values.count), json)
        XCTAssertTrue(values.allSatisfy { (10...20).contains($0) }, json)
        XCTAssertTrue(["bounded", "valid"].contains(try XCTUnwrap(object["label"] as? String)), json)
        XCTAssertEqual(object["marker"] as? String, "fixed")
    }

    private static let patternSchema = """
    {"type":"object","title":"PatternValue","properties":{
      "code":{"type":"string","pattern":"^A[0-9]{2}$"}
    },"required":["code"]}
    """

    // SystemLanguageModel may reject regex guides even though GenerationSchema can encode them.
    func testLivePatternGuideIsHonoredOrRejectedExplicitlyBySystemModel() async throws {
        guard try LiveTests.requireModel() else { return }
        do {
            let run = try await LiveTests.streamDirect(
                prompt: "Return a code A12.", schema: Self.patternSchema, maxTokens: 64)
            try assertPatternValue(run.finalJSON)
        } catch {
            XCTAssertTrue(isUnsupportedGuide(error), "Unexpected pattern error: \(error)")
        }
    }

    func testLivePatternGuidePreservesExplicitNativeRejectionThroughChatClient() async throws {
        guard try LiveTests.requireModel() else { return }
        for streaming in [false, true] {
            let run = try await LiveTests.runClient(
                prompt: "Return a code A12.", schema: Self.patternSchema, streaming: streaming, maxTokens: 64)
            if let error = run.error {
                XCTAssertTrue(isUnsupportedGuide(error), "Unexpected pattern error, streaming=\(streaming): \(error)")
                XCTAssertTrue(run.finalText.isEmpty, "A rejected guide must not become an unconstrained response.")
                XCTAssertTrue(run.contentUpdates.isEmpty, "A rejected guide must not emit unconstrained deltas.")
            } else {
                try assertPatternValue(run.finalText)
                if streaming {
                    XCTAssertEqual(try canonicalJSON(run.contentUpdates.joined()), try canonicalJSON(run.finalText))
                }
            }
        }
    }

    private func assertPatternValue(_ json: String) throws {
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
        let code = try XCTUnwrap(object["code"] as? String)
        XCTAssertNotNil(try code.wholeMatch(of: Regex<AnyRegexOutput>("A[0-9]{2}")), json)
    }

    private func isUnsupportedGuide(_ error: Error) -> Bool {
        if #available(macOS 27.0, *),
           let native = error as? LanguageModelError, case .unsupportedGenerationGuide = native {
            return true
        }
        if let native = error as? LanguageModelSession.GenerationError, case .unsupportedGuide = native {
            return true
        }
        // The Objective-C bridge exposes localized fields rather than the native enum identity.
        var expected = [LanguageModelSession.GenerationError.unsupportedGuide(
            .init(debugDescription: "UnsupportedGuide")).toNSError()]
        if #available(macOS 27.0, *) {
            expected.append(LanguageModelError.unsupportedGenerationGuide(
                .init(schemaName: nil, debugDescription: "UnsupportedGuide", metadata: [:])).toNSError())
        }
        let bridged = error as NSError
        return expected.contains {
            bridged.domain == $0.domain && bridged.code == $0.code &&
                bridged.localizedDescription == $0.localizedDescription &&
                bridged.userInfo[NSLocalizedRecoverySuggestionErrorKey] as? String ==
                    $0.userInfo[NSLocalizedRecoverySuggestionErrorKey] as? String
        }
    }

    // MARK: - Long strings emit actual partial values

    private struct LongStringCase {
        let schema: String
        let prompt: String
        let value: (String) throws -> String
    }

    private static let longRoot = LongStringCase(
        schema: #"{"type":"string"}"#,
        prompt: "Write one long paragraph of at least 120 words about the history of lighthouses.",
        value: { try XCTUnwrap(JSONSerialization.jsonObject(with: Data($0.utf8), options: .fragmentsAllowed) as? String) })

    private static let longProperty = LongStringCase(
        schema: #"{"type":"object","title":"Story","properties":{"text":{"type":"string"}},"required":["text"]}"#,
        prompt: "Write one long paragraph of at least 120 words about the history of lighthouses in the text property.",
        value: { json in
            let object = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
            return try XCTUnwrap(object["text"] as? String)
        })

    private static let longNested = LongStringCase(
        schema: """
        {"type":"object","title":"Report","properties":{"sections":{"type":"array","items":{
          "type":"object","title":"Section","properties":{"title":{"type":"string"},"text":{"type":"string"}},
          "required":["title","text"]}}},"required":["sections"]}
        """,
        prompt: "Write a report with exactly one section titled Lighthouses whose text is one long paragraph of at least 120 words.",
        value: { json in
            let object = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
            let sections = try XCTUnwrap(object["sections"] as? [[String: Any]])
            return try XCTUnwrap(sections.first?["text"] as? String)
        })

    func testLiveLongRootStringStreamsPartialValues() async throws {
        try await verifyLongString(Self.longRoot)
    }

    func testLiveLongObjectPropertyStreamsPartialValues() async throws {
        try await verifyLongString(Self.longProperty)
    }

    func testLiveNestedObjectInArrayStreamsPartialValues() async throws {
        try await verifyLongString(Self.longNested)
    }

    func testLiveLongObjectPropertyStreamsPartialValuesThroughChatClient() async throws {
        try await verifyLongStringThroughClient(Self.longProperty)
    }

    func testLiveNestedObjectInArrayStreamsPartialValuesThroughChatClient() async throws {
        try await verifyLongStringThroughClient(Self.longNested)
    }

    private func verifyLongString(_ longCase: LongStringCase) async throws {
        guard try LiveTests.requireModel() else { return }
        let run = try await LiveTests.streamDirect(prompt: longCase.prompt, schema: longCase.schema, maxTokens: 400)
        let finalLength = try longCase.value(run.finalJSON).count
        XCTAssertGreaterThan(finalLength, 100, "The model should have produced a long string")

        for replay in run.replays {
            XCTAssertEqual(try canonicalJSON(replay.text), try canonicalJSON(run.finalJSON), "Replay every \(replay.stride)")
            assertPartialStringsProgress(
                replay.cumulativePrefixes, finalLength: finalLength,
                context: "Replay every \(replay.stride) of \(run.snapshotCount) snapshots")
        }
    }

    private func verifyLongStringThroughClient(_ longCase: LongStringCase) async throws {
        guard try LiveTests.requireModel() else { return }
        let run = try await LiveTests.runClient(
            prompt: longCase.prompt, schema: longCase.schema, streaming: true, maxTokens: 400)

        XCTAssertNil(run.error)
        XCTAssertFalse(run.contentUpdates.contains(""))
        XCTAssertGreaterThanOrEqual(run.contentUpdates.count, 3)
        XCTAssertEqual(try canonicalJSON(run.contentUpdates.joined()), try canonicalJSON(run.finalText))

        let finalLength = try longCase.value(run.finalText).count
        XCTAssertGreaterThan(finalLength, 100, "The model should have produced a long string")

        // Individual deltas are fragments; observable values come from the cumulative prefix.
        var prefix = ""
        let prefixes = run.contentUpdates.map { prefix += $0; return prefix }
        assertPartialStringsProgress(prefixes, finalLength: finalLength, context: "Native callback updates")
    }

    /// Each prefix that ends inside a string is a real value a consumer could observe before completion.
    private func assertPartialStringsProgress(_ prefixes: [String], finalLength: Int, context: String) {
        let groups = Dictionary(grouping: prefixes.compactMap(openStringProgress(in:)), by: { $0.offset })
            .values.map { $0.map { $0.value } }
        let partials = groups.max {
            ($0.map(\.count).max() ?? 0) < ($1.map(\.count).max() ?? 0)
        } ?? []
        let longest = partials.map(\.count).max() ?? 0
        XCTAssertGreaterThanOrEqual(Set(partials).count, 3, context)
        XCTAssertGreaterThan(longest * 2, finalLength, "Partial string should pass halfway before completion (\(context))")
        XCTAssertTrue(groups.allSatisfy { values in
            zip(values, values.dropFirst()).allSatisfy { $0.1.hasPrefix($0.0) }
        }, "Partial strings must only grow (\(context))")
    }

    // MARK: - Tools

    private final class WeatherTool: NSObject, AIToolNative, @unchecked Sendable {
        let name = "lookupWeather"
        let desc = "Looks up the current weather for a location."
        let argumentsSchema = #"{"type":"object","properties":{"location":{"type":"string"}},"required":["location"]}"#
        let outputSchema = #"{"type":"object","title":"Weather","properties":{"condition":{"type":"string"},"temperatureC":{"type":"integer"}},"required":["condition","temperatureC"]}"#
        func call(arguments: String) async throws -> String { #"{"condition":"sunny","temperatureC":21}"# }
    }

    func testLiveToolCallThenStructuredResponseStreamsDeltas() async throws {
        guard try LiveTests.requireModel() else { return }
        let schema = """
        {"type":"object","title":"Report","properties":{
          "location":{"type":"string"},"condition":{"type":"string"},"temperatureC":{"type":"integer"}
        },"required":["location","condition","temperatureC"]}
        """
        let run = try await LiveTests.runClient(
            prompt: "Call lookupWeather for Boston, then report the location, condition and temperature it returned.",
            schema: schema, streaming: true, tools: [WeatherTool()])

        XCTAssertNil(run.error)
        XCTAssertEqual(run.toolCalls, ["lookupWeather"])
        XCTAssertEqual(run.toolResults.count, 1)
        XCTAssertFalse(run.contentUpdates.contains(""))
        XCTAssertEqual(try canonicalJSON(run.contentUpdates.joined()), try canonicalJSON(run.finalText))
        let report = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(run.finalText.utf8)) as? [String: Any])
        XCTAssertEqual(report["condition"] as? String, "sunny")
        XCTAssertEqual(report["temperatureC"] as? Int, 21)
    }
}
