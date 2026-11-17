import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class ApprovalModeTests: XCTestCase {
    func testResumeRequiresToolResultsForEveryPrecedingCall() throws {
        let user = message(.user, TextContentNative(text: "Hi"))
        let calls = message(.assistant,
                            FunctionCallContentNative(callId: "a", name: "tool", arguments: "{}"),
                            FunctionCallContentNative(callId: "b", name: "tool", arguments: "{}"))
        let resultA = message(.tool, FunctionResultContentNative(callId: "a", name: "tool", result: "1"))
        let resultB = message(.tool, FunctionResultContentNative(callId: "b", name: "tool", result: "2"))

        XCTAssertFalse(try ChatClientNative.isToolResultResume([user]))
        XCTAssertFalse(try ChatClientNative.isToolResultResume([user, calls, resultA, resultB, user]))
        // FICC can split rejected and approved results into separate tool messages.
        XCTAssertTrue(try ChatClientNative.isToolResultResume([user, calls, resultA, resultB]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, calls, resultA]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, resultA]))

        let callA = message(.assistant, FunctionCallContentNative(callId: "a", name: "tool", arguments: "{}"))
        let callB = message(.assistant, FunctionCallContentNative(callId: "b", name: "tool", arguments: "{}"))
        let resultC = message(.tool, FunctionResultContentNative(callId: "c", name: "tool", result: "3"))
        XCTAssertTrue(try ChatClientNative.isToolResultResume([user, callA, callB, resultB, resultA]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, callA, callB, resultA]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, callA, callB, resultA, resultB, resultC]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, callA, callB, resultA, resultA]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, callA, callA, resultA]))
        XCTAssertThrowsError(try ChatClientNative.isToolResultResume([user, callA, user, callB, resultA, resultB]))
    }

    func testApprovalModeNeedsToolsAndOS27() {
        let options = ChatOptionsNative()
        options.approvalMode = true
        XCTAssertFalse(ChatClientNative.isApprovalMode(options), "No tools means nothing to approve.")

        options.tools = [StubTool(name: "tool")]
        options.toolCallingMode = .disallowed
        XCTAssertFalse(ChatClientNative.isApprovalMode(options))

        options.toolCallingMode = .allowed
        if #available(macOS 27.0, *) {
            XCTAssertTrue(ChatClientNative.isApprovalMode(options))
        } else {
            XCTAssertFalse(ChatClientNative.isApprovalMode(options), "OS 26 has no onToolCall hook.")
        }
    }

    func testEmptyResumePromptIsFiltered() {
        XCTAssertTrue(ChatClientNative.isEmptyPrompt(.prompt(Transcript.Prompt(segments: [.text(.init(content: ""))]))))
        XCTAssertFalse(ChatClientNative.isEmptyPrompt(.prompt(Transcript.Prompt(segments: [.text(.init(content: "Hi"))]))))
    }

    func testLiveApprovalVetoesThenResumesForBothAPIsAndFormats() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("Approval mode needs OS 27.")
        }
        guard try LiveTests.requireModel() else { return }
        let schema = """
        {"type":"object","title":"WeatherResult","properties":{"summary":{"type":"string"}},"required":["summary"]}
        """
        for streaming in [false, true] {
            for structured in [false, true] {
                let label = "streaming=\(streaming), structured=\(structured)"
                let tool = StubTool(name: "getWeather")
                let prompt = "What is the weather in Paris? Use the getWeather tool."
                let first = try await LiveTests.runClient(
                    prompt: prompt, schema: structured ? schema : nil, streaming: streaming, maxTokens: 128,
                    tools: [tool], approvalMode: true, timeout: 60)

                XCTAssertNil(first.error, label)
                XCTAssertEqual(tool.callCount, 0, "Approval mode must not run tools natively. \(label)")
                let response = try XCTUnwrap(first.response, label)
                XCTAssertTrue(response.hasPendingToolCalls, label)
                let calls = response.messages.flatMap(\.contents).compactMap { $0 as? FunctionCallContentNative }
                XCTAssertFalse(calls.isEmpty, label)
                XCTAssertTrue(calls.allSatisfy { $0.name == tool.name }, label)
                XCTAssertFalse(response.messages.contains { $0.role == .user }, "Pending responses hold only this turn's output.")
                if streaming {
                    XCTAssertEqual(first.pendingToolCalls.map(\.id), calls.map(\.callId), label)
                } else {
                    XCTAssertTrue(first.pendingToolCalls.isEmpty, label)
                }

                // Resume as FICC would: the turn's output followed by one tool result per call.
                let user = message(.user, TextContentNative(text: prompt))
                let results = calls.map {
                    message(.tool, FunctionResultContentNative(callId: $0.callId, name: $0.name, result: "Sunny, 22C"))
                }
                let resumed = try await LiveTests.runClient(
                    prompt: nil, schema: structured ? schema : nil, streaming: streaming, maxTokens: 128,
                    tools: [tool], history: [user] + response.messages + results, approvalMode: true, timeout: 60)

                XCTAssertNil(resumed.error, label)
                XCTAssertEqual(tool.callCount, 0, label)
                let final = try XCTUnwrap(resumed.response, label)
                XCTAssertFalse(final.hasPendingToolCalls, label)
                XCTAssertFalse(final.messages.contains { $0.role == .user }, "The empty resume prompt must not leak.")
                XCTAssertTrue(resumed.finalText.contains("22") || resumed.finalText.lowercased().contains("sunny"),
                              "\(label): \(resumed.finalText)")
            }
        }
    }

    func testLiveRequiredApprovalResumeDoesNotForceAnotherCall() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("Approval mode needs OS 27.")
        }
        guard try LiveTests.requireModel() else { return }
        let tool = StubTool(name: "getWeather")
        let prompt = "What is the weather in Paris?"
        let first = try await LiveTests.runClient(
            prompt: prompt, streaming: false, maxTokens: 128, tools: [tool], toolCallingMode: .required,
            approvalMode: true, timeout: 60)
        let response = try XCTUnwrap(first.response)
        XCTAssertTrue(response.hasPendingToolCalls)
        let calls = response.messages.flatMap(\.contents).compactMap { $0 as? FunctionCallContentNative }

        let results = calls.map {
            message(.tool, FunctionResultContentNative(callId: $0.callId, name: $0.name, result: "Sunny, 22C"))
        }
        let resumed = try await LiveTests.runClient(
            prompt: nil, streaming: false, maxTokens: 128, tools: [tool], toolCallingMode: .required,
            history: [message(.user, TextContentNative(text: prompt))] + response.messages + results,
            approvalMode: true, timeout: 60)
        XCTAssertNil(resumed.error)
        XCTAssertEqual(resumed.response?.hasPendingToolCalls, false, "A resumed turn must not be forced into another tool call.")
        XCTAssertFalse(resumed.finalText.isEmpty)
    }

    func testLiveSplitCallsWithInterleavedTextResumeAndReplayForBothAPIs() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("Approval mode needs OS 27.")
        }
        guard try LiveTests.requireModel() else { return }

        for streaming in [false, true] {
            let firstTool = StubTool(name: "firstLookup")
            let secondTool = StubTool(name: "secondLookup")
            let user = message(.user, TextContentNative(text: "Summarize the two lookup results without calling tools again."))
            let firstCall = message(.assistant,
                TextContentNative(text: "Checking the first result."),
                FunctionCallContentNative(callId: "a", name: firstTool.name, arguments: #"{"city":"Paris"}"#))
            let secondCall = message(.assistant,
                TextContentNative(text: "Checking the second result."),
                FunctionCallContentNative(callId: "b", name: secondTool.name, arguments: #"{"city":"Paris"}"#))
            let firstResult = message(.tool, FunctionResultContentNative(callId: "a", name: firstTool.name, result: "Sunny"))
            let secondResult = message(.tool, FunctionResultContentNative(callId: "b", name: secondTool.name, result: "22C"))
            let history = [user, firstCall, secondCall, firstResult, secondResult]

            let resumed = try await LiveTests.runClient(
                prompt: nil, streaming: streaming, maxTokens: 128, tools: [firstTool, secondTool],
                history: history, approvalMode: true, timeout: 60)
            XCTAssertNil(resumed.error)
            let response = try XCTUnwrap(resumed.response)
            XCTAssertFalse(response.hasPendingToolCalls)
            XCTAssertTrue(resumed.finalText.contains("22"), resumed.finalText)

            let next = try await LiveTests.runClient(
                prompt: "Reply with a short goodbye.", streaming: streaming, maxTokens: 64,
                history: history + response.messages, timeout: 60)
            XCTAssertNil(next.error)
            XCTAssertFalse(next.finalText.isEmpty)
            XCTAssertEqual(firstTool.callCount, 0)
            XCTAssertEqual(secondTool.callCount, 0)
        }
    }

    // Foundation Models must not return an earlier transcript's calls when a resumed turn is vetoed again.
    func testLiveSecondApprovalReturnsOnlyNewCallsForBothAPIs() async throws {
        guard #available(macOS 27.0, *) else {
            throw XCTSkip("Approval mode needs OS 27.")
        }
        guard try LiveTests.requireModel() else { return }

        for streaming in [false, true] {
            let firstTool = StubTool(name: "getForecast")
            let secondTool = StubTool(name: "getTemperature")
            let user = message(.user, TextContentNative(text: """
                First call getForecast for Paris. Once you get its result, call getTemperature for Paris.
                Then answer with the temperature. Do not call either tool again.
                """))
            let first = try await LiveTests.runClient(
                prompt: nil, streaming: streaming, maxTokens: 128, tools: [firstTool], toolCallingMode: .required,
                history: [user], approvalMode: true, timeout: 60)
            XCTAssertNil(first.error)
            let firstResponse = try XCTUnwrap(first.response)
            XCTAssertTrue(firstResponse.hasPendingToolCalls)
            let firstCalls = firstResponse.messages.flatMap(\.contents).compactMap { $0 as? FunctionCallContentNative }
            XCTAssertEqual(firstCalls.map(\.name), ["getForecast"])
            let firstCall = try XCTUnwrap(firstCalls.first)
            let firstResult = message(.tool, FunctionResultContentNative(
                callId: firstCall.callId, name: firstCall.name,
                result: "Now call getTemperature for Paris to obtain the temperature."))

            let second = try await LiveTests.runClient(
                prompt: nil, streaming: streaming, maxTokens: 128, tools: [firstTool, secondTool],
                history: [user] + firstResponse.messages + [firstResult], approvalMode: true, timeout: 60)
            XCTAssertNil(second.error)
            let secondResponse = try XCTUnwrap(second.response)
            XCTAssertTrue(secondResponse.hasPendingToolCalls)
            let secondCalls = secondResponse.messages.flatMap(\.contents).compactMap { $0 as? FunctionCallContentNative }
            XCTAssertEqual(secondCalls.map(\.name), ["getTemperature"])
            XCTAssertFalse(secondResponse.messages.flatMap(\.contents).contains { $0 is FunctionResultContentNative })
            let secondCall = try XCTUnwrap(secondCalls.first)
            XCTAssertNotEqual(firstCall.callId, secondCall.callId)
            if streaming {
                XCTAssertEqual(second.pendingToolCalls.map(\.id), [secondCall.callId])
                XCTAssertTrue(second.toolCalls.isEmpty)
                XCTAssertTrue(second.toolResults.isEmpty)
            }

            let secondResult = message(.tool, FunctionResultContentNative(
                callId: secondCall.callId, name: secondCall.name, result: "Sunny, 22C"))
            let final = try await LiveTests.runClient(
                prompt: nil, streaming: streaming, maxTokens: 128, tools: [firstTool, secondTool],
                history: [user] + firstResponse.messages + [firstResult] + secondResponse.messages + [secondResult],
                approvalMode: true, timeout: 60)
            XCTAssertNil(final.error)
            XCTAssertFalse(try XCTUnwrap(final.response).hasPendingToolCalls)
            XCTAssertTrue(final.finalText.contains("22"), final.finalText)
            XCTAssertEqual(firstTool.callCount, 0)
            XCTAssertEqual(secondTool.callCount, 0)
        }
    }

    private func message(_ role: ChatRoleNative, _ contents: AIContentNative...) -> ChatMessageNative {
        let message = ChatMessageNative()
        message.role = role
        message.contents = contents
        return message
    }

    private final class StubTool: NSObject, AIToolNative, @unchecked Sendable {
        let name: String
        let desc = "Gets the current weather for a city."
        let argumentsSchema = #"{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"#
        let outputSchema = #"{"type":"string"}"#
        private let lock = NSLock()
        private var calls = 0

        init(name: String) { self.name = name }

        var callCount: Int { lock.withLock { calls } }

        func call(arguments: String) async throws -> String {
            lock.withLock { calls += 1 }
            return #""Sunny, 22C""#
        }
    }
}
