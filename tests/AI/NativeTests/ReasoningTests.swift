import Foundation
import FoundationModels

// Synthetic transcript/signature fixtures exercise transport, not actual model reasoning.
@main
struct ReasoningTests {
    static func require(_ condition: @autoclosure () -> Bool, _ message: String) {
        precondition(condition(), message)
    }

    static func rejects(_ operation: () throws -> Void) {
        do {
            try operation()
            preconditionFailure("Expected invalid protected reasoning to fail")
        } catch {}
    }

    static func main() throws {
        guard #available(macOS 27.0, *) else {
            print("Native reasoning host tests require macOS27; not executed.")
            return
        }
        try testNativeTransport()
        print("Native reasoning host tests passed (synthetic entries; no model loaded).")
    }

    @available(macOS 27.0, *)
    static func testNativeTransport() throws {
        let client = ChatClientNative()
        let reasoning = Transcript.Reasoning(id: "native-entry", segments: [
            .text(Transcript.TextSegment(id: "first-segment", content: "First ")),
            .text(Transcript.TextSegment(id: "second-segment", content: "thought"))
        ], signature: Data([0, 1, 2, 255]))
        let protected = try client.protectReasoning(reasoning)!
        let restored = try client.replayReasoning([
            TextReasoningContentNative(text: "First ", protectedData: nil),
            TextReasoningContentNative(text: "thought", protectedData: protected)
        ], messageId: reasoning.id)
        guard case .reasoning(let entry)? = restored else { preconditionFailure("Missing signed reasoning") }
        require(entry == reasoning, "Signed native entry was not restored exactly")
        let repeatedProtection = try client.protectReasoning(reasoning)
        require(repeatedProtection == protected, "Repeated signature serialization changed")
        let partial = Transcript.Reasoning(id: reasoning.id, segments: [reasoning.segments[0]], signature: Data([3]))
        let partialProtection = try client.protectReasoning(partial)!
        let revised = try client.replayReasoning([
            TextReasoningContentNative(text: "First ", protectedData: partialProtection),
            TextReasoningContentNative(text: "thought", protectedData: protected)
        ], messageId: reasoning.id)
        guard case .reasoning(let revisedEntry)? = revised else { preconditionFailure("Lost protection revision") }
        require(revisedEntry == reasoning, "Latest genuine native protection was not restored")
        let protectionOnly = try client.replayReasoning([
            TextReasoningContentNative(text: nil, protectedData: protected)
        ], messageId: reasoning.id)
        guard case .reasoning(let protectedEntry)? = protectionOnly else { preconditionFailure("Lost protection-only history") }
        require(protectedEntry == reasoning, "Protection-only history lost the native entry")
        let firstMessage = ChatMessageNative()
        firstMessage.role = .assistant
        firstMessage.messageId = reasoning.id
        firstMessage.contents = [
            TextReasoningContentNative(text: "First ", protectedData: partialProtection),
            TextReasoningContentNative(text: "thought", protectedData: nil)
        ]
        let callMessage = ChatMessageNative()
        callMessage.role = .assistant
        callMessage.contents = [FunctionCallContentNative(callId: "signed-call", name: "lookup", arguments: "{}")]
        let resultMessage = ChatMessageNative()
        resultMessage.role = .tool
        resultMessage.contents = [FunctionResultContentNative(callId: "signed-call", name: "lookup", result: "code")]
        let answerMessage = ChatMessageNative()
        answerMessage.role = .assistant
        answerMessage.messageId = "answer"
        answerMessage.contents = [TextContentNative(text: "Answer")]
        let finalMessage = ChatMessageNative()
        finalMessage.role = .assistant
        finalMessage.messageId = reasoning.id
        finalMessage.contents = [TextReasoningContentNative(text: nil, protectedData: protected)]
        let normalized = try client.normalizedReasoningHistory([
            firstMessage, callMessage, resultMessage, answerMessage, finalMessage
        ])
        require(normalized.count == 4, "Late protection remained after the answer")
        let firstEntries = try client.toAssistantEntries(normalized[0])
        guard case .reasoning(let historyEntry)? = firstEntries.first else { preconditionFailure("Lost signed history") }
        require(firstEntries.count == 1 && historyEntry == reasoning, "Signed history did not retain its final native entry")
        require(normalized[1] === callMessage && normalized[2] === resultMessage && normalized[3] === answerMessage,
            "Protection normalization changed answer/tool ordering")
        require(firstMessage.contents.count == 2 && finalMessage.contents.count == 1,
            "History normalization mutated caller fragments")

        rejects {
            _ = try client.replayReasoning([TextReasoningContentNative(text: "Altered", protectedData: protected)],
                messageId: reasoning.id)
        }
        rejects {
            _ = try client.replayReasoning([TextReasoningContentNative(text: nil, protectedData: protected)],
                messageId: "wrong-entry")
        }
        rejects {
            _ = try client.replayReasoning([TextReasoningContentNative(text: nil, protectedData: "azure-opaque")],
                messageId: reasoning.id)
        }
        rejects {
            _ = try client.replayReasoning([
                TextReasoningContentNative(text: nil, protectedData: "azure-opaque"),
                TextReasoningContentNative(text: nil, protectedData: protected)
            ], messageId: reasoning.id)
        }
        let otherProducer = ChatClientNative(modelDirectory: "./artifacts/native-reasoning-other-producer")
        rejects {
            _ = try otherProducer.replayReasoning([TextReasoningContentNative(text: nil, protectedData: protected)],
                messageId: reasoning.id)
        }
        let unsigned = Transcript.Reasoning(id: "unsigned", segments: reasoning.segments)
        let unsignedProtection = try client.protectReasoning(unsigned)
        require(unsignedProtection == nil, "Unsigned trace manufactured protected data")
        let unsignedReplay = try client.replayReasoning([
            TextReasoningContentNative(text: "Ignored trace", protectedData: nil)], messageId: unsigned.id)
        require(unsignedReplay == nil, "Unsigned trace was injected into history")

        let options = ChatOptionsNative()
        var updates: [ResponseUpdateNative] = []
        try client.emitReasoning([.reasoning(reasoning)], options, nil) { updates.append($0) }
        require(updates.count == 3, "Expected two text segments followed by real protected data")
        require(updates[0].messageId == reasoning.id && updates[0].segmentId == "first-segment", "Lost native identity")
        require(updates[0].text == "First " && updates[1].text == "thought", "Lost reasoning-only snapshot text")
        require(updates[2].text == nil && updates[2].protectedData == protected, "Lost signature-only update")
        options.includeReasoning = false
        try client.emitReasoning([.reasoning(reasoning)], options, nil) { updates.append($0) }
        require(updates.count == 3, "Output.None leaked reasoning")

        let signatureOnly = Transcript.Reasoning(id: "protected-only", segments: [], signature: Data([9]))
        options.includeReasoning = true
        try client.emitReasoning([.reasoning(signatureOnly)], options, nil) { updates.append($0) }
        require(updates.count == 4 && updates.last?.protectedData != nil, "Signature without text was dropped")

        let message = ChatMessageNative()
        message.role = .assistant
        message.messageId = reasoning.id
        message.contents = [
            TextReasoningContentNative(text: "Ignored", protectedData: nil),
            TextContentNative(text: "Before"),
            FunctionCallContentNative(callId: "opaque-call", name: "lookup", arguments: "{}"),
            TextContentNative(text: "After")
        ]
        let entries = try client.toAssistantEntries(message)
        require(entries.count == 3, "Unsigned history changed text/tool boundaries")
        guard case .response = entries[0], case .toolCalls(let calls) = entries[1],
              case .response = entries[2] else { preconditionFailure("History order changed") }
        require(calls.first?.id == "opaque-call", "Tool correlation changed")
    }
}
