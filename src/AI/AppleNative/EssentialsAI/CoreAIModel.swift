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
                    let calls: [[String: any Sendable]] = try toolCalls.map { call in
                        [
                            "id": call.id, "type": "function",
                            "function": ["name": call.toolName, "arguments": try jsonValue(call.arguments.jsonString)]
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
            do {
                _ = try tokenizer.applyChatTemplate(messages: turns, tools: specs.isEmpty ? nil : specs)
            } catch {
                throw Self.error("The local tokenizer cannot render this request without losing chat/tool semantics: \(error)")
            }
        }

        private func text(_ segments: [Transcript.Segment], separator: String = "") throws -> String {
            try segments.map { segment in
                switch segment {
                case .text(let text): return text.content
                case .structure(let structure): return structure.content.jsonString
                default: throw error("The experimental Core AI model supports text transcript segments only.")
                }
            }.joined(separator: separator)
        }

        private func jsonValue(_ json: String) throws -> any Sendable {
            try JSONDecoder().decode(JSONValue.self, from: Data(json.utf8)).value
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

    func release(path: String) {
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
            case .null: return Optional<String>.none
            }
        }
    }
}
#endif
