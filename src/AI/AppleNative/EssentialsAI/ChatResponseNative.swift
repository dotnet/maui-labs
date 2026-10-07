import Foundation
import FoundationModels

@objc(ChatResponseNative)
public class ChatResponseNative: NSObject, @unchecked Sendable {
    @objc public var messages: [ChatMessageNative]
    @objc public var inputTokenCount: NSNumber?
    @objc public var outputTokenCount: NSNumber?
    @objc public var totalTokenCount: NSNumber?
    
    @objc public init(messages: [ChatMessageNative]) {
        self.messages = messages
        super.init()
    }
}
