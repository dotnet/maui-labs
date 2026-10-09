#if ENABLE_CORE_AI
import Foundation
import CoreAILanguageModels
import FoundationModels
import Tokenizers

@available(iOS 27.0, macOS 27.0, *)
actor CoreAIModel {
    static let shared = CoreAIModel()
    private var entries: [String: Entry] = [:]
    private var owners: [String: Set<UUID>] = [:]
    private var active: Set<String> = []
    private var waiters: [String: [UUID: CheckedContinuation<Void, Error>]] = [:]

    struct Entry: LanguageModel {
        let model: CoreAILanguageModel
        let tokenizer: any Tokenizer
        let name: String
        var capabilities: LanguageModelCapabilities { model.capabilities }
        var executorConfiguration: CoreAILanguageModel.CoreAIExecutor.Configuration { model.executorConfiguration }

        // The upstream adapter catches template errors and silently drops tool semantics.
        // Validate every generation, including the transcript after an awaited tool.
        struct Executor: LanguageModelExecutor {
            typealias Model = Entry
            typealias Configuration = CoreAILanguageModel.CoreAIExecutor.Configuration
            private let inner: CoreAILanguageModel.CoreAIExecutor
            init(configuration: Configuration) throws {
                inner = try CoreAILanguageModel.CoreAIExecutor(configuration: configuration)
            }
            func prewarm(model: Entry, transcript: Transcript) {
                inner.prewarm(model: model.model, transcript: transcript)
            }
            nonisolated(nonsending) func respond(
                to request: LanguageModelExecutorGenerationRequest,
                model: Entry,
                streamingInto channel: LanguageModelExecutorGenerationChannel
            ) async throws {
                try model.validate(request: request)
                try await inner.respond(to: request, model: model.model, streamingInto: channel)
            }
        }

        private func validate(request: LanguageModelExecutorGenerationRequest) throws {
            // Match the pinned executor's input shapes, including JSON-string tool arguments.
            var turns: [Message] = []
            for entry in request.transcript {
                switch entry {
                case .instructions(let instructions):
                    let text = try text(instructions.segments, separator: "\n")
                    if !text.isEmpty { turns.append(["role": "system", "content": text]) }
                case .prompt(let prompt):
                    let text = try text(prompt.segments)
                    if !text.isEmpty { turns.append(["role": "user", "content": text]) }
                case .response(let response):
                    let text = try text(response.segments)
                    if !text.isEmpty { turns.append(["role": "assistant", "content": text]) }
                case .toolCalls(let toolCalls):
                    let calls: [[String: any Sendable]] = toolCalls.map { call in
                        [
                            "id": call.id, "type": "function",
                            "function": ["name": call.toolName, "arguments": call.arguments.jsonString]
                        ]
                    }
                    turns.append(["role": "assistant", "content": "", "tool_calls": calls])
                case .toolOutput(let output):
                    turns.append(["role": "tool", "content": try text(output.segments),
                                  "name": output.toolName, "tool_call_id": output.id])
                case .reasoning:
                    continue
                @unknown default:
                    throw error("Unsupported Core AI transcript entry.")
                }
            }
            let specs: [ToolSpec] = try request.enabledToolDefinitions.map { tool in
                ["type": "function", "function": [
                    "name": tool.name, "description": tool.description,
                    "parameters": try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(tool.parameters)).value
                ]]
            }
            let toolSpecs = specs.isEmpty ? nil : specs
            turns = injectToolsIntoSystemMessageIfNeeded(
                turns, toolSpecs: toolSpecs, detection: detectToolCallFormat(using: tokenizer))
            let effort: String? = switch request.contextOptions.reasoningLevel {
                case nil: nil
                case .light: "low"
                case .moderate: "medium"
                case .deep: "high"
                case .custom(let value): value
                @unknown default: throw error("Unsupported native Core AI reasoning context.")
            }
            var extra: [String: any Sendable] = [:]
            if let effort = effort?.trimmingCharacters(in: .whitespacesAndNewlines), !effort.isEmpty {
                if effort.lowercased() == "none" { extra["enable_thinking"] = false }
                else {
                    extra["reasoning_effort"] = effort
                    extra["enable_thinking"] = true
                }
            }
            do {
                _ = try tokenizer.applyChatTemplate(
                    messages: turns, tools: toolSpecs, additionalContext: extra.isEmpty ? nil : extra)
            } catch {
                throw Self.error("The local tokenizer cannot render this request without losing chat/tool semantics: \(error)")
            }
        }

        private func text(_ segments: [Transcript.Segment], separator: String = "") throws -> String {
            try segments.map { segment in
                switch segment {
                case .text(let text): return text.content
                default: throw error("The experimental Core AI model supports text transcript segments only.")
                }
            }.joined(separator: separator)
        }

        private static func error(_ message: String) -> NSError {
            NSError(domain: "CoreAI", code: 5, userInfo: [NSLocalizedDescriptionKey: message])
        }
        private func error(_ message: String) -> NSError { Self.error(message) }
    }

    func acquire(path: String, owner: UUID) async throws -> Entry {
        owners[path, default: []].insert(owner)
        let id = UUID()
        try await withTaskCancellationHandler {
            while active.contains(path) {
                try Task.checkCancellation()
                try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
                    if Task.isCancelled { continuation.resume(throwing: CancellationError()) }
                    else { waiters[path, default: [:]][id] = continuation }
                }
            }
            try Task.checkCancellation()
        } onCancel: {
            Task { await self.cancelWaiter(id, path: path) }
        }
        active.insert(path)
        do {
            if let entry = entries[path] { return entry }
            let entry = try await Self.load(at: URL(fileURLWithPath: path, isDirectory: true))
            try Task.checkCancellation()
            entries[path] = entry
            return entry
        } catch {
            release(path: path)
            throw error
        }
    }

    func release(path: String, interrupted: Bool = false) {
        if interrupted {
            // Discard partially generated engine state only after the executor's borrow returns.
            entries[path]?.model.unload()
        }
        active.remove(path)
        let waiting = waiters.removeValue(forKey: path) ?? [:]
        for continuation in waiting.values { continuation.resume() }
        if owners[path]?.isEmpty != false { entries.removeValue(forKey: path) }
    }

    func removeOwner(_ owner: UUID, path: String) {
        owners[path]?.remove(owner)
        if owners[path]?.isEmpty != false && !active.contains(path) {
            entries.removeValue(forKey: path)
            owners.removeValue(forKey: path)
        }
    }

    private func cancelWaiter(_ id: UUID, path: String) {
        waiters[path]?.removeValue(forKey: id)?.resume(throwing: CancellationError())
    }

    private static func load(at url: URL) async throws -> Entry {
        let bundle = try LanguageModelBundle(at: url)
        guard let tokenizerPath = bundle.tokenizerPath,
              FileManager.default.fileExists(atPath: tokenizerPath.appendingPathComponent("tokenizer_config.json").path) else {
            throw NSError(domain: "CoreAI", code: 1, userInfo: [
                NSLocalizedDescriptionKey: "Core AI requires a complete local tokenizer directory; network fallback is disabled."
            ])
        }
        let tokenizer = try await bundle.loadTokenizer()
        let model = try await CoreAILanguageModel(resourcesAt: url)
        try Task.checkCancellation()
        return Entry(model: model, tokenizer: tokenizer, name: bundle.name)
    }

    private indirect enum JSONValue: Decodable {
        case string(String), integer(Int), number(Double), bool(Bool), object([String: JSONValue]), array([JSONValue]), null
        init(from decoder: any Swift.Decoder) throws {
            let c = try decoder.singleValueContainer()
            if c.decodeNil() { self = .null }
            else if let value = try? c.decode(Bool.self) { self = .bool(value) }
            else if let value = try? c.decode(String.self) { self = .string(value) }
            else if let value = try? c.decode(Int.self) { self = .integer(value) }
            else if let value = try? c.decode(Double.self) { self = .number(value) }
            else if let value = try? c.decode([String: JSONValue].self) { self = .object(value) }
            else { self = .array(try c.decode([JSONValue].self)) }
        }
        var value: any Sendable {
            switch self {
            case .string(let value): return value
            case .integer(let value): return value
            case .number(let value): return value
            case .bool(let value): return value
            case .object(let value): return value.mapValues(\.value)
            case .array(let value): return value.map(\.value)
            case .null: return NSNull()
            }
        }
    }
}
#endif
