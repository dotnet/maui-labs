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

    func toGenerationOptions() -> GenerationOptions {
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

        return GenerationOptions(
            samplingMode: samplingMode,
            temperature: temperature?.doubleValue,
            maximumResponseTokens: maxOutputTokens?.intValue
        )
    }
}
