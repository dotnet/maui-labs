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
