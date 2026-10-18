import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

final class ChatClientTests: XCTestCase {
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
        try XCTSkipUnless(ProcessInfo.processInfo.environment["ESSENTIALSAI_LIVE_TESTS"] == "1",
                          "Set ESSENTIALSAI_LIVE_TESTS=1 to run on-device model tests.")
        guard case .available = SystemLanguageModel.default.availability else {
            return XCTFail("Live tests were requested, but Apple Intelligence is unavailable.")
        }

        for streaming in [false, true] {
            let message = ChatMessageNative()
            message.contents = [TextContentNative(text: "Return a greeting of hello and a count of 68.")]
            let options = ChatOptionsNative()
            options.temperature = 0
            options.maxOutputTokens = 128
            options.responseJsonSchema = """
            {"type":"object","title":"Greeting","properties":{
              "greeting":{"type":"string"},"count":{"type":"integer"}
            },"required":["greeting","count"]}
            """ as NSString

            let done = expectation(description: "Native structured generation completes")
            let contentReceived = streaming ? expectation(description: "Native stream emits content") : nil
            contentReceived?.assertForOverFulfill = false
            let snapshotLock = NSLock()
            var lastSnapshot: String?

            let onUpdate: (ResponseUpdateNative) -> Void = { update in
                if update.updateType == .content {
                    XCTAssertFalse(update.text?.isEmpty ?? true)
                    snapshotLock.withLock { lastSnapshot = update.text }
                    contentReceived?.fulfill()
                }
            }

            let completion: (ChatResponseNative?, NSError?) -> Void = { response, error in
                XCTAssertNil(error)
                let text = response?.messages.flatMap(\.contents)
                    .compactMap { ($0 as? TextContentNative)?.text }.joined() ?? ""
                do {
                    let value = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
                    XCTAssertEqual((value["greeting"] as? String)?.lowercased(), "hello")
                    XCTAssertEqual(value["count"] as? Int, 68)

                    if streaming {
                        let streamedText = try XCTUnwrap(snapshotLock.withLock { lastSnapshot })
                        let streamed = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(streamedText.utf8)) as? NSDictionary)
                        XCTAssertEqual(streamed, value as NSDictionary)
                    }
                } catch {
                    XCTFail("Invalid structured response: \(text); \(error)")
                }
                done.fulfill()
            }

            let client = ChatClientNative()
            let token = streaming
                ? client.streamResponse(messages: [message], options: options, onUpdate: onUpdate, onComplete: completion)
                : client.getResponse(messages: [message], options: options, onUpdate: { _ in }, onComplete: completion)

            await fulfillment(of: [done] + (contentReceived.map { [$0] } ?? []), timeout: 120)
            token?.cancel()
        }
    }
}
