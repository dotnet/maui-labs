import Foundation
import FoundationModels

@objc(ToolCallingModeNative)
public enum ToolCallingModeNative: Int {
    case allowed = 0
    case disallowed = 1
    case required = 2
}

@objc(ChatOptionsNative)
public class ChatOptionsNative: NSObject {
    @objc public var topK: NSNumber? = nil
    @objc public var topP: NSNumber? = nil
    @objc public var seed: NSNumber? = nil
    @objc public var temperature: NSNumber? = nil
    @objc public var maxOutputTokens: NSNumber? = nil
    @objc public var responseJsonSchema: NSString? = nil
    @objc public var tools: [any AIToolNative]? = nil
    @objc public var toolCallingMode: ToolCallingModeNative = .allowed

    func toGenerationOptions() throws -> GenerationOptions {
        if let seed, seed.doubleValue < 0 {
            throw NSError.chatError(.invalidContent, description: "Seed must be nonnegative.")
        }
        let samplingMode: GenerationOptions.SamplingMode?

        // Native sampling modes are exclusive; an omitted mode preserves the model's defaults.
        if let topK = topK?.intValue {
            samplingMode = .random(top: topK, seed: seed?.uint64Value)
        } else if let topP = topP?.doubleValue {
            samplingMode = .random(probabilityThreshold: topP, seed: seed?.uint64Value)
        } else if let seed = seed?.uint64Value {
            // Apply the seed without arbitrarily narrowing the candidate token pool.
            samplingMode = .random(probabilityThreshold: 1, seed: seed)
        } else {
            samplingMode = nil
        }

        var result = GenerationOptions(
            samplingMode: samplingMode,
            temperature: temperature?.doubleValue,
            maximumResponseTokens: maxOutputTokens?.intValue
        )
        if #available(iOS 27.0, macOS 27.0, visionOS 27.0, *) {
            switch toolCallingMode {
            case .allowed: result.toolCallingMode = .allowed
            case .disallowed: result.toolCallingMode = .disallowed
            // Required is controlled by the session's dynamic profile, not a persistent request override.
            case .required: break
            }
        } else if toolCallingMode == .required {
            throw NSError.chatError(.invalidContent, description: "Required tool calling requires Apple OS 27 or later.")
        }
        return result
    }
}
