import Foundation

/// Converts cumulative Foundation Models text snapshots into append-only deltas.
struct PlainTextDeltas {
    enum Failure: Error, Equatable, LocalizedError {
        case revisedPrefix

        var errorDescription: String? {
            switch self {
            case .revisedPrefix:
                return "The model revised text that was already emitted."
            }
        }
    }

    private(set) var emitted = ""

    mutating func reset() {
        emitted = ""
    }

    mutating func process(_ snapshot: String) throws -> String {
        guard snapshot.utf8.starts(with: emitted.utf8) else {
            throw Failure.revisedPrefix
        }

        let delta = String(decoding: snapshot.utf8.dropFirst(emitted.utf8.count), as: UTF8.self)
        emitted = snapshot
        return delta
    }
}

// Tool callbacks and streamed snapshots can arrive on different executors.
// Serialize state transitions with their events so a tool call starts the next text epoch.
final class PlainTextDeltaEmitter: @unchecked Sendable {
    private let lock = NSLock()
    private var deltas = PlainTextDeltas()

    func process(_ snapshot: String, emit: (String) -> Void) throws {
        try lock.withLock {
            let delta = try deltas.process(snapshot)
            if !delta.isEmpty { emit(delta) }
        }
    }

    func toolCall(emit: () -> Void) {
        lock.withLock {
            deltas.reset()
            emit()
        }
    }
}
