import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class PlainTextStreamingLiveTests: XCTestCase {
    func testLiveSnapshotsReplayToCollectedResponse() async throws {
        guard try LiveTests.requireModel() else { return }
        let cases = [
            ("Write one paragraph of at least 120 words about lighthouses.", 100, 2),
            // Foundation Models may coalesce a short Unicode reply into one snapshot.
            ("Reply with exactly: A😀e\u{301}👨‍👩‍👧‍👦Z", 5, 1),
        ]

        for (prompt, minimumLength, minimumChunks) in cases {
            let stream = LanguageModelSession(model: .default).streamResponse(
                to: prompt,
                options: GenerationOptions(temperature: 0, maximumResponseTokens: 400))
            var deltas = PlainTextDeltas()
            var chunks: [String] = []
            var completionStates: [Bool] = []

            for try await snapshot in stream {
                completionStates.append(snapshot.rawContent.isComplete)
                let delta = try deltas.process(snapshot.content)
                if !delta.isEmpty {
                    chunks.append(delta)
                }
            }

            XCTAssertGreaterThanOrEqual(chunks.count, minimumChunks)
            let response = try await stream.collect()
            let finalDelta = try deltas.process(response.content)
            if !finalDelta.isEmpty {
                chunks.append(finalDelta)
            }

            XCTAssertGreaterThanOrEqual(response.content.count, minimumLength)
            XCTAssertEqual(chunks.joined(), response.content)
            XCTAssertEqual(Array(chunks.joined().utf8), Array(response.content.utf8))
            XCTAssertGreaterThanOrEqual(chunks.count, minimumChunks)
            XCTAssertEqual(completionStates.last, true)
            XCTAssertTrue(completionStates.dropLast().allSatisfy { !$0 })
        }
    }

    func testLiveNativeClientEmitsPlainTextDeltas() async throws {
        guard try LiveTests.requireModel() else { return }
        let run = await runClient(
            prompt: "Write one paragraph of at least 120 words about lighthouses.",
            tools: nil)

        XCTAssertNil(run.error)
        XCTAssertGreaterThan(run.finalText.count, 100)
        XCTAssertGreaterThanOrEqual(run.contentUpdates.count, 3)
        XCTAssertFalse(run.contentUpdates.contains(""))
        XCTAssertEqual(run.contentUpdates.joined(), run.finalText)
    }

    func testLiveToolCallbacksPrecedePlainTextDeltas() async throws {
        guard try LiveTests.requireModel() else { return }
        let run = await runClient(
            prompt: "Call lookupWeather for Boston and then for Seattle. Summarize both results.",
            tools: [WeatherTool()])

        XCTAssertNil(run.error)
        XCTAssertEqual(run.toolCalls, ["lookupWeather", "lookupWeather"])
        XCTAssertEqual(run.toolResults.count, 2)
        XCTAssertGreaterThanOrEqual(run.contentUpdates.count, 2)
        XCTAssertEqual(run.contentUpdates.joined(), run.finalText)
        let firstContent = try XCTUnwrap(run.events.firstIndex(of: "content"))
        let lastToolResult = try XCTUnwrap(run.events.lastIndex(of: "tool-result"))
        XCTAssertLessThan(lastToolResult, firstContent)
    }

    private struct ClientRun {
        var contentUpdates: [String] = []
        var toolCalls: [String] = []
        var toolResults: [String] = []
        var events: [String] = []
        var finalText = ""
        var error: NSError?
    }

    private func runClient(prompt: String, tools: [any AIToolNative]?) async -> ClientRun {
        let message = ChatMessageNative()
        message.contents = [TextContentNative(text: prompt)]
        let options = ChatOptionsNative()
        options.temperature = 0
        options.maxOutputTokens = 400
        options.tools = tools

        let lock = NSLock()
        var run = ClientRun()
        let done = XCTestExpectation(description: "Native plain-text generation completes")
        let token = ChatClientNative().streamResponse(
            messages: [message],
            options: options,
            onUpdate: { update in
                lock.withLock {
                    switch update.updateType {
                    case .content:
                        run.events.append("content")
                        run.contentUpdates.append(update.text ?? "")
                    case .toolCall:
                        run.events.append("tool-call")
                        run.toolCalls.append(update.toolCallName ?? "")
                    case .toolResult:
                        run.events.append("tool-result")
                        run.toolResults.append(update.toolCallResult ?? "")
                    @unknown default:
                        break
                    }
                }
            },
            onComplete: { response, error in
                lock.withLock {
                    run.error = error
                    run.finalText = response?.messages.flatMap(\.contents)
                        .compactMap { ($0 as? TextContentNative)?.text }.joined() ?? ""
                }
                done.fulfill()
            })
        defer { token?.cancel() }

        let result = await XCTWaiter.fulfillment(of: [done], timeout: 180)
        XCTAssertEqual(result, .completed, "Native generation timed out")
        return lock.withLock { run }
    }

    private final class WeatherTool: NSObject, AIToolNative, @unchecked Sendable {
        let name = "lookupWeather"
        let desc = "Returns the current weather for a location."
        let argumentsSchema =
            #"{"type":"object","properties":{"location":{"type":"string"}},"required":["location"]}"#
        let outputSchema = #"{"type":"string"}"#

        func call(arguments: String) async throws -> String {
            "The requested location is sunny and 21 degrees Celsius."
        }
    }
}
