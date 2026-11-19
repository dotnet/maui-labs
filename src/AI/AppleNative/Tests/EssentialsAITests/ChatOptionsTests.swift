import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class ChatOptionsTests: XCTestCase {
    func testOmittedOptionsPreserveNativeDefaults() throws {
        var expected = GenerationOptions()
        if #available(macOS 27.0, *) { expected.toolCallingMode = .allowed }
        XCTAssertEqual(try ChatOptionsNative().toGenerationOptions(), expected)
    }

    func testTemperatureDoesNotForceGreedySampling() throws {
        let options = ChatOptionsNative()
        options.temperature = 0.7
        options.maxOutputTokens = 100

        let result = try options.toGenerationOptions()
        XCTAssertNil(result.samplingMode)
        XCTAssertEqual(result.temperature, 0.7)
        XCTAssertEqual(result.maximumResponseTokens, 100)
    }

    func testTopKPrecedesTopPAndPreservesSeed() throws {
        let options = ChatOptionsNative()
        options.topK = 10
        options.topP = 0.75
        options.seed = 42

        XCTAssertEqual(try options.toGenerationOptions().samplingMode, .random(top: 10, seed: 42))
    }

    func testNucleusSamplingPreservesBoundariesAndSeed() throws {
        for threshold in [0.0, 0.75, 1.0] {
            let options = ChatOptionsNative()
            options.topP = NSNumber(value: threshold)
            options.seed = 42

            XCTAssertEqual(try options.toGenerationOptions().samplingMode,
                           .random(probabilityThreshold: threshold, seed: 42))
        }
    }

    func testSeedWithoutSamplingModeUsesFullProbabilityPool() throws {
        let options = ChatOptionsNative()
        options.seed = NSNumber(value: Int64.max)

        XCTAssertEqual(try options.toGenerationOptions().samplingMode,
                       .random(probabilityThreshold: 1, seed: UInt64(Int64.max)))
    }

    func testRandomModesWithoutSeedPreserveNativeRandomness() throws {
        let options = ChatOptionsNative()
        options.topK = 10

        XCTAssertEqual(try options.toGenerationOptions().samplingMode, .random(top: 10))

        options.topK = nil
        options.topP = 0.75

        XCTAssertEqual(try options.toGenerationOptions().samplingMode, .random(probabilityThreshold: 0.75))
    }

    func testNegativeSeedIsRejectedBeforeUnsignedConversion() {
        for topK in [nil, NSNumber(value: 10)] {
            let options = ChatOptionsNative()
            options.seed = -1
            options.topK = topK
            XCTAssertThrowsError(try options.toGenerationOptions())
        }
    }

    func testOptionalToolCallingModesMatchNativeOptions() throws {
        guard #available(macOS 27.0, *) else { return }
        for (mode, expected) in [
            (ToolCallingModeNative.allowed, GenerationOptions.ToolCallingMode.allowed),
            (.disallowed, .disallowed)
        ] {
            let options = ChatOptionsNative()
            options.toolCallingMode = mode
            XCTAssertEqual(try options.toGenerationOptions().toolCallingMode, expected)
        }
    }

    func testRequiredLeavesToolModeToDynamicProfileAndPreservesSamplingOptions() throws {
        guard #available(macOS 27.0, *) else { return }
        let options = ChatOptionsNative()
        options.toolCallingMode = .required
        options.topK = 10
        options.topP = 0.5
        options.seed = 42
        options.temperature = 0.3
        options.maxOutputTokens = 128

        let native = try options.toGenerationOptions()
        XCTAssertNil(native.toolCallingMode, "A persistent request mode would override the dynamic profile's exit condition.")
        XCTAssertEqual(native.samplingMode, .random(top: 10, seed: 42))
        XCTAssertEqual(native.temperature, 0.3)
        XCTAssertEqual(native.maximumResponseTokens, 128)
    }
}
