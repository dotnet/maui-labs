import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

/// Deep, key-order-insensitive JSON representation for comparing structured responses.
func canonicalJSON(_ json: String) throws -> String {
    let value = try JSONSerialization.jsonObject(with: Data(json.utf8), options: .fragmentsAllowed)
    let data = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys, .fragmentsAllowed])
    return String(decoding: data, as: UTF8.self)
}

/// Identifies the open string by its stable offset in the append-only JSON prefix.
func openStringProgress(in prefix: String) -> (offset: Int, value: String)? {
    var inString = false
    var escaped = false
    var start = prefix.startIndex
    for index in prefix.indices {
        let character = prefix[index]
        if inString {
            if escaped { escaped = false }
            else if character == "\\" { escaped = true }
            else if character == "\"" { inString = false }
        } else if character == "\"" {
            inString = true
            start = prefix.index(after: index)
        }
    }
    guard inString, !escaped else { return nil }
    guard let value = try? JSONSerialization.jsonObject(
        with: Data(("\"" + prefix[start...] + "\"").utf8), options: .fragmentsAllowed) as? String else {
        return nil
    }
    return (prefix.distance(from: prefix.startIndex, to: start), value)
}

enum LiveTests {
    /// Returns false (after failing the test) when live tests were requested but the model is unavailable.
    static func requireModel() throws -> Bool {
        try XCTSkipUnless(ProcessInfo.processInfo.environment["ESSENTIALSAI_LIVE_TESTS"] == "1",
                          "Set ESSENTIALSAI_LIVE_TESTS=1 to run on-device model tests.")
        guard case .available = SystemLanguageModel.default.availability else {
            XCTFail("Live tests were requested, but Apple Intelligence is unavailable.")
            return false
        }
        return true
    }

    /// One real model stream replayed through adapters that each see every Nth snapshot, simulating
    /// snapshots that arrive coalesced.
    struct DirectRun {
        struct Replay {
            let stride: Int
            /// Non-empty deltas produced before `collect()` completes.
            let streamed: [String]
            /// The remaining delta produced by the authoritative final content.
            let finalDelta: String

            var text: String { streamed.joined() + finalDelta }
            var cumulativePrefixes: [String] {
                var prefix = ""
                return streamed.map { prefix += $0; return prefix }
            }
        }

        let snapshotCount: Int
        let finalJSON: String
        let replays: [Replay]
    }

    static func streamDirect(
        prompt: String, schema schemaJson: String, maxTokens: Int, strides: [Int] = [1, 7, 13]
    ) async throws -> DirectRun {
        let schema = try XCTUnwrap(try JsonSchemaDecoder.parse(schemaJson))
        let stream = LanguageModelSession(model: .default).streamResponse(
            to: prompt, schema: schema, options: GenerationOptions(temperature: 0, maximumResponseTokens: maxTokens))

        var adapters = strides.map { _ in StructuredJsonDeltas() }
        var streamed = strides.map { _ in [String]() }
        var count = 0
        for try await snapshot in stream {
            count += 1
            for (slot, stride) in strides.enumerated() where count % stride == 0 {
                let delta = try adapters[slot].process(snapshot.rawContent)
                if !delta.isEmpty { streamed[slot].append(delta) }
            }
        }

        let response = try await stream.collect()
        var replays: [DirectRun.Replay] = []
        for (slot, stride) in strides.enumerated() {
            let finalDelta = try adapters[slot].process(response.rawContent, final: true)
            replays.append(.init(stride: stride, streamed: streamed[slot], finalDelta: finalDelta))
        }
        return DirectRun(snapshotCount: count, finalJSON: response.content.jsonString, replays: replays)
    }

    struct ClientRun {
        var contentUpdates: [String] = []
        var toolCalls: [String] = []
        var toolResults: [String] = []
        var finalText = ""
        var error: NSError?
    }

    static func runClient(
        prompt: String, schema: String? = nil, streaming: Bool, maxTokens: Int = 256, tools: [any AIToolNative]? = nil,
        toolCallingMode: ToolCallingModeNative = .allowed, history: [ChatMessageNative] = [], timeout: TimeInterval = 180
    ) async throws -> ClientRun {
        let message = ChatMessageNative()
        message.contents = [TextContentNative(text: prompt)]
        let options = ChatOptionsNative()
        options.temperature = 0
        options.maxOutputTokens = NSNumber(value: maxTokens)
        options.responseJsonSchema = schema.map { $0 as NSString }
        options.tools = tools
        options.toolCallingMode = toolCallingMode

        let lock = NSLock()
        var run = ClientRun()
        let done = XCTestExpectation(description: "Native structured generation completes")

        let onUpdate: (ResponseUpdateNative) -> Void = { update in
            lock.withLock {
                switch update.updateType {
                case .content: run.contentUpdates.append(update.text ?? "")
                case .toolCall: run.toolCalls.append(update.toolCallName ?? "")
                case .toolResult: run.toolResults.append(update.toolCallResult ?? "")
                @unknown default: break
                }
            }
        }
        let onComplete: (ChatResponseNative?, NSError?) -> Void = { response, error in
            lock.withLock {
                run.error = error
                run.finalText = response?.messages.flatMap(\.contents)
                    .compactMap { ($0 as? TextContentNative)?.text }.joined() ?? ""
            }
            done.fulfill()
        }

        let client = ChatClientNative()
        let token = streaming
            ? client.streamResponse(messages: history + [message], options: options, onUpdate: onUpdate, onComplete: onComplete)
            : client.getResponse(messages: history + [message], options: options, onUpdate: onUpdate, onComplete: onComplete)
        defer { token?.cancel() }

        let result = await XCTWaiter.fulfillment(of: [done], timeout: timeout)
        XCTAssertEqual(result, .completed, "Native generation timed out")
        return lock.withLock { run }
    }
}
