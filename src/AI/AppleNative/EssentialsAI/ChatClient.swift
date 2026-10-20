import Foundation
import CoreGraphics
import ImageIO
import FoundationModels

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

/// Preserves request-local callback order while retaining the invocation queue as the execution target.
final class CallbackDispatcher: @unchecked Sendable {
    private let queue: DispatchQueue

    init(target: DispatchQueue?) {
        queue = DispatchQueue(label: "Microsoft.Maui.EssentialsAI.Callbacks", target: target)
    }

    func dispatch(_ callback: @escaping () -> Void) {
        queue.async(execute: callback)
    }
}

/// Known availability values returned through the Objective-C bridge.
/// Unknown future reasons are still returned as their native string description.
enum AppleIntelligenceModelAvailabilityNative {
    static let available = "available"
    static let deviceNotEligible = "deviceNotEligible"
    static let appleIntelligenceNotEnabled = "appleIntelligenceNotEnabled"
    static let modelNotReady = "modelNotReady"
}

/// Capability flags returned through the Objective-C bridge.
/// Keep these values aligned with AppleIntelligenceModelCapabilitiesNative in managed code.
struct AppleIntelligenceModelCapabilitiesNative: OptionSet {
    let rawValue: UInt64

    static let vision = Self(rawValue: 1 << 0)
    static let guidedGeneration = Self(rawValue: 1 << 1)
    static let reasoning = Self(rawValue: 1 << 2)
    static let toolCalling = Self(rawValue: 1 << 3)
}

@objc(ChatClientError)
public enum ChatClientError: Int {
    case emptyMessages = 1
    case invalidRole = 2
    case invalidContent = 3
    case cancelled = 4
}

@objc(ChatClientNative)
public class ChatClientNative: NSObject {

    @objc public static func getModelAvailability() -> String {
        availabilityName(SystemLanguageModel.default.availability)
    }

    static func availabilityName(_ availability: SystemLanguageModel.Availability) -> String {
        switch availability {
        case .available: return AppleIntelligenceModelAvailabilityNative.available
        case .unavailable(let reason):
            switch reason {
            case .deviceNotEligible: return AppleIntelligenceModelAvailabilityNative.deviceNotEligible
            case .appleIntelligenceNotEnabled: return AppleIntelligenceModelAvailabilityNative.appleIntelligenceNotEnabled
            case .modelNotReady: return AppleIntelligenceModelAvailabilityNative.modelNotReady
            @unknown default: return String(describing: reason)
            }
        }
    }

    @objc public static func getModelCapabilities() -> NSNumber? {
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
            let capabilities = SystemLanguageModel.default.capabilities
            var flags: AppleIntelligenceModelCapabilitiesNative = []
            if capabilities.contains(.vision) { flags.insert(.vision) }
            if capabilities.contains(.guidedGeneration) { flags.insert(.guidedGeneration) }
            if capabilities.contains(.reasoning) { flags.insert(.reasoning) }
            if capabilities.contains(.toolCalling) { flags.insert(.toolCalling) }
            return NSNumber(value: flags.rawValue)
        }
        return nil
    }

    static func validateImageSupport(hasImages: Bool, supportsOS27: Bool, supportsVision: Bool) throws {
        guard hasImages else { return }
        guard supportsOS27 else {
            throw NSError.chatError(.invalidContent, description: "Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.")
        }
        guard supportsVision else {
            throw NSError.chatError(.invalidContent, description: "The current Apple Intelligence model does not report vision support.")
        }
    }

    // MARK: - Stream Response

    @objc public func streamResponse(
        messages: [ChatMessageNative],
        options: ChatOptionsNative?,
        onUpdate: @escaping (ResponseUpdateNative) -> Void,
        onComplete: @escaping (ChatResponseNative?, NSError?) -> Void
    ) -> CancellationTokenNative? {

        let methodName = "streamResponse"
        let callbacks = CallbackDispatcher(target: OperationQueue.current?.underlyingQueue)
        let textDeltas = PlainTextDeltaEmitter()

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
                    textDeltas.toolCall {
                        callbacks.dispatch { onUpdate(update) }
                    }
                },
                onToolResult: { id, name, result in
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Tool completed: \(name) (id=\(id)) with result: \(result)")
                    }
#endif

                    let update = ResponseUpdateNative(updateType: .toolResult, toolCallId: id, toolCallName: name, toolCallResult: result)
                    callbacks.dispatch { onUpdate(update) }
                }
            )

        return executeTask(methodName, messages, options, toolWatcher, callbacks, onComplete) { session, prompt, schema, genOptions in

            if let jsonSchema = schema {
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Starting schema-based stream response")
                }
#endif

                // Keep the schema in the prompt as well as the grammar so nested fields retain their meaning.
                let responseStream = session.streamResponse(to: prompt, schema: jsonSchema, options: genOptions)

                // Native completion metadata yields append-only JSON deltas; the state is local to this response.
                var deltas = StructuredJsonDeltas()
                let emit: (String) -> Void = { delta in
                    guard !delta.isEmpty else { return }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Streaming update: \(delta)")
                    }
#endif
                    let update = ResponseUpdateNative(updateType: .content, text: delta)
                    callbacks.dispatch { onUpdate(update) }
                }

                for try await snapshot in responseStream {
                    try Task.checkCancellation()
                    emit(try deltas.process(snapshot.rawContent))
                }

                let response = try await responseStream.collect()
                emit(try deltas.process(response.rawContent, final: true))
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Stream collected, content length: \(response.content.jsonString.count)")
                }
#endif
                return (response.content.jsonString, response.transcriptEntries, self.toUsageDetailsNative(response))
            } else {
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Starting text-based stream response")
                }
#endif

                let responseStream = session.streamResponse(to: prompt, options: genOptions)
                let emit: (String) -> Void = { delta in
                    guard !delta.isEmpty else { return }
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Streaming update: \(delta)")
                    }
#endif
                    let update = ResponseUpdateNative(updateType: .content, text: delta)
                    callbacks.dispatch { onUpdate(update) }
                }

                for try await response in responseStream {
                    try Task.checkCancellation()
                    try textDeltas.process(response.content, emit: emit)
                }

                let response = try await responseStream.collect()
                try textDeltas.process(response.content, emit: emit)
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Stream collected, content length: \(response.content.count)")
                }
#endif
                return (response.content, response.transcriptEntries, self.toUsageDetailsNative(response))
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
        let callbacks = CallbackDispatcher(target: OperationQueue.current?.underlyingQueue)

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
                    callbacks.dispatch { onUpdate(update) }
                },
                onToolResult: { id, name, result in
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Tool completed: \(name) (id=\(id)) with result: \(result)")
                    }
#endif

                    let update = ResponseUpdateNative(updateType: .toolResult, toolCallId: id, toolCallName: name, toolCallResult: result)
                    callbacks.dispatch { onUpdate(update) }
                }
            )

        return executeTask(methodName, messages, options, toolWatcher, callbacks, onComplete) { session, prompt, schema, genOptions in

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
            if let log = AppleIntelligenceLogger.log {
                log("[\(methodName)] \(schema != nil ? "Getting schema-based response" : "Getting text-based response")")
            }
#endif

            let response = try await {
                if let jsonSchema = schema {
                    let inner = try await session.respond(to: prompt, schema: jsonSchema, options: genOptions)
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Response received, content length: \(inner.content.jsonString.count)")
                    }
#endif
                    return (inner.content.jsonString, inner.transcriptEntries, self.toUsageDetailsNative(inner))
                } else {
                    let inner = try await session.respond(to: prompt, options: genOptions)
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                    if let log = AppleIntelligenceLogger.log {
                        log("[\(methodName)] Response received, content length: \(inner.content.count)")
                    }
#endif
                    return (inner.content, inner.transcriptEntries, self.toUsageDetailsNative(inner))
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

        let model = SystemLanguageModel.default
        let hasImages = messages.contains(where: { message in message.contents.contains(where: { $0 is ImageContentNative }) })
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
            try Self.validateImageSupport(hasImages: hasImages, supportsOS27: true, supportsVision: model.capabilities.contains(.vision))
        } else {
            try Self.validateImageSupport(hasImages: hasImages, supportsOS27: false, supportsVision: false)
        }

        let tools = try options?.toolCallingMode == .disallowed ? [] :
            options?.tools?.map { try ToolNative($0, toolWatcher?.notifyToolCall, toolWatcher?.notifyToolResult) } ?? []

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

        let genOptions = try (options ?? ChatOptionsNative()).toGenerationOptions()

        let availability = Self.getModelAvailability()
        guard availability == AppleIntelligenceModelAvailabilityNative.available else {
            throw NSError.chatError(.invalidContent, description: "Apple Intelligence system model is unavailable: \(availability)")
        }

        let session = Self.makeSession(model: model, tools: tools, transcript: transcript,
                                       toolCallingMode: options?.toolCallingMode ?? .allowed)

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
        if let log = AppleIntelligenceLogger.log {
            log("[\(methodName)] Session ready, hasSchema=\(schema != nil)")
        }
#endif

        return (session, prompt, schema, genOptions)
    }

    static func makeSession(
        model: SystemLanguageModel, tools: [ToolNative], transcript: Transcript, toolCallingMode: ToolCallingModeNative
    ) -> LanguageModelSession {
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *), toolCallingMode == .required {
            return LanguageModelSession(profile: RequiredToolProfile(model: model, tools: tools), history: transcript)
        }
        return LanguageModelSession(model: model, tools: tools, transcript: transcript)
    }

    private func toUsageDetailsNative<Content>(
        _ response: LanguageModelSession.Response<Content>
    ) -> UsageDetailsNative? where Content: Generable {
#if compiler(>=6.4)
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
            let usage = response.usage
            return UsageDetailsNative(
                inputTokenCount: usage.input.totalTokenCount,
                outputTokenCount: usage.output.totalTokenCount,
                totalTokenCount: usage.totalTokenCount,
                cachedInputTokenCount: usage.input.cachedTokenCount,
                reasoningTokenCount: usage.output.reasoningTokenCount
            )
        }
#endif

        return nil
    }

    private func executeTask(
        _ methodName: String,
        _ messages: [ChatMessageNative],
        _ options: ChatOptionsNative?,
        _ toolWatcher: ToolCallWatcher?,
        _ callbacks: CallbackDispatcher,
        _ onComplete: @escaping (ChatResponseNative?, NSError?) -> Void,
        operation:
            @escaping (LanguageModelSession, Prompt, GenerationSchema?, GenerationOptions) async throws
            -> (String, ArraySlice<Transcript.Entry>, UsageDetailsNative?)
    ) -> CancellationTokenNative? {

        guard !messages.isEmpty else {
            let error = NSError.chatError(.emptyMessages, description: "No messages provided.")
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
            if let log = AppleIntelligenceLogger.log {
                log("[\(methodName)] Failed: No messages provided")
            }
#endif

            callbacks.dispatch { onComplete(nil, error.toNSError()) }
            return nil
        }

        let task = Task {
            do {
                try Task.checkCancellation()

                let (session, prompt, schema, genOptions) = try await self.prepareSession(methodName, messages, options, toolWatcher)

                let result = try await operation(session, prompt, schema, genOptions)

                try Task.checkCancellation()

                // Convert transcript entries to messages
                let transcriptMessages = try result.1.compactMap(self.fromTranscriptEntry)
                guard !result.0.isEmpty, !transcriptMessages.isEmpty else {
                    throw NSError.chatError(.invalidContent, description: "Apple Intelligence returned an empty native response.")
                }

                // Create response with all transcript messages
                let response = ChatResponseNative(messages: transcriptMessages, usage: result.2)

#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Completed with \(transcriptMessages.count) messages")
                    log("[\(methodName)] Response: \(self.formatResponseDetailed(response))")
                }
#endif

                callbacks.dispatch { onComplete(response, nil) }
            } catch {
#if APPLE_INTELLIGENCE_LOGGING_ENABLED
                if let log = AppleIntelligenceLogger.log {
                    log("[\(methodName)] Failed: \(error.localizedDescription)")
                }
#endif

                let nativeError = error.toNSError(isCancelled: Task.isCancelled)
                callbacks.dispatch { onComplete(nil, nativeError) }
            }
        }

        return CancellationTokenNative(task: task)
    }

    // MARK: - Conversion to Foundation Models Helpers

    func toPrompt(message: ChatMessageNative) throws -> Prompt {
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

    func toTranscriptEntries(message: ChatMessageNative) throws -> [Transcript.Entry] {
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

    private func toAssistantEntries(_ message: ChatMessageNative) throws -> [Transcript.Entry] {
        // Process contents in order, flushing batches when the content type changes.
        // This preserves interleaving: [text, funcCall, text] → [.response, .toolCalls, .response]
        var entries: [Transcript.Entry] = []
        var pendingResponseSegments: [Transcript.Segment] = []
        var pendingToolCalls: [Transcript.ToolCall] = []

        if message.contents.isEmpty {
            return [.response(Transcript.Response(assetIDs: [], segments: [.text(Transcript.TextSegment(content: ""))]))]
        }

        for content in message.contents {
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

    private func fromTranscriptEntry(_ entry: Transcript.Entry) throws -> ChatMessageNative? {
        switch entry {
        case .prompt(let prompt):
            let message = ChatMessageNative()
            message.role = .user
            message.contents = prompt.segments.compactMap(fromTranscriptSegment)
            return message

        case .response(let response):
            let message = ChatMessageNative()
            message.role = .assistant
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
            // Reasoning and any future entry kinds are not surfaced as messages.
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
        if let topP = options.topP { parts.append("topP=\(topP)") }
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

@available(iOS 27.0, macOS 27.0, visionOS 27.0, *)
extension SessionPropertyValues {
    @SessionPropertyEntry var essentialsAIRequiredToolCallCount: Int = 0
}

@available(iOS 27.0, macOS 27.0, visionOS 27.0, *)
struct RequiredToolProfile: LanguageModelSession.DynamicProfile {
    let model: SystemLanguageModel
    let tools: [any Tool]

    @SessionProperty(\.essentialsAIRequiredToolCallCount) var toolCallCount

    static func toolCallingMode(for count: Int) -> GenerationOptions.ToolCallingMode {
        count < 1 ? .required : .allowed
    }

    var body: some LanguageModelSession.DynamicProfile {
        Profile { tools }
            .model(model)
            .toolCallingMode(Self.toolCallingMode(for: toolCallCount))
            .onToolCall { toolCallCount += 1 }
    }
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

    func toNSError(isCancelled: Bool = false) -> NSError {
        if isCancelled || self is CancellationError {
            return NSError.chatError(.cancelled, description: "Request was cancelled.")
        }

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
