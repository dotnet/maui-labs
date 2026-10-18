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
