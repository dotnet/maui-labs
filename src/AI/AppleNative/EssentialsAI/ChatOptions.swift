import Foundation
import FoundationModels

@objc(ChatOptionsNative)
public class ChatOptionsNative: NSObject {
    @objc public var topK: NSNumber? = nil
    @objc public var topP: NSNumber? = nil
    @objc public var seed: NSNumber? = nil
    @objc public var temperature: NSNumber? = nil
    @objc public var maxOutputTokens: NSNumber? = nil
    @objc public var responseJsonSchema: NSString? = nil
    @objc public var tools: [any AIToolNative]? = nil

    func toGenerationOptions() throws -> GenerationOptions {
        if topK != nil && topP != nil {
            throw NSError.chatError(.invalidContent, description: "The Apple Intelligence adapter supports either TopK or TopP, not both.")
        }
        if let topK, topK.intValue <= 0 {
            throw NSError.chatError(.invalidContent, description: "TopK must be greater than zero.")
        }
        if let topP, !topP.doubleValue.isFinite || !(0...1).contains(topP.doubleValue) {
            throw NSError.chatError(.invalidContent, description: "TopP must be finite and between zero and one.")
        }
        if let temperature, !temperature.doubleValue.isFinite || temperature.doubleValue < 0 {
            throw NSError.chatError(.invalidContent, description: "Temperature must be finite and nonnegative.")
        }
        if let maxOutputTokens, maxOutputTokens.intValue <= 0 {
            throw NSError.chatError(.invalidContent, description: "MaxOutputTokens must be greater than zero.")
        }

        let samplingMode: GenerationOptions.SamplingMode
        if let topK {
            samplingMode = .random(top: topK.intValue, seed: seed?.uint64Value)
        } else if let topP {
            samplingMode = .random(probabilityThreshold: topP.doubleValue, seed: seed?.uint64Value)
        } else if temperature != nil || seed != nil {
            samplingMode = .random(probabilityThreshold: 1, seed: seed?.uint64Value)
        } else {
            samplingMode = .greedy
        }

        return GenerationOptions(
            samplingMode: samplingMode,
            temperature: temperature?.doubleValue,
            maximumResponseTokens: maxOutputTokens?.intValue)
    }
}
