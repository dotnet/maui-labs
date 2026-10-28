import Foundation
import FoundationModels
import XCTest
@testable import EssentialsAI

/// Model-free tests. Partial JSON text parsed by `GeneratedContent(json:)` reproduces the per-node
/// `isComplete` metadata that Foundation Models reports for streaming snapshots.
final class StructuredJsonDeltasTests: XCTestCase {

    func testOpenStringProgressSeparatesNestedProperties() throws {
        let title = try XCTUnwrap(openStringProgress(in: #"{"sections":[{"title":"Light"#))
        let text = try XCTUnwrap(openStringProgress(in: #"{"sections":[{"title":"Lighthouses","text":"The history"#))
        let next = try XCTUnwrap(openStringProgress(in: #"{"sections":[{"title":"Lighthouses","text":"The history of"#))

        XCTAssertEqual(title.value, "Light")
        XCTAssertEqual(text.value, "The history")
        XCTAssertNotEqual(title.offset, text.offset)
        XCTAssertEqual(text.offset, next.offset)
        XCTAssertTrue(next.value.hasPrefix(text.value))
    }

    private func process(_ snapshots: [String], final: Bool = false) throws -> (deltas: [String], text: String) {
        var adapter = StructuredJsonDeltas()
        var deltas: [String] = []
        for (index, snapshot) in snapshots.enumerated() {
            let isLast = index == snapshots.count - 1
            deltas.append(try adapter.process(try GeneratedContent(json: snapshot), final: final && isLast))
        }
        return (deltas, deltas.joined())
    }

    private func assertSameJSON(_ actual: String, _ expected: String, file: StaticString = #filePath, line: UInt = #line) throws {
        XCTAssertEqual(try canonicalJSON(actual), try canonicalJSON(expected), file: file, line: line)
    }

    // MARK: - Provisional scalars

    func testProvisionalNumbersAreWithheldUntilComplete() throws {
        let result = try process([
            #"{"amount":-1"#, #"{"amount":-12"#, #"{"amount":-12.7"#, #"{"amount":-12.75"#,
            #"{"amount":-12.75,"label":""#, #"{"amount":-12.75,"label":"ok"}"#,
        ], final: true)

        XCTAssertEqual(result.deltas, [#"{"amount":"#, "", "", "", #"-12.75,"label":""#, #"ok"}"#])
        XCTAssertEqual(result.text, #"{"amount":-12.75,"label":"ok"}"#)
    }

    func testProvisionalArrayNumbersAreWithheldUntilComplete() throws {
        let result = try process(["[1", "[1,10", "[1,10,3", "[1,10,30", "[1,10,30]"], final: true)

        XCTAssertEqual(result.deltas, ["[", "1,", "10,", "", "30]"])
        XCTAssertEqual(result.text, "[1,10,30]")
    }

    func testCompleteBooleansAndNullEmitImmediately() throws {
        let result = try process([#"{"ready":true"#, #"{"ready":true,"none":null"#, #"{"ready":true,"none":null}"#], final: true)

        XCTAssertEqual(result.deltas, [#"{"ready":true"#, #","none":null"#, "}"])
    }

    func testRootScalarsAndEmptyContainers() throws {
        XCTAssertEqual(try process(["-1", "-12.75"]).text, "")
        XCTAssertEqual(try process(["true"], final: true).text, "true")
        XCTAssertEqual(try process(["null"], final: true).text, "null")
        XCTAssertEqual(try process(["[", "[]"], final: true).deltas, ["[", "]"])
        XCTAssertEqual(try process(["{", "{}"], final: true).deltas, ["{", "}"])
        XCTAssertEqual(try process([#"{"a":[],"b":{},"c":""}"#], final: true).text, #"{"a":[],"b":{},"c":""}"#)

        var adapter = StructuredJsonDeltas()
        XCTAssertEqual(try adapter.process(GeneratedContent(kind: .number(-12.75)), final: true), "-12.75")
    }

    // MARK: - Strings

    func testLongStringsAppendManyDeltasWithoutClosingQuote() throws {
        let words = (0..<40).map { "word\($0)" }
        let full = words.joined(separator: " ")
        let prefixes = words.indices.map { index in words[...index].joined(separator: " ") }

        let root = try process(prefixes.map { "\"" + $0 } + ["\"" + full + "\""], final: true)
        XCTAssertEqual(root.deltas.filter { !$0.isEmpty }.count, 41)
        XCTAssertEqual(root.deltas.first, "\"word0")
        XCTAssertEqual(root.text, "\"" + full + "\"")

        let property = try process(prefixes.map { #"{"text":""# + $0 } + [#"{"text":""# + full + #""}"#], final: true)
        XCTAssertEqual(property.deltas.filter { !$0.isEmpty }.count, 41)
        XCTAssertFalse(property.deltas.dropLast().joined().hasSuffix("\""))
        try assertSameJSON(property.text, #"{"text":""# + full + #""}"#)

        let nested = try process(prefixes.map { #"{"sections":[{"title":"t","text":""# + $0 } + [#"{"sections":[{"title":"t","text":""# + full + #""}]}"#], final: true)
        XCTAssertGreaterThanOrEqual(nested.deltas.filter { !$0.isEmpty }.count, 41)
        try assertSameJSON(nested.text, #"{"sections":[{"title":"t","text":""# + full + #""}]}"#)

        let array = try process([#"["ab"#, #"["abc"#, #"["abc","x"#, #"["abc","xy"]"#], final: true)
        XCTAssertEqual(array.deltas, [#"["ab"#, "c", #"","x"#, #"y"]"#])
    }

    func testStringEscapingRoundTripsAllCategories() throws {
        let strings = [
            "quote\" backslash\\ slash/",
            (0..<0x20).map { String(UnicodeScalar($0)!) }.joined(),
            "emoji 😀 combining e\u{301} joined 👨‍👩‍👧‍👦 line\u{2028}para\u{2029} del\u{7f}",
        ]
        for string in strings {
            var adapter = StructuredJsonDeltas()
            let delta = try adapter.process(GeneratedContent(string), final: true)

            XCTAssertFalse(delta.unicodeScalars.contains { $0.value < 0x20 }, "Raw control characters must be escaped")
            let decoded = try JSONSerialization.jsonObject(with: Data(delta.utf8), options: .fragmentsAllowed) as? String
            XCTAssertEqual(decoded?.unicodeScalars.map(\.value), string.unicodeScalars.map(\.value))
        }
        XCTAssertEqual(StructuredJsonDeltas.escape("\u{0}\u{1f}\n"), "\\u0000\\u001f\\u000a")
    }

    func testPartialEscapedStringKeepsUnicodeAcrossDeltas() throws {
        let result = try process([#""a\"b"#, #""a\"b\n😀e"#, "\"a\\\"b\\n😀e\u{301}\u{2028}\""], final: true)

        XCTAssertEqual(try JSONSerialization.jsonObject(with: Data(result.text.utf8), options: .fragmentsAllowed) as? String,
                       "a\"b\n😀e\u{301}\u{2028}")
    }

    // MARK: - Structure and key order

    func testDuplicateSnapshotsEmitNothing() throws {
        let snapshot = #"{"a":"x"#
        XCTAssertEqual(try process([snapshot, snapshot, snapshot]).deltas, [#"{"a":"x"#, "", ""])
    }

    func testKeyReorderingRetainsEmittedPrefix() throws {
        let result = try process([
            #"{"location":"Bos"#,
            #"{"location":"Boston","condition":"su"#,
            #"{"condition":"sunny","location":"Boston""#,
            #"{"location":"Boston","condition":"sunny"}"#,
        ], final: true)

        XCTAssertTrue(result.deltas.joined().hasPrefix(#"{"location":"Bos"#))
        try assertSameJSON(result.text, #"{"location":"Boston","condition":"sunny"}"#)
    }

    func testCoalescedKeysAreCompletedFirstThenLexical() throws {
        let result = try process([
            #"{"zeta":1,"alpha":2,"mid":"par"#,
            #"{"mid":"partial","zeta":1,"alpha":2,"new":5"#,
            #"{"new":5,"alpha":2,"mid":"partial","zeta":1}"#,
        ], final: true)

        XCTAssertEqual(result.deltas[0], #"{"alpha":2,"zeta":1,"mid":"par"#)
        XCTAssertEqual(result.text, #"{"alpha":2,"zeta":1,"mid":"partial","new":5}"#)
    }

    func testSparseSnapshotsMatchEverySnapshot() throws {
        let snapshots = [
            #"{"label":"te"#, #"{"label":"test","amount":-1"#, #"{"label":"test","amount":-12.75,"values":[1"#,
            #"{"label":"test","amount":-12.75,"values":[10,20,3"#,
            #"{"label":"test","amount":-12.75,"values":[10,20,30],"details":{"text":"all"#,
            #"{"label":"test","amount":-12.75,"values":[10,20,30],"details":{"text":"all done","enabled":false}}"#,
        ]
        let every = try process(snapshots, final: true)
        let sparse = try process([snapshots[1], snapshots[4], snapshots[5]], final: true)

        XCTAssertEqual(every.text, sparse.text)
        try assertSameJSON(every.text, snapshots[5])
    }

    func testKeysWithJsonPunctuationAndNumericNamesStayDistinctFromIndexes() throws {
        let json = #"{"a,b":1,"}{":[2,{"0":"x"}],"\"q\\":"v","0":[7],"":null}"#
        let prefixes = (1..<json.count).map { String(json.prefix($0)) }
        var adapter = StructuredJsonDeltas()
        var output = ""
        for prefix in prefixes {
            guard let content = try? GeneratedContent(json: prefix) else { continue }
            output += try adapter.process(content)
        }
        output += try adapter.process(GeneratedContent(json: json), final: true)

        try assertSameJSON(output, json)
    }

    // MARK: - Failures

    func testIncompatibleRevisionsFailClosedAndLeaveStateUnchanged() throws {
        let cases: [(first: String, second: String)] = [
            (#"{"a":"x"#, #"{"a":"y"#),
            (#"{"a":"x"}"#, #"{"a":"xy"#),
            (#"{"a":1}"#, #"{"a":2}"#),
            (#"{"a":1}"#, #"{"a":"1"}"#),
            (#"{"a":1,"b":2}"#, #"{"a":1}"#),
            (#"{"a":true}"#, #"{"a":false}"#),
            (#"["abc"]"#, #"["abd"]"#),
        ]
        for (first, second) in cases {
            var adapter = StructuredJsonDeltas()
            _ = try adapter.process(GeneratedContent(json: first))
            let emitted = adapter.emitted

            XCTAssertThrowsError(try adapter.process(GeneratedContent(json: second)), "\(first) -> \(second)") {
                XCTAssertEqual($0 as? StructuredJsonDeltas.Failure, .revisedPrefix)
            }
            XCTAssertEqual(adapter.emitted, emitted)
            XCTAssertEqual(try adapter.process(GeneratedContent(json: first)), "")
        }
    }

    func testFailedRevisionDoesNotLeakLearnedKeysIntoLaterSnapshots() throws {
        var adapter = StructuredJsonDeltas()
        XCTAssertEqual(try adapter.process(GeneratedContent(json: #"{"a":"x"#)), #"{"a":"x"#)
        let emitted = adapter.emitted

        // Learns key "b" while rendering, then fails because the emitted "x" was revised to "y".
        XCTAssertThrowsError(try adapter.process(GeneratedContent(json: #"{"a":"y","b":"new"#))) {
            XCTAssertEqual($0 as? StructuredJsonDeltas.Failure, .revisedPrefix)
        }
        XCTAssertEqual(adapter.emitted, emitted)
        XCTAssertEqual(try adapter.process(GeneratedContent(json: #"{"a":"x"#)), "")
        XCTAssertEqual(adapter.emitted, emitted)

        // If "b" had been remembered it would precede "c"; first sight orders the complete "c" first.
        let next = try adapter.process(GeneratedContent(json: #"{"c":1,"a":"xy","b":"par"#))
        XCTAssertEqual(String(decoding: adapter.emitted, as: UTF8.self), #"{"a":"xy","c":1,"b":"par"#)
        XCTAssertEqual(next, #"y","c":1,"b":"par"#)
    }

    func testIncompleteFinalThrows() throws {
        for json in [#"{"a":1"#, #"{"a":"x"#, "[1", "-1"] {
            var adapter = StructuredJsonDeltas()
            XCTAssertThrowsError(try adapter.process(GeneratedContent(json: json), final: true), json) {
                XCTAssertEqual($0 as? StructuredJsonDeltas.Failure, .incompleteFinal)
            }
        }
    }

    func testNonFiniteNumbersAreRejected() throws {
        for value in [Double.nan, .infinity, -.infinity] {
            var adapter = StructuredJsonDeltas()
            XCTAssertThrowsError(try adapter.process(GeneratedContent(kind: .number(value)))) {
                XCTAssertEqual($0 as? StructuredJsonDeltas.Failure, .nonFiniteNumber)
            }

            let nested = GeneratedContent(kind: .array([GeneratedContent(1), GeneratedContent(kind: .number(value))]))
            XCTAssertThrowsError(try adapter.process(nested)) {
                XCTAssertEqual($0 as? StructuredJsonDeltas.Failure, .nonFiniteNumber)
            }
        }
    }
}
