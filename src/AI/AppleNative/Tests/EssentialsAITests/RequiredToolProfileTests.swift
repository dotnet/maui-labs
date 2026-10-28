import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class RequiredToolProfileTests: XCTestCase {
    func testModeBecomesAllowedAfterFirstCallAndStaysAllowed() {
        guard #available(macOS 27.0, *) else { return }
        XCTAssertEqual(RequiredToolProfile.toolCallingMode(for: 0), .required)
        for count in [1, 2, Int.max] {
            XCTAssertEqual(RequiredToolProfile.toolCallingMode(for: count), .allowed)
        }
    }

    func testCounterIsSessionLocalAndRuntimeToolsAreRetained() throws {
        guard #available(macOS 27.0, *) else { return }
        let tools = try [ToolNative(CountingTool(name: "first"), nil, nil),
                     ToolNative(CountingTool(name: "second"), nil, nil)]
        let profile = RequiredToolProfile(model: .default, tools: tools)
        XCTAssertEqual(profile.tools.map(\.name), ["first", "second"])
        XCTAssertTrue(profile.tools[0] as? ToolNative === tools[0])
        XCTAssertTrue(profile.tools[1] as? ToolNative === tools[1])

        let first = ChatClientNative.makeSession(model: .default, tools: tools, transcript: Transcript(),
                                                toolCallingMode: .required)
        let second = ChatClientNative.makeSession(model: .default, tools: tools, transcript: Transcript(),
                                                 toolCallingMode: .required)
        XCTAssertEqual(first.properties.essentialsAIRequiredToolCallCount, 0)
        first.properties.essentialsAIRequiredToolCallCount += 1
        XCTAssertEqual(RequiredToolProfile.toolCallingMode(for: first.properties.essentialsAIRequiredToolCallCount), .allowed)
        XCTAssertEqual(second.properties.essentialsAIRequiredToolCallCount, 0)
    }

    func testRequiredAndOrdinarySessionsPreserveTranscriptHistory() throws {
        guard #available(macOS 27.0, *) else { return }
        let client = ChatClientNative()
        let system = ChatMessageNative()
        system.role = .system
        system.contents = [TextContentNative(text: "Remember the supplied history.")]
        let user = ChatMessageNative()
        user.role = .user
        user.contents = [TextContentNative(text: "Look up the previous value.")]
        let assistant = ChatMessageNative()
        assistant.role = .assistant
        assistant.contents = [FunctionCallContentNative(callId: "previous-call", name: "previousTool", arguments: "{}")]
        let result = ChatMessageNative()
        result.role = .tool
        result.contents = [FunctionResultContentNative(callId: "previous-call", name: "previousTool", result: "previous-value")]
        let history = try Transcript(entries: [system, user, assistant, result].flatMap(client.toTranscriptEntries))

        for mode in [ToolCallingModeNative.required, .allowed, .disallowed] {
            let session = ChatClientNative.makeSession(
                model: .default, tools: [], transcript: history, toolCallingMode: mode)
            XCTAssertEqual(Array(session.transcript.suffix(history.count)), Array(history))
            if mode == .required {
                XCTAssertEqual(session.properties.essentialsAIRequiredToolCallCount, 0,
                               "Replayed tool history must not satisfy this request's Required mode.")
            }
        }
    }

    func testLiveRequiredToolsReachFinalTextForBothAPIsAndFormats() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("Required tool profiles need OS 27.")
        }
        guard try LiveTests.requireModel() else { return }
        let schema = """
        {"type":"object","title":"RequiredResult","properties":{
          "value":{"type":"string"}
        },"required":["value"]}
        """
        for streaming in [false, true] {
            for structured in [false, true] {
                let tool = CountingTool(name: "lookupRequiredValue")
                let run = try await LiveTests.runClient(
                    prompt: """
                    Return the value for key secret. Once you know the value, give the final answer \
                    containing that value and do not call the tool again.
                    """,
                    schema: structured ? schema : nil, streaming: streaming, maxTokens: 128,
                    tools: [tool], toolCallingMode: .required, timeout: 45)

                XCTAssertNil(run.error, "streaming=\(streaming), structured=\(structured)")
                XCTAssertEqual(tool.callCount, 1, "Required must stop forcing calls after the first invocation.")
                XCTAssertEqual(run.toolCalls, [tool.name])
                XCTAssertEqual(run.toolResults.count, 1)
                XCTAssertTrue(run.finalText.contains("REQUIRED_VALUE_42"), run.finalText)
                if structured {
                    let value = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(run.finalText.utf8)) as? [String: Any])
                    XCTAssertEqual(value["value"] as? String, "REQUIRED_VALUE_42")
                }
                if streaming {
                    if structured {
                        XCTAssertEqual(try canonicalJSON(run.contentUpdates.joined()), try canonicalJSON(run.finalText))
                    } else {
                        XCTAssertEqual(run.contentUpdates.joined(), run.finalText)
                    }
                }
            }
        }
    }

    func testLiveRequiredNullableParameterToolExecutesExactlyOnceForBothAPIs() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("Required tool profiles need OS 27.")
        }
        guard try LiveTests.requireModel() else { return }
        for streaming in [false, true] {
            let tool = CountingTool(name: "lookupNullableValue", nullableParameters: true)
            let run = try await LiveTests.runClient(
                prompt: """
                Look up the secret value with lookupNullableValue. There is no numeric value: \
                pass JSON null for the required value parameter, not zero or a string. \
                The optional label can be omitted or null. After the tool returns, answer with its \
                returned secret value and do not call it again.
                """,
                streaming: streaming, maxTokens: 128, tools: [tool], toolCallingMode: .required, timeout: 45)

            XCTAssertNil(run.error, "streaming=\(streaming): \(String(describing: run.error))")
            XCTAssertEqual(tool.callCount, 1, "Required nullable arguments must not cause repeated execution.")
            XCTAssertEqual(run.toolCalls, [tool.name])
            XCTAssertEqual(run.toolResults.count, 1)
            XCTAssertTrue(run.finalText.contains("REQUIRED_VALUE_42"), run.finalText)
            let arguments = try XCTUnwrap(tool.receivedArguments.first)
            let json = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(arguments.utf8)) as? [String: Any])
            XCTAssertTrue(json["value"] is NSNull, "Required nullable value must be present as JSON null: \(arguments)")
            XCTAssertTrue(json["label"] == nil || json["label"] is NSNull, arguments)
            if streaming { XCTAssertEqual(run.contentUpdates.joined(), run.finalText) }
        }
    }

    private final class CountingTool: NSObject, AIToolNative, @unchecked Sendable {
        let name: String
        let desc: String
        let argumentsSchema: String
        let outputSchema = #"{"type":"string"}"#
        private let lock = NSLock()
        private var calls = 0
        private var arguments: [String] = []

        init(name: String, nullableParameters: Bool = false) {
            self.name = name
            desc = nullableParameters
                ? "Looks up the secret value. Pass null for value when no number is supplied; label is optional. Call only once, then answer using the returned value."
                : "Looks up the secret value for a key. Call only once, then answer using the returned value."
            argumentsSchema = nullableParameters
                ? #"{"type":"object","properties":{"value":{"type":["integer","null"]},"label":{"type":["string","null"]}},"required":["value"]}"#
                : #"{"type":"object","properties":{"key":{"type":"string"}},"required":["key"]}"#
        }

        var callCount: Int { lock.withLock { calls } }
        var receivedArguments: [String] { lock.withLock { arguments } }

        func call(arguments: String) async throws -> String {
            let count = lock.withLock { calls += 1; self.arguments.append(arguments); return calls }
            guard count <= 3 else {
                throw NSError(domain: "RequiredToolProfileTests", code: 1,
                              userInfo: [NSLocalizedDescriptionKey: "Required mode exceeded the bounded tool-call budget."])
            }
            return #""REQUIRED_VALUE_42""#
        }
    }
}
