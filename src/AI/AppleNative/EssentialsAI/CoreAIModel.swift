#if ENABLE_CORE_AI
import Foundation
import CoreAILanguageModels

@available(iOS 27.0, macOS 27.0, *)
enum CoreAIModel {
    static func load(at url: URL) async throws -> CoreAILanguageModel {
        let bundle = try LanguageModelBundle(at: url)
        guard bundle.hasEmbeddedTokenizer else {
            throw NSError(domain: "CoreAI", code: 1, userInfo: [
                NSLocalizedDescriptionKey: "Core AI requires a complete local tokenizer directory; network fallback is disabled."
            ])
        }
        return try await CoreAILanguageModel(resourcesAt: url)
    }
}
#endif
