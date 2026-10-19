import XCTest
@testable import EssentialsAI

final class PlainTextDeltasTests: XCTestCase {
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
