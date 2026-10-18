import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class ChatOptionsTests: XCTestCase {
    func testOmittedOptionsPreserveNativeDefaults() {
        XCTAssertEqual(ChatOptionsNative().toGenerationOptions(), GenerationOptions())
    }

    func testTemperatureDoesNotForceGreedySampling() {
        let options = ChatOptionsNative()
        options.temperature = 0.7
        options.maxOutputTokens = 100

        XCTAssertEqual(options.toGenerationOptions(),
                       GenerationOptions(temperature: 0.7, maximumResponseTokens: 100))
    }

    func testTopKPrecedesTopPAndPreservesSeed() {
        let options = ChatOptionsNative()
        options.topK = 10
        options.topP = 0.75
        options.seed = 42

        XCTAssertEqual(options.toGenerationOptions(),
                       GenerationOptions(samplingMode: .random(top: 10, seed: 42)))
    }

    func testNucleusSamplingPreservesBoundariesAndSeed() {
        for threshold in [0.0, 0.75, 1.0] {
            let options = ChatOptionsNative()
            options.topP = NSNumber(value: threshold)
            options.seed = 42

            XCTAssertEqual(options.toGenerationOptions(),
                           GenerationOptions(samplingMode: .random(probabilityThreshold: threshold, seed: 42)))
        }
    }

    func testSeedWithoutSamplingModeUsesFullProbabilityPool() {
        let options = ChatOptionsNative()
        options.seed = -1

        XCTAssertEqual(options.toGenerationOptions(),
                       GenerationOptions(samplingMode: .random(probabilityThreshold: 1, seed: UInt64.max)))
    }

    func testRandomModesWithoutSeedPreserveNativeRandomness() {
        let options = ChatOptionsNative()
        options.topK = 10

        XCTAssertEqual(options.toGenerationOptions(),
                       GenerationOptions(samplingMode: .random(top: 10)))

        options.topK = nil
        options.topP = 0.75

        XCTAssertEqual(options.toGenerationOptions(),
                       GenerationOptions(samplingMode: .random(probabilityThreshold: 0.75)))
    }
}
