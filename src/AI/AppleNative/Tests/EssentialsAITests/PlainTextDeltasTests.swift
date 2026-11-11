import XCTest
@testable import EssentialsAI

final class PlainTextDeltasTests: XCTestCase {
    func testGrowingGraphemeClustersPreserveExactUTF8() throws {
        let sequences = [
            ["e", "e\u{301}", "e\u{301}x"],
            ["\u{1F44D}", "\u{1F44D}\u{1F3FD}", "\u{1F44D}\u{1F3FD}\u{200D}\u{1F4BB}"],
            ["\u{1F1FA}", "\u{1F1FA}\u{1F1F8}", "\u{1F1FA}\u{1F1F8}!"],
            ["\u{915}", "\u{915}\u{94D}", "\u{915}\u{94D}\u{200D}\u{937}", "\u{915}\u{94D}\u{200D}\u{937}\u{93F}"]
        ]
        for snapshots in sequences {
            var deltas = PlainTextDeltas()
            var output = ""
            for snapshot in snapshots {
                output += try deltas.process(snapshot)
                XCTAssertEqual(Array(output.utf8), Array(snapshot.utf8))
            }
        }
    }

    func testCanonicallyEquivalentReplacementStillRevisesEmittedBytes() throws {
        var deltas = PlainTextDeltas()
        _ = try deltas.process("e\u{301}")
        XCTAssertThrowsError(try deltas.process("\u{E9}")) {
            XCTAssertEqual($0 as? PlainTextDeltas.Failure, .revisedPrefix)
        }
    }

    func testToolResetStartsFreshTextEpoch() throws {
        var deltas = PlainTextDeltas()
        XCTAssertEqual(try deltas.process("Checking"), "Checking")
        XCTAssertEqual(try deltas.process("Checking now"), " now")
        deltas.reset()
        XCTAssertEqual(try deltas.process("The answer"), "The answer")
        XCTAssertEqual(try deltas.process("The answer is 42"), " is 42")
    }

    func testResetIsIdempotent() throws {
        var deltas = PlainTextDeltas()
        deltas.reset()
        deltas.reset()
        XCTAssertEqual(try deltas.process("Before"), "Before")
        deltas.reset()
        deltas.reset()
        XCTAssertEqual(deltas.emitted, "")
        XCTAssertEqual(try deltas.process("After"), "After")
    }

    func testMultipleToolBoundariesPreserveEmissionOrder() throws {
        let emitter = PlainTextDeltaEmitter()
        var events: [String] = []
        let emit: (String) -> Void = { events.append("text:\($0)") }
        try emitter.process("First", emit: emit)
        try emitter.process("First step", emit: emit)
        emitter.toolCall { events.append("tool:first") }
        try emitter.process("Second", emit: emit)
        try emitter.process("Second step", emit: emit)
        emitter.toolCall { events.append("tool:second") }
        try emitter.process("Final", emit: emit)
        try emitter.process("Final answer", emit: emit)
        XCTAssertEqual(events, [
            "text:First", "text: step", "tool:first",
            "text:Second", "text: step", "tool:second",
            "text:Final", "text: answer"
        ])
    }

    func testProgressiveSnapshotsEmitOnlySuffixes() throws {
        var deltas = PlainTextDeltas()

        XCTAssertEqual(try deltas.process("Hello"), "Hello")
        XCTAssertEqual(try deltas.process("Hello"), "")
        XCTAssertEqual(try deltas.process("Hello world"), " world")
        XCTAssertEqual(try deltas.process("Hello world!"), "!")
        XCTAssertEqual(deltas.emitted, "Hello world!")
    }

    func testEmptyAndWhitespaceSnapshots() throws {
        var deltas = PlainTextDeltas()

        XCTAssertEqual(try deltas.process(""), "")
        XCTAssertEqual(try deltas.process(" "), " ")
        XCTAssertEqual(try deltas.process(" \n\t"), "\n\t")
    }

    func testUnicodeGraphemeClustersRemainIntact() throws {
        var deltas = PlainTextDeltas()

        XCTAssertEqual(try deltas.process("A😀"), "A😀")
        XCTAssertEqual(try deltas.process("A😀e\u{301}"), "e\u{301}")
        XCTAssertEqual(try deltas.process("A😀e\u{301}👨‍👩‍👧‍👦Z"), "👨‍👩‍👧‍👦Z")
    }

    func testLongSnapshotReconstructsExactly() throws {
        let words = (0..<500).map { "word\($0)" }
        var deltas = PlainTextDeltas()
        var output = ""

        for index in words.indices {
            let snapshot = words[...index].joined(separator: " ")
            output += try deltas.process(snapshot)
        }

        XCTAssertEqual(output, words.joined(separator: " "))
    }

    func testRevisedPrefixFailsWithoutChangingState() throws {
        var deltas = PlainTextDeltas()
        XCTAssertEqual(try deltas.process("Hello world"), "Hello world")

        XCTAssertThrowsError(try deltas.process("Hello there")) {
            XCTAssertEqual($0 as? PlainTextDeltas.Failure, .revisedPrefix)
        }
        XCTAssertEqual(deltas.emitted, "Hello world")
        XCTAssertEqual(try deltas.process("Hello world!"), "!")
    }

    func testShorterSnapshotFails() throws {
        var deltas = PlainTextDeltas()
        _ = try deltas.process("Hello world")

        XCTAssertThrowsError(try deltas.process("Hello")) {
            XCTAssertEqual($0 as? PlainTextDeltas.Failure, .revisedPrefix)
        }
    }
}
