import Foundation
import FoundationModels

@objc(ChatOptionsNative)
public class ChatOptionsNative: NSObject {
    @objc public var topK: NSNumber? = nil
    @objc public var seed: NSNumber? = nil
    @objc public var temperature: NSNumber? = nil
    @objc public var maxOutputTokens: NSNumber? = nil
    @objc public var responseJsonSchema: NSString? = nil
    @objc public var tools: [any AIToolNative]? = nil
    @objc public var requireAnyTool: Bool = false
}

@available(iOS 27.0, macOS 27.0, macCatalyst 27.0, visionOS 27.0, *)
extension SessionPropertyValues {
    @SessionPropertyEntry var essentialsAIHasCalledTool = false
}

@available(iOS 27.0, macOS 27.0, macCatalyst 27.0, visionOS 27.0, *)
struct RequireAnyToolProfile: LanguageModelSession.DynamicProfile {
    let tools: [ToolNative]
    @SessionProperty(\.essentialsAIHasCalledTool) private var hasCalledTool

    var body: some LanguageModelSession.DynamicProfile {
        LanguageModelSession.Profile {
            tools as [any Tool]
        }
        .model(SystemLanguageModel.default)
        .toolCallingMode(hasCalledTool ? .allowed : .required)
        .onToolCall { hasCalledTool = true }
    }
}
