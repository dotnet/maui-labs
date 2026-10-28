import Foundation
import FoundationModels

/// Converts the cumulative `GeneratedContent` snapshots of one structured response into
/// append-only JSON text deltas.
///
/// Foundation Models reports completion per node (`isComplete`), so the adapter never has to
/// infer whether a string may still grow or a number is final. State is scoped to one response
/// and must only be mutated from the task that consumes the stream.
struct StructuredJsonDeltas {

    enum Failure: Error, Equatable, LocalizedError {
        case revisedPrefix
        case nonFiniteNumber
        case incompleteFinal
        case unsupportedKind

        var errorDescription: String? {
            switch self {
            case .revisedPrefix:
                return "The model revised structured JSON that was already streamed."
            case .nonFiniteNumber:
                return "The model produced a non-finite number, which JSON cannot represent."
            case .incompleteFinal:
                return "The final structured response was not marked complete."
            case .unsupportedKind:
                return "The model produced an unsupported structured content kind."
            }
        }
    }

    // Object keys and array indexes must stay distinct so a key such as "0" cannot alias an index.
    private enum Step: Hashable {
        case key(String)
        case index(Int)
    }

    private(set) var emitted: [UInt8] = []
    private var keyOrders: [[Step]: [String]] = [:]

    /// Returns the JSON text that extends everything emitted so far. `final` requires the root to be complete.
    mutating func process(_ content: GeneratedContent, final: Bool = false) throws -> String {
        var orders = keyOrders
        let (prefix, complete) = try Self.render(content, path: [], orders: &orders)
        guard !final || complete else { throw Failure.incompleteFinal }

        let bytes = Array(prefix.utf8)
        guard bytes.starts(with: emitted) else { throw Failure.revisedPrefix }

        let delta = String(decoding: bytes[emitted.count...], as: UTF8.self)
        emitted = bytes
        keyOrders = orders
        return delta
    }

    // Renders the longest JSON prefix that can no longer change. Rendering stops at the first
    // incomplete child because JSON text can only be appended to at a single position.
    private static func render(
        _ content: GeneratedContent, path: [Step], orders: inout [[Step]: [String]]
    ) throws -> (text: String, complete: Bool) {
        let complete = content.isComplete

        switch content.kind {
        case .string(let value):
            return ("\"" + escape(value) + (complete ? "\"" : ""), complete)

        case .number(let value):
            guard value.isFinite else { throw Failure.nonFiniteNumber }
            // Partial numbers are valid JSON numbers that may still grow (-1 -> -12 -> -12.75).
            return (complete ? content.jsonString : "", complete)

        case .bool, .null:
            return (complete ? content.jsonString : "", complete)

        case .array(let elements):
            var text = "["
            for (index, element) in elements.enumerated() {
                if index > 0 { text += "," }
                let child = try render(element, path: path + [.index(index)], orders: &orders)
                text += child.text
                if !child.complete { return (text, false) }
            }
            return (text + (complete ? "]" : ""), complete)

        case .structure(let properties, _):
            // orderedKeys is not stable across snapshots, so the first-seen order is retained.
            // Keys first seen together are ordered completed-first, then lexically.
            var keys = orders[path] ?? []
            guard keys.allSatisfy({ properties[$0] != nil }) else { throw Failure.revisedPrefix }
            let known = Set(keys)
            keys += properties.keys.filter { !known.contains($0) }.sorted {
                let left = properties[$0]!.isComplete, right = properties[$1]!.isComplete
                return left != right ? left : $0 < $1
            }
            orders[path] = keys

            var text = "{"
            for (index, key) in keys.enumerated() {
                if index > 0 { text += "," }
                text += "\"" + escape(key) + "\":"
                let child = try render(properties[key]!, path: path + [.key(key)], orders: &orders)
                text += child.text
                if !child.complete { return (text, false) }
            }
            return (text + (complete ? "}" : ""), complete)

        @unknown default:
            throw Failure.unsupportedKind
        }
    }

    static func escape(_ string: String) -> String {
        var result = ""
        for scalar in string.unicodeScalars {
            switch scalar.value {
            case 0x22: result += "\\\""
            case 0x5c: result += "\\\\"
            case 0..<0x20:
                let hex = String(scalar.value, radix: 16)
                result += "\\u" + String(repeating: "0", count: 4 - hex.count) + hex
            default: result.unicodeScalars.append(scalar)
            }
        }
        return result
    }
}
