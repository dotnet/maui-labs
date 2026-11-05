import Foundation
import FoundationModels

@objc(ChatResponseNative)
public class ChatResponseNative: NSObject, @unchecked Sendable {
    @objc public var messages: [ChatMessageNative]
    @objc public let usage: UsageDetailsNative?
    
    @objc public init(messages: [ChatMessageNative]) {
        self.messages = messages
        self.usage = nil
        super.init()
    }

    @objc public init(messages: [ChatMessageNative], usage: UsageDetailsNative?) {
        self.messages = messages
        self.usage = usage
        super.init()
    }
}

@objc(UsageDetailsNative)
public final class UsageDetailsNative: NSObject, Sendable {
    @objc public let inputTokenCount: Int
    @objc public let outputTokenCount: Int
    @objc public let totalTokenCount: Int
    @objc public let cachedInputTokenCount: Int
    @objc public let reasoningTokenCount: Int

    @objc public init(
        inputTokenCount: Int,
        outputTokenCount: Int,
        totalTokenCount: Int,
        cachedInputTokenCount: Int,
        reasoningTokenCount: Int
    ) {
        self.inputTokenCount = inputTokenCount
        self.outputTokenCount = outputTokenCount
        self.totalTokenCount = totalTokenCount
        self.cachedInputTokenCount = cachedInputTokenCount
        self.reasoningTokenCount = reasoningTokenCount
        super.init()
    }
}
