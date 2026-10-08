import Foundation
import Dispatch
import CoreGraphics
import ImageIO
import FoundationModels
#if ENABLE_CORE_AI
import CoreAILanguageModels
#endif

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
/// Type alias for the logging action block.
public typealias AppleIntelligenceLogAction = (String) -> Void

/// Singleton holder for the logger action.
@objc(AppleIntelligenceLogger)
public class AppleIntelligenceLogger: NSObject {
    /// The logging action. Set this to receive log callbacks.
    /// Example: AppleIntelligenceLogger.log = { message in print("[Native] \(message)") }
    @objc public static var log: AppleIntelligenceLogAction?
}
#endif

@objc(ChatClientError)
public enum ChatClientError: Int {
    case emptyMessages = 1
    case invalidRole = 2
    case invalidContent = 3
    case cancelled = 4
}

@objc(ChatClientNative)
public class ChatClientNative: NSObject {
    private let modelDirectory: String?
    private let owner = UUID()
    @objc public private(set) var modelIdentifier: String?
#if ENABLE_CORE_AI
    private var coreAIEntry: CoreAIModel.Entry?
#endif

    @objc public override init() {
        modelDirectory = nil
        modelIdentifier = "apple-intelligence"
        super.init()
    }

    @objc public init(modelDirectory: String) {
        self.modelDirectory = URL(fileURLWithPath: modelDirectory, isDirectory: true)
            .standardizedFileURL.resolvingSymlinksInPath().path
        super.init()
    }

    deinit {
#if ENABLE_CORE_AI
        if let path = modelDirectory {
            let owner = owner
            Task { await CoreAIModel.shared.removeOwner(owner, path: path) }
        }
#endif
    }

    // MARK: - Stream Response

    @objc public func streamResponse(
        messages: [ChatMessageNative],
        options: ChatOptionsNative?,
        onUpdate: @escaping (ResponseUpdateNative) -> Void,
        onComplete: @escaping (ChatResponseNative?, NSError?) -> Void
    ) -> CancellationTokenNative? {

        let methodName = "streamResponse"
        let cq: DispatchQueue? = callbackQueue()

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
        if let log = AppleIntelligenceLogger.log {
            log("[\(methodName)] Invoked with \(messages.count) messages")
            log("[\(methodName)] Messages: \(formatMessagesDetailed(messages))")
            if let opts = options {
                log("[\(methodName)] Options: \(formatOptionsDetailed(opts))")
            }
        }
#endif

        let toolWatcher =
            options?.tools == nil
            ? nil
            : ToolCallWatcher(
                onToolCall: { id, name, arguments in
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Tool invoking: \(name) (id=\(id)) with arguments: \(arguments)")
                    }
#endif

                    let update = ResponseUpdateNative(updateType: .toolCall, toolCallId: id, toolCallName: name, toolCallArguments: arguments)
                    cq?.async { onUpdate(update) } ?? onUpdate(update)
                },
                onToolResult: { id, name, result in
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Tool completed: \(name) (id=\(id)) with result: \(result)")
                    }
#endif

                    let update = ResponseUpdateNative(updateType: .toolResult, toolCallId: id, toolCallName: name, toolCallResult: result)
                    cq?.async { onUpdate(update) } ?? onUpdate(update)
                }
            )

        return executeTask(methodName, messages, options, toolWatcher, cq, onComplete) { session, prompt, schema, genOptions in

            if let jsonSchema = schema {
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Starting schema-based stream response")
                }
#endif

                let responseStream: LanguageModelSession.ResponseStream<GeneratedContent>
                if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                    responseStream = session.streamResponse(to: prompt, schema: jsonSchema, options: genOptions,
                        contextOptions: try self.contextOptions(options, includeSchema: false))
                } else {
                    responseStream = session.streamResponse(to: prompt, schema: jsonSchema, includeSchemaInPrompt: false, options: genOptions)
                }

                for try await response in responseStream {
                    try Task.checkCancellation()
                    if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                        try self.emitReasoning(response.transcriptEntries, options, cq, onUpdate)
                    }
                    let text = response.content.jsonString
                    guard !text.isEmpty else { continue }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Streaming update: \(text)")
                    }
#endif
                    let update = ResponseUpdateNative(updateType: .content, text: text,
                        messageId: self.responseMessageId(response))
                    cq?.async { onUpdate(update) } ?? onUpdate(update)
                }

                let response = try await responseStream.collect()
                if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                    try self.emitReasoning(response.transcriptEntries, options, cq, onUpdate)
                }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Stream collected, content length: \(response.content.jsonString.count)")
                }
#endif
                return try self.fromResponse(response, includeReasoning: options?.includeReasoning != false)
            } else {
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Starting text-based stream response")
                }
#endif

                let responseStream: LanguageModelSession.ResponseStream<String>
                if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                    responseStream = session.streamResponse(to: prompt, options: genOptions,
                        contextOptions: try self.contextOptions(options))
                } else {
                    responseStream = session.streamResponse(to: prompt, options: genOptions)
                }

                for try await response in responseStream {
                    try Task.checkCancellation()
                    if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                        try self.emitReasoning(response.transcriptEntries, options, cq, onUpdate)
                    }
                    let text = response.content
                    guard !text.isEmpty else { continue }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Streaming update: \(text)")
                    }
#endif
                    let update = ResponseUpdateNative(updateType: .content, text: text,
                        messageId: self.responseMessageId(response))
                    cq?.async { onUpdate(update) } ?? onUpdate(update)
                }

                let response = try await responseStream.collect()
                if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                    try self.emitReasoning(response.transcriptEntries, options, cq, onUpdate)
                }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Stream collected, content length: \(response.content.count)")
                }
#endif
                return try self.fromResponse(response, includeReasoning: options?.includeReasoning != false)
            }
        }

    }

    // MARK: - Get Response

    @objc public func getResponse(
        messages: [ChatMessageNative],
        options: ChatOptionsNative?,
        onUpdate: @escaping (ResponseUpdateNative) -> Void,
        onComplete: @escaping (ChatResponseNative?, NSError?) -> Void
    ) -> CancellationTokenNative? {

        let methodName = "getResponse"
        let cq: DispatchQueue? = callbackQueue()

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
        if let log = AppleIntelligenceLogger.log {
            log("[\(methodName)] Invoked with \(messages.count) messages")
            log("[\(methodName)] Messages: \(formatMessagesDetailed(messages))")
            if let opts = options {
                log("[\(methodName)] Options: \(formatOptionsDetailed(opts))")
            }
        }
#endif

        let toolWatcher =
            options?.tools == nil
            ? nil
            : ToolCallWatcher(
                onToolCall: { id, name, arguments in
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Tool invoking: \(name) (id=\(id)) with arguments: \(arguments)")
                    }
#endif

                    let update = ResponseUpdateNative(updateType: .toolCall, toolCallId: id, toolCallName: name, toolCallArguments: arguments)
                    cq?.async { onUpdate(update) } ?? onUpdate(update)
                },
                onToolResult: { id, name, result in
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Tool completed: \(name) (id=\(id)) with result: \(result)")
                    }
#endif

                    let update = ResponseUpdateNative(updateType: .toolResult, toolCallId: id, toolCallName: name, toolCallResult: result)
                    cq?.async { onUpdate(update) } ?? onUpdate(update)
                }
            )

        return executeTask(methodName, messages, options, toolWatcher, cq, onComplete) { session, prompt, schema, genOptions in

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
            if let log = AppleIntelligenceLogger.log {
                log("[\(methodName)] \(schema != nil ? "Getting schema-based response" : "Getting text-based response")")
            }
#endif

            let response = try await {
                if let jsonSchema = schema {
                    let inner: LanguageModelSession.Response<GeneratedContent>
                    if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                        inner = try await session.respond(to: prompt, schema: jsonSchema, options: genOptions,
                            contextOptions: self.contextOptions(options, includeSchema: false))
                    } else {
                        inner = try await session.respond(to: prompt, schema: jsonSchema, includeSchemaInPrompt: false, options: genOptions)
                    }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Response received, content length: \(inner.content.jsonString.count)")
                    }
#endif
                    return try self.fromResponse(inner, includeReasoning: options?.includeReasoning != false)
                } else {
                    let inner: LanguageModelSession.Response<String>
                    if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                        inner = try await session.respond(to: prompt, options: genOptions,
                            contextOptions: self.contextOptions(options))
                    } else {
                        inner = try await session.respond(to: prompt, options: genOptions)
                    }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Response received, content length: \(inner.content.count)")
                    }
#endif
                    return try self.fromResponse(inner, includeReasoning: options?.includeReasoning != false)
                }
            }()

            return response
        }
    }

    // MARK: - Session Helpers

    private func prepareSession(
        _ methodName: String,
        _ messages: [ChatMessageNative],
        _ options: ChatOptionsNative?,
        _ toolWatcher: ToolCallWatcher?
    ) async throws -> (
        session: LanguageModelSession,
        prompt: Prompt,
        schema: GenerationSchema?,
        genOptions: GenerationOptions
    ) {

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
        if let log = AppleIntelligenceLogger.log {
            log("[\(methodName)] Preparing session with \(messages.count) messages, hasTools=\(options?.tools != nil)")
        }
#endif

        // The last message is the prompt; everything before is the transcript history.
        guard let lastMessage = messages.last else {
            throw NSError.chatError(.invalidRole, description: "No messages found in conversation")
        }
        let otherMessages = Array(messages.dropLast())

        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *), modelDirectory == nil,
           messages.contains(where: { message in message.contents.contains(where: { $0 is ImageContentNative }) }),
           !SystemLanguageModel.default.capabilities.contains(.vision) {
            throw NSError.chatError(.invalidContent, description: "Image input requires iOS, Mac Catalyst, or macOS 27.0 or later and a vision-capable Apple Intelligence model. The current model does not report vision support.")
        }

        let tools = options?.tools?.map { ToolNative($0, toolWatcher?.notifyToolCall, toolWatcher?.notifyToolResult) } ?? []

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
        if let log = AppleIntelligenceLogger.log, let toolList = options?.tools {
            for tool in toolList {
                log("[\(methodName)] Tool registered: \(tool.name) - \(tool.desc)")
                log("[\(methodName)] Tool \(tool.name) argumentsSchema: \(tool.argumentsSchema)")
                log("[\(methodName)] Tool \(tool.name) outputSchema: \(tool.outputSchema)")
            }
        }
#endif

        let transcript = try Transcript(entries: otherMessages.flatMap(self.toTranscriptEntries))
        let prompt = try self.toPrompt(message: lastMessage)

        // Parse the JSON schema from the options
        let schema: GenerationSchema? = try {
            if let jsonSchema = options?.responseJsonSchema {
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Parsing JSON schema for structured output: \(jsonSchema)")
                }
#endif

                let parsed = try JsonSchemaDecoder.parse(String(jsonSchema))
                return parsed
            }
            return nil
        }()

        // Map options into GenerationOptions
        let genOptions = GenerationOptions(
            samplingMode: {
                if let topK = options?.topK?.intValue {
                    return .random(top: topK, seed: options?.seed?.uint64Value)
                }
                return .greedy
            }(),
            temperature: options?.temperature?.doubleValue ?? (modelDirectory == nil ? nil : 0.6),
            maximumResponseTokens: options?.maxOutputTokens?.intValue
        )

        let session: LanguageModelSession
        if modelDirectory != nil {
#if ENABLE_CORE_AI
            guard #available(iOS 27.0, macOS 27.0, *), let entry = coreAIEntry else {
                throw NSError(domain: "CoreAI", code: 2, userInfo: [
                    NSLocalizedDescriptionKey: "Core AI requires OS27 and a loaded local model."
                ])
            }
            if !tools.isEmpty && !entry.model.capabilities.contains(.toolCalling) {
                throw NSError(domain: "CoreAI", code: 3, userInfo: [
                    NSLocalizedDescriptionKey: "The selected local model does not support tool calling."
                ])
            }
            if schema != nil && !entry.model.capabilities.contains(.guidedGeneration) {
                throw NSError(domain: "CoreAI", code: 4, userInfo: [
                    NSLocalizedDescriptionKey: "The selected local model does not support guided generation."
                ])
            }
            session = LanguageModelSession(model: entry, tools: tools, transcript: transcript)
#else
            throw NSError(domain: "CoreAI", code: 2, userInfo: [
                NSLocalizedDescriptionKey: "Core AI is not enabled in this build."
            ])
#endif
        } else {
            session = LanguageModelSession(model: SystemLanguageModel.default, tools: tools, transcript: transcript)
        }

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
        if let log = AppleIntelligenceLogger.log {
            log("[\(methodName)] Session ready, hasSchema=\(schema != nil)")
        }
#endif

        return (session, prompt, schema, genOptions)
    }

    private func executeTask(
        _ methodName: String,
        _ messages: [ChatMessageNative],
        _ options: ChatOptionsNative?,
        _ toolWatcher: ToolCallWatcher?,
        _ cq: DispatchQueue?,
        _ onComplete: @escaping (ChatResponseNative?, NSError?) -> Void,
        operation:
            @escaping (LanguageModelSession, Prompt, GenerationSchema?, GenerationOptions) async throws
            -> ChatResponseNative
    ) -> CancellationTokenNative? {

        guard !messages.isEmpty else {
            let error = NSError.chatError(.emptyMessages, description: "No messages provided.")
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
            if let log = AppleIntelligenceLogger.log {
                log("[\(methodName)] Failed: No messages provided")
            }
#endif

            cq?.async { onComplete(nil, error.toNSError()) } ?? onComplete(nil, error.toNSError())
            return nil
        }

        let task = Task {
            var acquired = false
            do {
                try Task.checkCancellation()

#if ENABLE_CORE_AI
                if let path = modelDirectory {
                    coreAIEntry = try await CoreAIModel.shared.acquire(path: path, owner: owner)
                    acquired = true
                    modelIdentifier = coreAIEntry?.name
                }
#endif
                let (session, prompt, schema, genOptions) = try await self.prepareSession(methodName, messages, options, toolWatcher)

                let response = try await operation(session, prompt, schema, genOptions)

                try Task.checkCancellation()
#if ENABLE_CORE_AI
                if acquired, let path = modelDirectory {
                    await CoreAIModel.shared.release(path: path)
                    acquired = false
                }
#endif

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Completed with \(response.messages.count) messages")
                    log("[\(methodName)] Response: \(self.formatResponseDetailed(response))")
                }
#endif

                cq?.async { onComplete(response, nil) } ?? onComplete(response, nil)
            } catch {
#if ENABLE_CORE_AI
                if acquired, let path = modelDirectory {
                    await CoreAIModel.shared.release(path: path, interrupted: Task.isCancelled || error is CancellationError)
                }
#endif
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Failed: \(error.localizedDescription)")
                }
#endif

                cq?.async { onComplete(nil, error.toNSError()) } ?? onComplete(nil, error.toNSError())
            }
        }

        return CancellationTokenNative(task: task)
    }

    private func fromResponse<Content: Generable>(_ response: LanguageModelSession.Response<Content>, includeReasoning: Bool) throws -> ChatResponseNative {
        let result = ChatResponseNative(messages: try response.transcriptEntries.compactMap {
            try self.fromTranscriptEntry($0, includeReasoning: includeReasoning)
        })
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
            result.inputTokenCount = NSNumber(value: response.usage.input.totalTokenCount)
            result.outputTokenCount = NSNumber(value: response.usage.output.totalTokenCount)
            result.totalTokenCount = NSNumber(value: response.usage.totalTokenCount)
        }
        return result
    }

    @available(iOS 27.0, macOS 27.0, visionOS 27.0, *)
    private func contextOptions(_ options: ChatOptionsNative?, includeSchema: Bool? = nil) throws -> ContextOptions {
        // The default system model does not support reasoning controls. Leave its behavior unchanged.
        guard modelDirectory != nil, let level = options?.reasoningLevel else {
            return ContextOptions(includeSchemaInPrompt: includeSchema)
        }
#if ENABLE_CORE_AI
        guard coreAIEntry?.model.capabilities.contains(.reasoning) == true else {
            throw NSError.chatError(.invalidContent, description: "The selected local model does not support reasoning.")
        }
#endif
        let nativeLevel: ContextOptions.ReasoningLevel
        switch level {
        case "none": nativeLevel = .custom("none")
        case "light": nativeLevel = .light
        case "moderate": nativeLevel = .moderate
        case "deep": nativeLevel = .deep
        default: throw NSError.chatError(.invalidContent, description: "Unsupported reasoning level: \(level).")
        }
        return ContextOptions(includeSchemaInPrompt: includeSchema, reasoningLevel: nativeLevel)
    }

    private func responseMessageId<Content>(_ snapshot: LanguageModelSession.ResponseStream<Content>.Snapshot) -> String? {
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
            return snapshot.transcriptEntries.compactMap { entry in
                if case .response(let response) = entry { return response.id }
                return nil
            }.last
        }
        return nil
    }

    @available(iOS 27.0, macOS 27.0, visionOS 27.0, *)
    func emitReasoning(_ entries: ArraySlice<Transcript.Entry>, _ options: ChatOptionsNative?,
        _ queue: DispatchQueue?, _ onUpdate: @escaping (ResponseUpdateNative) -> Void) throws {
        guard options?.includeReasoning != false else { return }
        for entry in entries {
            guard case .reasoning(let reasoning) = entry else { continue }
            for segment in reasoning.segments {
                guard case .text(let text) = segment else { continue }
                let update = ResponseUpdateNative(updateType: .reasoning, text: text.content,
                    messageId: reasoning.id, segmentId: text.id)
                queue?.async { onUpdate(update) } ?? onUpdate(update)
            }
            if let protectedData = try protectReasoning(reasoning) {
                let update = ResponseUpdateNative(updateType: .reasoning,
                    messageId: reasoning.id, protectedData: protectedData)
                queue?.async { onUpdate(update) } ?? onUpdate(update)
            }
        }
    }

    private struct ProtectedReasoning: Codable {
        let producer: String
        let transcript: Transcript
    }

    private var reasoningProducer: String { modelDirectory.map { "coreai:\($0)" } ?? "apple-intelligence" }
    private static let protectionPrefix = "foundation-models-reasoning-v1:"

    @available(iOS 27.0, macOS 27.0, visionOS 27.0, *)
    func protectReasoning(_ reasoning: Transcript.Reasoning) throws -> String? {
        guard reasoning.signature != nil else { return nil }
        // Preserve the signed native entry verbatim, including segment identities and metadata.
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let payload = ProtectedReasoning(producer: reasoningProducer, transcript: Transcript(entries: [.reasoning(reasoning)]))
        return Self.protectionPrefix + (try encoder.encode(payload)).base64EncodedString()
    }

    func replayReasoning(_ contents: [TextReasoningContentNative], messageId: String?) throws -> Transcript.Entry? {
        let protectedValues = Set(contents.compactMap(\.protectedData))
        guard !protectedValues.isEmpty else {
            // Unsigned traces are not answer text or fabricated tokens. CoreAILM ignores reasoning history.
            return nil
        }
        guard #available(iOS 27.0, macOS 27.0, visionOS 27.0, *),
              protectedValues.count == 1, let value = protectedValues.first,
              value.hasPrefix(Self.protectionPrefix),
              let bytes = Data(base64Encoded: String(value.dropFirst(Self.protectionPrefix.count))) else {
            throw NSError.chatError(.invalidContent, description: "Unsupported protected reasoning data.")
        }
        let payload = try JSONDecoder().decode(ProtectedReasoning.self, from: bytes)
        guard payload.producer == reasoningProducer, payload.transcript.count == 1,
              case .reasoning(let reasoning)? = payload.transcript.first,
              reasoning.signature != nil, reasoning.id == messageId else {
            throw NSError.chatError(.invalidContent, description: "Protected reasoning must retain its native message identity and producer.")
        }
        let text = contents.compactMap(\.text).joined()
        let signedText = reasoning.segments.compactMap { segment in
            if case .text(let text) = segment { return text.content }
            return nil
        }.joined()
        guard text.isEmpty || text == signedText else {
            throw NSError.chatError(.invalidContent, description: "Reasoning text does not match its protected native entry.")
        }
        return .reasoning(reasoning)
    }

    private func callbackQueue() -> DispatchQueue {
        // Tool callbacks and snapshot iteration can arrive on different native tasks.
        // One serial queue also keeps completion behind every pending update.
        DispatchQueue(label: "EssentialsAI.response", target: OperationQueue.current?.underlyingQueue)
    }

    // MARK: - Conversion to Foundation Models Helpers

    private func toPrompt(message: ChatMessageNative) throws -> Prompt {
        guard message.role == .user else {
            throw NSError.chatError(.invalidRole, description: "Only user messages can be prompts. Found: \(message.role)")
        }

        // Build one Prompt fragment per content item, then combine so that text and image
        // attachments interleave in order.
        let fragments: [Prompt] = try message.contents.map { content in
            switch content {
            case let textContent as TextContentNative:
                return Prompt { textContent.text }

            case let imageContent as ImageContentNative:
                if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                    let attachment = try imageContent.toAttachment()
                    return Prompt { attachment }
                } else {
                    throw NSError.chatError(.invalidContent, description: "Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.")
                }

            default:
                throw NSError.chatError(.invalidContent, description: "Unsupported content type in prompt. Found: \(type(of: content))")
            }
        }

        return Prompt {
            for fragment in fragments { fragment }
        }
    }

    private func toTranscriptEntries(message: ChatMessageNative) throws -> [Transcript.Entry] {
        switch message.role {
        case .user:
            return [try toUserEntry(message)]
        case .assistant:
            return try toAssistantEntries(message)
        case .system:
            return [try toSystemEntry(message)]
        case .tool:
            return try toToolEntries(message)
        default:
            throw NSError.chatError(.invalidRole, description: "Unsupported role in transcript. Found: \(message.role)")
        }
    }

    /// Maps a single content item to a transcript segment (text or image attachment).
    /// Shared by user prompts and system instructions so both can carry images.
    private func toSegment(_ content: AIContentNative) throws -> Transcript.Segment {
        switch content {
        case let textContent as TextContentNative:
            return .text(Transcript.TextSegment(content: textContent.text))

        case let imageContent as ImageContentNative:
            if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
                let attachment = try imageContent.toTranscriptAttachment()
                return .attachment(Transcript.AttachmentSegment(content: attachment, label: imageContent.label))
            } else {
                throw NSError.chatError(.invalidContent, description: "Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.")
            }

        default:
            throw NSError.chatError(.invalidContent, description: "Unsupported content type in message: \(type(of: content))")
        }
    }

    private func toUserEntry(_ message: ChatMessageNative) throws -> Transcript.Entry {
        let segments = try message.contents.map(self.toSegment)
        return .prompt(Transcript.Prompt(segments: segments))
    }

    func toAssistantEntries(_ message: ChatMessageNative) throws -> [Transcript.Entry] {
        // Process contents in order, flushing batches when the content type changes.
        // This preserves interleaving: [text, funcCall, text] → [.response, .toolCalls, .response]
        var entries: [Transcript.Entry] = []
        var pendingResponseSegments: [Transcript.Segment] = []
        var pendingToolCalls: [Transcript.ToolCall] = []
        var pendingReasoning: [TextReasoningContentNative] = []

        for content in message.contents {
            if let reasoning = content as? TextReasoningContentNative {
                if !pendingResponseSegments.isEmpty {
                    entries.append(.response(Transcript.Response(assetIDs: [], segments: pendingResponseSegments)))
                    pendingResponseSegments = []
                }
                if !pendingToolCalls.isEmpty {
                    entries.append(.toolCalls(Transcript.ToolCalls(pendingToolCalls)))
                    pendingToolCalls = []
                }
                pendingReasoning.append(reasoning)
                continue
            }
            if !pendingReasoning.isEmpty {
                if let entry = try replayReasoning(pendingReasoning, messageId: message.messageId) { entries.append(entry) }
                pendingReasoning = []
            }
            if let textContent = content as? TextContentNative {
                if !pendingToolCalls.isEmpty {
                    entries.append(.toolCalls(Transcript.ToolCalls(pendingToolCalls)))
                    pendingToolCalls = []
                }
                pendingResponseSegments.append(.text(Transcript.TextSegment(content: textContent.text)))
            } else if let imageContent = content as? ImageContentNative {
                if !pendingToolCalls.isEmpty {
                    entries.append(.toolCalls(Transcript.ToolCalls(pendingToolCalls)))
                    pendingToolCalls = []
                }
                pendingResponseSegments.append(try toSegment(imageContent))
            } else if let funcCall = content as? FunctionCallContentNative {
                if !pendingResponseSegments.isEmpty {
                    entries.append(.response(Transcript.Response(assetIDs: [], segments: pendingResponseSegments)))
                    pendingResponseSegments = []
                }
                let argsContent = (try? GeneratedContent(json: funcCall.arguments)) ?? GeneratedContent(funcCall.arguments)
                pendingToolCalls.append(Transcript.ToolCall(id: funcCall.callId, toolName: funcCall.name, arguments: argsContent))
            } else {
                throw NSError.chatError(.invalidContent, description: "Unsupported content type in assistant history: \(type(of: content))")
            }
        }

        if !pendingResponseSegments.isEmpty {
            entries.append(.response(Transcript.Response(assetIDs: [], segments: pendingResponseSegments)))
        }
        if !pendingToolCalls.isEmpty {
            entries.append(.toolCalls(Transcript.ToolCalls(pendingToolCalls)))
        }
        if !pendingReasoning.isEmpty {
            if let entry = try replayReasoning(pendingReasoning, messageId: message.messageId) { entries.append(entry) }
        }
        return entries
    }

    private func toSystemEntry(_ message: ChatMessageNative) throws -> Transcript.Entry {
        let segments = try message.contents.map(self.toSegment)
        return .instructions(Transcript.Instructions(segments: segments, toolDefinitions: []))
    }

    private func toToolEntries(_ message: ChatMessageNative) throws -> [Transcript.Entry] {
        return try message.contents.map { content in
            guard let funcResult = content as? FunctionResultContentNative else {
                throw NSError.chatError(.invalidContent, description: "Unsupported content type in tool message: \(type(of: content))")
            }
            let segment = Transcript.Segment.text(Transcript.TextSegment(content: funcResult.result))
            return .toolOutput(Transcript.ToolOutput(id: funcResult.callId, toolName: funcResult.name, segments: [segment]))
        }
    }

    // MARK: - Conversion to Essentials AI Helpers

    private func fromTranscriptEntry(_ entry: Transcript.Entry, includeReasoning: Bool) throws -> ChatMessageNative? {
        switch entry {
        case .prompt(let prompt):
            let message = ChatMessageNative()
            message.role = .user
            message.contents = prompt.segments.compactMap(fromTranscriptSegment)
            return message

        case .response(let response):
            let message = ChatMessageNative()
            message.role = .assistant
            message.messageId = response.id
            message.contents = response.segments.compactMap(fromTranscriptSegment)
            return message

        case .instructions(let instructions):
            let message = ChatMessageNative()
            message.role = .system
            message.contents = instructions.segments.compactMap(fromTranscriptSegment)
            return message

        case .toolCalls(let toolCalls):
            let message = ChatMessageNative()
            message.role = .assistant
            message.contents = toolCalls.map(fromToolCall)
            return message

        case .toolOutput(let toolOutput):
            let message = ChatMessageNative()
            message.role = .tool
            message.contents = fromToolOutput(toolOutput)
            return message

        default:
            if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *),
               includeReasoning, case .reasoning(let reasoning) = entry {
                let message = ChatMessageNative()
                message.role = .assistant
                message.messageId = reasoning.id
                let text = reasoning.segments.compactMap { segment in
                    if case .text(let text) = segment { return text.content }
                    return nil
                }.joined()
                message.contents = [TextReasoningContentNative(text: text,
                    protectedData: try protectReasoning(reasoning))]
                return message
            }
            return nil
        }
    }

    private func fromToolCall(_ toolCall: Transcript.ToolCall) -> AIContentNative {
        let argsJson = toolCall.arguments.jsonString
        return FunctionCallContentNative(callId: toolCall.id, name: toolCall.toolName, arguments: argsJson)
    }

    private func fromToolOutput(_ toolOutput: Transcript.ToolOutput) -> [AIContentNative] {
        return toolOutput.segments.compactMap { segment -> AIContentNative? in
            let resultText: String
            switch segment {
            case .text(let textSegment):
                resultText = textSegment.content
            case .structure(let structuredSegment):
                resultText = structuredSegment.content.jsonString
            default:
                // Attachment/custom/future tool-output segments are not represented as text.
                return nil
            }

            return FunctionResultContentNative(callId: toolOutput.id, name: toolOutput.toolName, result: resultText)
        }
    }

    private func fromTranscriptSegment(_ segment: Transcript.Segment) -> AIContentNative? {
        switch segment {
        case .text(let textSegment):
            return TextContentNative(text: textSegment.content)

        case .structure(let structuredSegment):
            // For now, convert structured content to text
            return TextContentNative(text: structuredSegment.content.jsonString)

        default:
            // Image attachment segments are available on 27.0+. Match inside the availability
            // guard so the case references compile against the 26.x deployment target. Other
            // segment kinds (custom, future) are not represented as content.
            if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *),
               case .attachment(let attachmentSegment) = segment,
               case .image(let image) = attachmentSegment.content {
                return ImageContentNative(
                    cgImage: image.cgImage,
                    orientationValue: NSNumber(value: image.orientation.rawValue),
                    label: attachmentSegment.label)
            }
            return nil
        }
    }

    // MARK: - Logging Helpers

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
    private func formatMessagesDetailed(_ messages: [ChatMessageNative]) -> String {
        let formatted = messages.map { message -> String in
            let role = "\(message.role)"
            let contents = message.contents.map { content -> String in
                switch content {
                case let text as TextContentNative:
                    return "TextContent(text=\"\(text.text)\")"
                case let funcCall as FunctionCallContentNative:
                    return "FunctionCallContent(name=\"\(funcCall.name)\", callId=\"\(funcCall.callId)\", arguments=\(funcCall.arguments))"
                case let funcResult as FunctionResultContentNative:
                    return "FunctionResultContent(callId=\"\(funcResult.callId)\", result=\"\(funcResult.result)\")"
                default:
                    return "UnknownContent(\(type(of: content)))"
                }
            }.joined(separator: ", ")
            return "Message(role=\(role), contents=[\(contents)])"
        }.joined(separator: ", ")
        return "[\(formatted)]"
    }

    private func formatOptionsDetailed(_ options: ChatOptionsNative) -> String {
        var parts: [String] = []
        if let topK = options.topK { parts.append("topK=\(topK)") }
        if let temp = options.temperature { parts.append("temperature=\(temp)") }
        if let maxTokens = options.maxOutputTokens { parts.append("maxOutputTokens=\(maxTokens)") }
        if let seed = options.seed { parts.append("seed=\(seed)") }
        if let schema = options.responseJsonSchema { parts.append("responseJsonSchema=\(schema)") }
        if let tools = options.tools {
            let toolNames = tools.map { "\($0.name)" }.joined(separator: ", ")
            parts.append("tools=[\(toolNames)]")
        }
        return "Options(\(parts.joined(separator: ", ")))"
    }

    private func formatResponseDetailed(_ response: ChatResponseNative) -> String {
        let messagesStr = response.messages.map { message -> String in
            let role = "\(message.role)"
            let contents = message.contents.map { content -> String in
                switch content {
                case let text as TextContentNative:
                    return "TextContent(text=\"\(text.text)\")"
                case let funcCall as FunctionCallContentNative:
                    return "FunctionCallContent(name=\"\(funcCall.name)\", callId=\"\(funcCall.callId)\", arguments=\(funcCall.arguments))"
                case let funcResult as FunctionResultContentNative:
                    return "FunctionResultContent(callId=\"\(funcResult.callId)\", result=\"\(funcResult.result)\")"
                default:
                    return "UnknownContent(\(type(of: content)))"
                }
            }.joined(separator: ", ")
            return "Message(role=\(role), contents=[\(contents)])"
        }.joined(separator: ", ")
        return "ChatResponse(messages=[\(messagesStr)])"
    }
#endif
}

extension NSError {

    static func chatError(_ code: ChatClientError, description: String) -> NSError {
        NSError(
            domain: "ChatClientNative",
            code: code.rawValue,
            userInfo: [NSLocalizedDescriptionKey: description]
        )
    }
}

extension Error {

    fileprivate func toNSError() -> NSError {
        switch self
        {
        case let error as LanguageModelSession.GenerationError:
            return NSError(
                domain: "ChatClientNative",
                code: 0,
                userInfo: [
                    NSUnderlyingErrorKey: error.errorDescription ?? "",
                    NSLocalizedRecoverySuggestionErrorKey: error.recoverySuggestion ?? "",
                    NSLocalizedFailureReasonErrorKey: error.failureReason ?? "",
                    NSLocalizedDescriptionKey: error.localizedDescription,
                ]
            )

        case let error as LanguageModelSession.ToolCallError:
            return NSError(
                domain: "ChatClientNative",
                code: 0,
                userInfo: [
                    NSUnderlyingErrorKey: error.errorDescription ?? "",
                    NSLocalizedRecoverySuggestionErrorKey: error.recoverySuggestion ?? "",
                    NSLocalizedFailureReasonErrorKey: error.failureReason ?? "",
                    NSLocalizedDescriptionKey: error.localizedDescription,
                ]
            )

        case let error as LocalizedError:
            return NSError(
                domain: "ChatClientNative",
                code: 0,
                userInfo: [
                    NSUnderlyingErrorKey: error.errorDescription ?? "",
                    NSLocalizedRecoverySuggestionErrorKey: error.recoverySuggestion ?? "",
                    NSLocalizedFailureReasonErrorKey: error.failureReason ?? "",
                    NSLocalizedDescriptionKey: error.localizedDescription,
                ]
            )

        case is CancellationError:
            return NSError.chatError(.cancelled, description: "Request was cancelled.")

        case let error as NSError:
            return error

        default:
            return NSError(
                domain: "ChatClientNative",
                code: 0,
                userInfo: [
                    NSLocalizedDescriptionKey: self.localizedDescription
                ]
            )
        }

    }

}
