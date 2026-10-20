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

    mutating func process(_ snapshot: String) throws -> String {
        guard snapshot.hasPrefix(emitted) else {
            throw Failure.revisedPrefix
        }

        let delta = String(snapshot.dropFirst(emitted.count))
        emitted = snapshot
        return delta
    }
}
