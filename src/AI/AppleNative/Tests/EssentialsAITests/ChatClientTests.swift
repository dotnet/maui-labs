import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class ChatClientTests: XCTestCase {
    func testAvailabilityPreservesEveryNativeReason() {
        XCTAssertEqual(ChatClientNative.availabilityName(.available),
                       AppleIntelligenceModelAvailabilityNative.available)
        XCTAssertEqual(ChatClientNative.availabilityName(.unavailable(.deviceNotEligible)),
                       AppleIntelligenceModelAvailabilityNative.deviceNotEligible)
        XCTAssertEqual(ChatClientNative.availabilityName(.unavailable(.appleIntelligenceNotEnabled)),
                       AppleIntelligenceModelAvailabilityNative.appleIntelligenceNotEnabled)
        XCTAssertEqual(ChatClientNative.availabilityName(.unavailable(.modelNotReady)),
                       AppleIntelligenceModelAvailabilityNative.modelNotReady)
        XCTAssertEqual(ChatClientNative.getModelAvailability(),
                       ChatClientNative.availabilityName(SystemLanguageModel.default.availability))
    }

    func testCapabilitiesMatchTheCurrentSystemModel() {
        if #available(macOS 27.0, *) {
            let native = SystemLanguageModel.default.capabilities
            let flags = AppleIntelligenceModelCapabilitiesNative(
                rawValue: ChatClientNative.getModelCapabilities()!.uint64Value)
            XCTAssertEqual(flags.contains(.vision), native.contains(.vision))
            XCTAssertEqual(flags.contains(.guidedGeneration), native.contains(.guidedGeneration))
            XCTAssertEqual(flags.contains(.reasoning), native.contains(.reasoning))
            XCTAssertEqual(flags.contains(.toolCalling), native.contains(.toolCalling))
        } else {
            XCTAssertNil(ChatClientNative.getModelCapabilities())
        }
    }

    func testBlankNativeTurnsRetainTranscriptEntries() throws {
        let client = ChatClientNative()
        for role in [ChatRoleNative.user, .assistant, .system] {
            for contents in [[], [TextContentNative(text: "")]] {
                let message = ChatMessageNative()
                message.role = role
                message.contents = contents
                let entries = try client.toTranscriptEntries(message: message)
                XCTAssertEqual(entries.count, 1)
                switch entries[0] {
                case .prompt: XCTAssertEqual(role, .user)
                case .response: XCTAssertEqual(role, .assistant)
                case .instructions: XCTAssertEqual(role, .system)
                default: XCTFail("Unexpected blank transcript entry")
                }
            }
        }
    }

    func testImageSupportRequiresOS27AndNativeVision() throws {
        XCTAssertThrowsError(try ChatClientNative.validateImageSupport(
            hasImages: true, supportsOS27: false, supportsVision: true))
        XCTAssertThrowsError(try ChatClientNative.validateImageSupport(
            hasImages: true, supportsOS27: true, supportsVision: false))
        try ChatClientNative.validateImageSupport(hasImages: true, supportsOS27: true, supportsVision: true)
        try ChatClientNative.validateImageSupport(hasImages: false, supportsOS27: false, supportsVision: false)
    }

    func testMalformedImageFailsNativeDecodingWithoutModelGeneration() throws {
        guard #available(macOS 27.0, *) else { return }
        let message = ChatMessageNative()
        message.role = .user
        message.contents = [ImageContentNative(data: Data([0, 1, 2]), mimeType: "image/png",
                                              orientationValue: nil, label: nil)]
        XCTAssertThrowsError(try ChatClientNative().toPrompt(message: message))
    }

    func testEmptyMessagesReportNativeErrorForBothAPIs() async {
        for streaming in [false, true] {
            let done = expectation(description: "Empty messages complete")
            let completion: (ChatResponseNative?, NSError?) -> Void = { response, error in
                XCTAssertNil(response)
                XCTAssertEqual(error?.domain, "ChatClientNative")
                XCTAssertEqual(error?.code, ChatClientError.emptyMessages.rawValue)
                done.fulfill()
            }
            let client = ChatClientNative()

            let token = streaming
                ? client.streamResponse(messages: [], options: nil, onUpdate: { _ in }, onComplete: completion)
                : client.getResponse(messages: [], options: nil, onUpdate: { _ in }, onComplete: completion)
            XCTAssertNil(token)

            await fulfillment(of: [done], timeout: 5)
        }
    }

    func testAssistantCannotBeCurrentPrompt() async {
        let message = ChatMessageNative()
        message.role = .assistant
        message.contents = [TextContentNative(text: "Hello")]

        let done = expectation(description: "Invalid role completes")
        let token = ChatClientNative().getResponse(
            messages: [message], options: nil, onUpdate: { _ in },
            onComplete: { response, error in
                XCTAssertNil(response)
                XCTAssertEqual(error?.domain, "ChatClientNative")
                XCTAssertEqual(error?.code, ChatClientError.invalidRole.rawValue)
                done.fulfill()
            })

        await fulfillment(of: [done], timeout: 5)
        withExtendedLifetime(token) {}
    }

    func testLiveStructuredResponseAndStreamPreserveRequiredFields() async throws {
        guard try LiveTests.requireModel() else { return }
        let prompt = "Return a greeting of hello and a count of 68."
        let schema = """
        {"type":"object","title":"Greeting","properties":{
          "greeting":{"type":"string"},"count":{"type":"integer"}
        },"required":["greeting","count"]}
        """

        for streaming in [false, true] {
            let run = try await LiveTests.runClient(prompt: prompt, schema: schema, streaming: streaming, maxTokens: 128)
            XCTAssertNil(run.error)

            let value = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(run.finalText.utf8)) as? [String: Any])
            XCTAssertEqual((value["greeting"] as? String)?.lowercased(), "hello")
            XCTAssertEqual(value["count"] as? Int, 68)

            if streaming {
                // Content updates are native JSON deltas, not cumulative snapshots.
                XCTAssertFalse(run.contentUpdates.contains(""))
                XCTAssertGreaterThanOrEqual(run.contentUpdates.count, 3)
                XCTAssertEqual(try canonicalJSON(run.contentUpdates.joined()), try canonicalJSON(run.finalText))
            } else {
                XCTAssertTrue(run.contentUpdates.isEmpty)
            }
        }
    }
}
