import Foundation
import FoundationModels

private func response(_ text: String) -> Transcript.Response {
    Transcript.Response(assetIDs: [], segments: [.text(Transcript.TextSegment(content: text))])
}

private func textById(_ entries: ArraySlice<Transcript.Entry>) -> [String: String] {
    var result: [String: String] = [:]
    for entry in entries {
        guard case .response(let value) = entry else { continue }
        result[value.id] = value.segments.compactMap { segment -> String? in
            switch segment {
            case .text(let text): return text.content
            case .structure(let structure): return structure.content.jsonString
            default: return nil
            }
        }.joined()
    }
    return result
}

private func checkNativeEntries() throws {
    var projection = TranscriptResponseProjection()
    var first = response("\n\n")
    let second = response("\n\n")
    var answer = response("Answer")
    let arguments = try GeneratedContent(json: "{}")
    let callOne = Transcript.ToolCall(id: "call-one", toolName: "get_key", arguments: arguments)
    let callTwo = Transcript.ToolCall(id: "call-two", toolName: "get_answer", arguments: arguments)
    var entries: [Transcript.Entry] = [.response(first)]
    let initial = projection.project(entries[...])
    precondition(initial.count == 1 && initial[0].messageId == first.id && initial[0].text == "\n\n")

    entries += [
        .toolCalls(Transcript.ToolCalls([callOne])),
        .toolOutput(Transcript.ToolOutput(id: callOne.id, toolName: callOne.toolName,
                                        segments: [.text(Transcript.TextSegment(content: "key"))])),
        .response(second)
    ]
    let afterFirstTool = projection.project(entries[...])
    precondition(afterFirstTool.count == 1 && afterFirstTool[0].messageId == second.id)
    precondition(afterFirstTool[0].text == "\n\n")
    entries += [
        .toolCalls(Transcript.ToolCalls([callTwo])),
        .toolOutput(Transcript.ToolOutput(id: callTwo.id, toolName: callTwo.toolName,
                                        segments: [.text(Transcript.TextSegment(content: "answer"))])),
        .response(answer)
    ]
    let afterSecondTool = projection.project(entries[...])
    precondition(afterSecondTool.count == 1 && afterSecondTool[0].messageId == answer.id)
    precondition(afterSecondTool[0].text == "Answer")
    precondition(projection.project(entries[...]).isEmpty)

    first.segments.append(.text(Transcript.TextSegment(content: "\tolder growth\r\n")))
    entries[0] = .response(first)
    let olderGrowth = projection.project(entries[...])
    precondition(olderGrowth.count == 1 && olderGrowth[0].messageId == first.id)
    precondition(olderGrowth[0].text == "\n\n\tolder growth\r\n")

    // Collection can contain growth missing from the last streaming snapshot.
    answer.segments.append(.text(Transcript.TextSegment(content: "\n final \t")))
    entries[entries.count - 1] = .response(answer)
    let final = projection.project(entries[...])
    precondition(final.count == 1 && final[0].messageId == answer.id)
    precondition(final[0].text == "Answer\n final \t")
    precondition(projection.project(entries[...]).isEmpty)

    first.segments = [.text(Transcript.TextSegment(content: "replacement"))]
    let changed = projection.project([.response(first)][...])
    precondition(changed.count == 1 && changed[0].text == "replacement")
}

private func checkStructuredEntries() throws {
    let firstContent = try GeneratedContent(json: #"{"text":"first\n\t"}"#)
    let secondContent = try GeneratedContent(json: #"{"text":"second\r\n "}"#)
    var first = Transcript.Response(assetIDs: [], segments: [
        .text(Transcript.TextSegment(content: "\n")),
        .structure(Transcript.StructuredSegment(source: "Result", content: firstContent))
    ])
    let second = Transcript.Response(assetIDs: [], segments: [
        .structure(Transcript.StructuredSegment(source: "Result", content: secondContent))
    ])
    var projection = TranscriptResponseProjection()
    let initial = projection.project([.response(first), .response(second)][...])
    precondition(initial.count == 2)
    precondition(initial[0].messageId == first.id && initial[0].text == "\n" + firstContent.jsonString)
    precondition(initial[1].messageId == second.id && initial[1].text == secondContent.jsonString)
    precondition(projection.project([.response(first), .response(second)][...]).isEmpty)
    let finalContent = try GeneratedContent(json: #"{"text":"first\n\t final"}"#)
    first.segments[1] = .structure(Transcript.StructuredSegment(source: "Result", content: finalContent))
    let final = projection.project([.response(first), .response(second)][...])
    precondition(final.count == 1 && final[0].messageId == first.id)
    precondition(final[0].text == "\n" + finalContent.jsonString)
}

@available(macOS 27.0, *)
private struct SnapshotBoundaryModel: LanguageModel {
    var capabilities: LanguageModelCapabilities { .init([]) }
    var executorConfiguration: Int { 0 }

    struct Executor: LanguageModelExecutor {
        typealias Model = SnapshotBoundaryModel
        typealias Configuration = Int
        init(configuration: Int) {}
        func prewarm(model: SnapshotBoundaryModel, transcript: Transcript) {}

        nonisolated(nonsending) func respond(
            to request: LanguageModelExecutorGenerationRequest,
            model: SnapshotBoundaryModel,
            streamingInto channel: LanguageModelExecutorGenerationChannel
        ) async throws {
            for (id, text) in [("blank-one", "\n\n"), ("blank-two", "\n\n"), ("answer", "Answer")] {
                await channel.send(.response(entryID: id, action: .appendText(text, tokenCount: 1)))
                try await Task.sleep(for: .milliseconds(200))
            }
        }
    }
}

@available(macOS 27.0, *)
private func checkSDKSnapshotBoundaries() async throws {
    let session = LanguageModelSession(model: SnapshotBoundaryModel())
    let stream = session.streamResponse(to: "Controlled snapshot identity regression; no system model is loaded.")
    var projection = TranscriptResponseProjection()
    var cumulativeById: [String: String] = [:]
    var emittedById: [String: String] = [:]
    var mismatches = 0
    var snapshots = 0

    func apply(_ content: TranscriptResponseProjection.Content) {
        let previous = cumulativeById[content.messageId] ?? ""
        precondition(content.text.hasPrefix(previous), "This controlled source must be cumulative per ID.")
        emittedById[content.messageId, default: ""] += String(content.text.dropFirst(previous.count))
        cumulativeById[content.messageId] = content.text
    }

    for try await snapshot in stream {
        snapshots += 1
        let ownText = textById(snapshot.transcriptEntries)
        if let lastResponse = snapshot.transcriptEntries.reversed().first(where: {
            if case .response = $0 { return true }
            return false
        }), ownText[lastResponse.id] != snapshot.content {
            mismatches += 1
        }
        for content in projection.project(snapshot.transcriptEntries) {
            precondition(ownText[content.messageId] == content.text)
            apply(content)
        }
    }
    let collected = try await stream.collect()
    for content in projection.project(collected.transcriptEntries) { apply(content) }
    precondition(snapshots > 0 && mismatches > 0, "The old scalar/last-ID projection must reproduce the defect.")
    precondition(emittedById == textById(collected.transcriptEntries))
    precondition(emittedById == ["blank-one": "\n\n", "blank-two": "\n\n", "answer": "Answer"])
    precondition(projection.project(collected.transcriptEntries).isEmpty)
    print("SDK snapshots: \(snapshots); scalar/last-response mismatches: \(mismatches); exact per-ID content passed")
}

private actor ToolCounter {
    private var counts: [String: Int] = [:]

    func invoke(_ name: String) -> String {
        counts[name, default: 0] += 1
        return "result"
    }

    func values() -> [String: Int] { counts }
}

private struct BoundaryTool: Tool {
    let name: String
    let counter: ToolCounter
    var description: String { "Controlled snapshot boundary tool." }

    @Generable
    struct Arguments {}

    func call(arguments: Arguments) async -> String {
        await counter.invoke(name)
    }
}

@available(macOS 27.0, *)
private struct ToolBoundaryModel: LanguageModel {
    var capabilities: LanguageModelCapabilities { .init([.toolCalling]) }
    var executorConfiguration: Int { 0 }

    struct Executor: LanguageModelExecutor {
        typealias Model = ToolBoundaryModel
        typealias Configuration = Int
        init(configuration: Int) {}
        func prewarm(model: ToolBoundaryModel, transcript: Transcript) {}

        nonisolated(nonsending) func respond(
            to request: LanguageModelExecutorGenerationRequest,
            model: ToolBoundaryModel,
            streamingInto channel: LanguageModelExecutorGenerationChannel
        ) async throws {
            let outputs = request.transcript.filter {
                if case .toolOutput = $0 { return true }
                return false
            }.count
            precondition(outputs <= 2)
            let responseId = ["before-first-tool", "before-second-tool", "answer"][outputs]
            let text = outputs == 2 ? "Answer\r\n\t done \n" : "\n\n"
            await channel.send(.response(entryID: responseId, action: .appendText(text, tokenCount: 1)))
            try await Task.sleep(for: .milliseconds(200))
            if outputs < 2 {
                await channel.send(.toolCalls(
                    entryID: "calls-\(outputs)",
                    action: .toolCall(
                        id: "call-\(outputs)",
                        name: ["get_key", "get_answer"][outputs],
                        action: .appendArguments("{}", tokenCount: 1))))
            }
        }
    }
}

@available(macOS 27.0, *)
private func checkSDKToolBoundaries() async throws {
    let counter = ToolCounter()
    let session = LanguageModelSession(model: ToolBoundaryModel(), tools: [
        BoundaryTool(name: "get_key", counter: counter),
        BoundaryTool(name: "get_answer", counter: counter)
    ])
    let stream = session.streamResponse(to: "Controlled two-tool, three-response identity regression.")
    var projection = TranscriptResponseProjection()
    var cumulativeById: [String: String] = [:]
    var emittedById: [String: String] = [:]
    var snapshots = 0

    func apply(_ content: TranscriptResponseProjection.Content) {
        let previous = cumulativeById[content.messageId] ?? ""
        precondition(content.text.hasPrefix(previous))
        emittedById[content.messageId, default: ""] += String(content.text.dropFirst(previous.count))
        cumulativeById[content.messageId] = content.text
    }

    for try await snapshot in stream {
        snapshots += 1
        let ownText = textById(snapshot.transcriptEntries)
        for content in projection.project(snapshot.transcriptEntries) {
            precondition(ownText[content.messageId] == content.text)
            apply(content)
        }
    }
    let collected = try await stream.collect()
    for content in projection.project(collected.transcriptEntries) { apply(content) }
    let calls = collected.transcriptEntries.flatMap { entry -> [Transcript.ToolCall] in
        if case .toolCalls(let calls) = entry { return Array(calls) }
        return []
    }
    precondition(calls.map(\.toolName) == ["get_key", "get_answer"])
    let counts = await counter.values()
    precondition(counts == ["get_key": 1, "get_answer": 1])
    precondition(snapshots > 0)
    precondition(emittedById == textById(collected.transcriptEntries))
    precondition(emittedById == [
        "before-first-tool": "\n\n", "before-second-tool": "\n\n", "answer": "Answer\r\n\t done \n"
    ])
    print("SDK two-tool regression: \(snapshots) snapshots; 2 actual tool calls; 3 exact native response IDs passed")
}

@main
private struct TranscriptResponseProjectionTests {
    static func main() async throws {
        try checkNativeEntries()
        try checkStructuredEntries()
        if #available(macOS 27.0, *) {
            try await checkSDKSnapshotBoundaries()
            try await checkSDKToolBoundaries()
        }
        print("TranscriptResponseProjection regressions passed")
    }
}
