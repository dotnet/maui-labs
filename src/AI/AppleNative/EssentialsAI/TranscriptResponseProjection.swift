import FoundationModels

/// Projects cumulative content from the response entry that owns it, not Snapshot.content.
struct TranscriptResponseProjection {
    struct Content {
        let messageId: String
        let text: String
    }

    private var lastContentById: [String: String] = [:]

    mutating func project(_ entries: ArraySlice<Transcript.Entry>) -> [Content] {
        var updates: [Content] = []
        for entry in entries {
            guard case .response(let response) = entry else { continue }
            let text = response.segments.compactMap { segment -> String? in
                switch segment {
                case .text(let value): return value.content
                case .structure(let value): return value.content.jsonString
                default: return nil
                }
            }.joined()

            guard lastContentById[response.id] != text else { continue }
            lastContentById[response.id] = text
            if !text.isEmpty {
                updates.append(Content(messageId: response.id, text: text))
            }
        }
        return updates
    }
}
