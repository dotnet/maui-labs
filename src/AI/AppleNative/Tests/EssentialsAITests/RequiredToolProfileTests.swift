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
        let tools = [ToolNative(CountingTool(name: "first"), nil, nil),
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

    private final class CountingTool: NSObject, AIToolNative, @unchecked Sendable {
        let name: String
        let desc = "Looks up the secret value for a key. Call only once, then answer using the returned value."
        let argumentsSchema = #"{"type":"object","properties":{"key":{"type":"string"}},"required":["key"]}"#
        let outputSchema = #"{"type":"string"}"#
        private let lock = NSLock()
        private var calls = 0

        init(name: String) { self.name = name }

        var callCount: Int { lock.withLock { calls } }

        func call(arguments: String) async throws -> String {
            let count = lock.withLock { calls += 1; return calls }
            guard count <= 3 else {
                throw NSError(domain: "RequiredToolProfileTests", code: 1,
                              userInfo: [NSLocalizedDescriptionKey: "Required mode exceeded the bounded tool-call budget."])
            }
            return #""REQUIRED_VALUE_42""#
        }
    }
}
