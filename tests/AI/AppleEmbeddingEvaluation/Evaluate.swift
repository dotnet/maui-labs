import CryptoKit
import Foundation
import NaturalLanguage

// Native-only evaluation: no MAUI dependency, network requests, or downloaded models.
struct Record: Decodable {
    let id: String
    let text: String
}
struct Query: Decodable {
    let id: String
    let group: String
    let text: String
    let relevant: [String]
}
struct LongRecord: Decodable {
    let id: String
    let position: String
    let lead: Int
    let tail: Int
    let needle: String
}
struct Corpus: Decodable {
    let schemaVersion: Int
    let documents: [Record]
    let queries: [Query]
    let longDocuments: [LongRecord]
    let longQueries: [Query]
    let longFiller: String
}
enum EvaluationError: Error, CustomStringConvertible {
    case message(String)
    var description: String {
        switch self { case .message(let message): return message }
    }
}

func normalized(_ text: String) -> String {
    let range = NSRange(location: 0, length: (text as NSString).length)
    return try! NSRegularExpression(pattern: "\\s+")
        .stringByReplacingMatches(in: text, range: range, withTemplate: " ")
        .trimmingCharacters(in: .whitespacesAndNewlines)
}

// Port of playground #516's 360 UTF-16 code-unit policy; parameterized size.
// The original splits at the last ASCII space after the midpoint; no overlap.
func fixedChunks(_ text: String, limit: Int) -> [String] {
    let source = normalized(text) as NSString
    var offset = 0
    var chunks: [String] = []
    while offset < source.length {
        let count = min(limit, source.length - offset)
        var end = offset + count
        if end < source.length {
            let slice = source.substring(with: NSRange(location: offset, length: count)) as NSString
            let space = slice.range(of: " ", options: .backwards).location
            if space != NSNotFound && space > count / 2 {
                end = offset + space
            }
        }
        let chunk = source.substring(with: NSRange(location: offset, length: end - offset))
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if !chunk.isEmpty { chunks.append(chunk) }
        offset = end
        while offset < source.length && source.character(at: offset) == 32 { offset += 1 }
    }
    return chunks
}

func sentenceChunks(_ text: String, groupSize: Int) -> [String] {
    let text = normalized(text)
    let tokenizer = NLTokenizer(unit: .sentence)
    tokenizer.string = text
    var sentences: [String] = []
    tokenizer.enumerateTokens(in: text.startIndex..<text.endIndex) { range, _ in
        let sentence = String(text[range]).trimmingCharacters(in: .whitespacesAndNewlines)
        if !sentence.isEmpty { sentences.append(sentence) }
        return true
    }
    if sentences.isEmpty { return text.isEmpty ? [] : [text] }
    if groupSize == 1 { return sentences }
    var chunks: [String] = []
    var offset = 0
    while offset < sentences.count {
        chunks.append(sentences[offset..<min(offset + groupSize, sentences.count)].joined(separator: " "))
        if offset + groupSize >= sentences.count { break }
        offset += groupSize - 1 // one sentence of overlap
    }
    return chunks
}

func validVector(_ value: [Double]?, dimension: Int) -> [Double]? {
    guard let value, value.count == dimension,
          value.allSatisfy({ $0.isFinite }) else { return nil }
    let squaredNorm = value.reduce(0, { $0 + $1 * $1 })
    guard squaredNorm.isFinite, squaredNorm > 0 else { return nil }
    return value
}

func vectorStatus(_ value: [Double]?, dimension: Int?) -> String {
    guard let value else { return "nil" }
    guard value.count == dimension else { return "invalid_dimension" }
    guard value.allSatisfy({ $0.isFinite }) else { return "nonfinite" }
    let squaredNorm = value.reduce(0, { $0 + $1 * $1 })
    guard squaredNorm.isFinite else { return "nonfinite_norm" }
    guard squaredNorm > 0 else { return "zero" }
    return "valid"
}

func cosine(_ a: [Double], _ b: [Double]) -> Double {
    var dot = 0.0
    var aa = 0.0
    var bb = 0.0
    for i in a.indices {
        dot += a[i] * b[i]
        aa += a[i] * a[i]
        bb += b[i] * b[i]
    }
    return dot / sqrt(aa * bb)
}

func modelInfo(_ embedding: NLEmbedding?) -> [String: Any] {
    guard let embedding else { return ["available": false] }
    return ["available": true, "dimension": embedding.dimension,
            "revision": embedding.revision, "language": embedding.language?.rawValue as Any? ?? NSNull()]
}

func retrieval(_ records: [Record], _ queries: [Query], model: NLEmbedding,
               strategy: String, corpusName: String) throws -> [String: Any] {
    var vectors: [String: [[Double]]] = [:]
    var counts: [String: Int] = [:]
    var invalidChunks: [String: Int] = [:]
    for record in records {
        let pieces: [String]
        switch strategy {
        case "whole": pieces = [normalized(record.text)]
        case "fixed180": pieces = fixedChunks(record.text, limit: 180)
        case "playground360": pieces = fixedChunks(record.text, limit: 360)
        case "fixed720": pieces = fixedChunks(record.text, limit: 720)
        case "sentence1": pieces = sentenceChunks(record.text, groupSize: 1)
        case "sentence3_overlap1": pieces = sentenceChunks(record.text, groupSize: 3)
        default: throw EvaluationError.message("Unknown strategy \(strategy)")
        }
        counts[record.id] = pieces.count
        let values = pieces.compactMap { validVector(model.vector(for: $0), dimension: model.dimension) }
        vectors[record.id] = values
        invalidChunks[record.id] = pieces.count - values.count
    }
    var rows: [[String: Any]] = []
    for query in queries {
        guard let queryVector = validVector(model.vector(for: normalized(query.text)),
                                            dimension: model.dimension) else {
            throw EvaluationError.message("Query \(query.id) returned nil/invalid sentence vector")
        }
        let ranked: [(String, Double)] = records.compactMap { record in
            guard let values = vectors[record.id], !values.isEmpty else { return nil }
            return (record.id, values.map { cosine(queryVector, $0) }.max()!)
        }.sorted { $0.1 == $1.1 ? $0.0 < $1.0 : $0.1 > $1.1 }
        let relevant = Set(query.relevant)
        let rank = ranked.firstIndex(where: { relevant.contains($0.0) }).map { $0 + 1 }
        rows.append([
            "id": query.id, "group": query.group, "text": query.text,
            "relevant": query.relevant, "rank": rank as Any? ?? NSNull(),
            "rankings": ranked.map { ["id": $0.0, "score": $0.1] as [String: Any] }
        ])
    }
    let groups = Dictionary(grouping: rows, by: { $0["group"] as! String })
    let metrics: [String: [String: Any]] = groups.mapValues { group in
        let ranks = group.map { $0["rank"] as? Int }
        let n = Double(group.count)
        return ["n": group.count,
                "recall1": Double(ranks.filter { $0 == 1 }.count) / n,
                "recall3": Double(ranks.filter { ($0 ?? Int.max) <= 3 }.count) / n,
                "mrr": ranks.reduce(0.0) { $0 + ($1.map { 1.0 / Double($0) } ?? 0) } / n]
    }
    return ["corpus": corpusName, "strategy": strategy, "metrics": metrics,
            "chunksPerDocument": counts, "invalidChunksPerDocument": invalidChunks, "queries": rows]
}

func vectorProbe(_ model: NLEmbedding?, _ texts: [String]) -> [[String: Any]] {
    texts.map { text in
        let raw = model?.vector(for: text)
        let vector = model.flatMap { validVector(raw, dimension: $0.dimension) }
        return ["input": text, "utf16Length": (text as NSString).length,
                "status": model == nil ? "model_unavailable" : vectorStatus(raw, dimension: model?.dimension),
                "dimension": vector?.count as Any? ?? NSNull(),
                "norm": vector.map { sqrt($0.reduce(0, { $0 + $1 * $1 })) } as Any? ?? NSNull()]
    }
}

func lengthProbe(_ model: NLEmbedding) -> [[String: Any]] {
    [16, 32, 64, 128, 256, 512].flatMap { count -> [[String: Any]] in
        ["suffix", "prefix"].map { placement in
            let shared = Array(repeating: "garden", count: count).joined(separator: " ")
            let a = placement == "suffix" ? shared + " comet" : "comet " + shared
            let b = placement == "suffix" ? shared + " vineyard" : "vineyard " + shared
            let va = validVector(model.vector(for: a), dimension: model.dimension)
            let vb = validVector(model.vector(for: b), dimension: model.dimension)
            var result: [String: Any] = [
                "sharedWordCount": count, "placement": placement,
                "utf16LengthA": (a as NSString).length, "utf16LengthB": (b as NSString).length,
                "status": va == nil || vb == nil ? "nil_or_invalid" : "valid"
            ]
            if let va, let vb {
                result["exactlyEqual"] = va == vb
                result["l2Distance"] = sqrt(zip(va, vb).reduce(0.0) { $0 + pow($1.0 - $1.1, 2) })
                result["cosine"] = cosine(va, vb)
            }
            return result
        }
    }
}

func validate(_ corpus: Corpus) throws {
    guard corpus.schemaVersion == 1 else { throw EvaluationError.message("Unsupported corpus schema") }
    guard corpus.longDocuments.allSatisfy({ $0.lead >= 0 && $0.tail >= 0 && $0.lead + $0.tail == 70 }) else {
        throw EvaluationError.message("Position controls require 70 nonnegative filler sentences per document")
    }
    for (documents, queries) in [(corpus.documents, corpus.queries),
                                  (corpus.longDocuments.map { Record(id: $0.id, text: $0.needle) },
                                   corpus.longQueries)] {
        let ids = documents.map(\.id)
        guard Set(ids).count == ids.count, Set(queries.map(\.id)).count == queries.count,
              !queries.isEmpty, queries.allSatisfy({ !$0.relevant.isEmpty
                  && Set($0.relevant).isSubset(of: Set(ids)) }) else {
            throw EvaluationError.message("Duplicate IDs or invalid predeclared relevance labels")
        }
    }
}

func compare(_ current: [String: Any], baseline: [String: Any], tolerance: Double) throws {
    let oldMetadata = baseline["metadata"] as? [String: Any]
    let newMetadata = current["metadata"] as? [String: Any]
    for key in ["protocolVersion", "corpusSHA256", "corpusSchemaVersion", "modelKind", "language",
                "revision", "dimension", "preprocessing"] {
        guard let old = oldMetadata?[key], let new = newMetadata?[key],
              String(describing: old) == String(describing: new) else {
            throw EvaluationError.message("Incompatible baseline metadata: \(key)")
        }
    }
    guard let oldRuns = baseline["runs"] as? [[String: Any]],
          let newRuns = current["runs"] as? [[String: Any]],
          oldRuns.count == newRuns.count else {
        throw EvaluationError.message("Incompatible run strategies")
    }
    var regressions: [String] = []
    for (old, new) in zip(oldRuns, newRuns) {
        for key in ["corpus", "strategy"] {
            guard old[key] as? String == new[key] as? String else {
                throw EvaluationError.message("Incompatible run: \(key)")
            }
        }
        guard let oldGroups = old["metrics"] as? [String: [String: Any]],
              let newGroups = new["metrics"] as? [String: [String: Any]],
              Set(oldGroups.keys) == Set(newGroups.keys) else {
            throw EvaluationError.message("Incompatible metric groups")
        }
        for group in oldGroups.keys.sorted() {
            guard oldGroups[group]?["n"] as? Int == newGroups[group]?["n"] as? Int else {
                throw EvaluationError.message("Incompatible query counts for \(group)")
            }
            for metric in ["recall1", "recall3", "mrr"] {
                guard let a = oldGroups[group]?[metric] as? Double,
                      let b = newGroups[group]?[metric] as? Double,
                      a.isFinite, b.isFinite, (0...1).contains(a), (0...1).contains(b) else {
                    throw EvaluationError.message("Missing or invalid metric \(group)/\(metric)")
                }
                if a - b > tolerance + 1e-12 {
                    regressions.append("\(new["corpus"]!)/\(new["strategy"]!)/\(group)/\(metric): \(a) -> \(b)")
                }
            }
        }
    }
    guard regressions.isEmpty else {
        throw EvaluationError.message("Measured regression (absolute tolerance \(tolerance)):\n"
                                      + regressions.joined(separator: "\n"))
    }
}

func run() throws {
    let args = CommandLine.arguments
    guard args.count >= 3, args[1] == "--output" else {
        throw EvaluationError.message("Usage: Evaluate --output results.json [--compare baseline.json] [--tolerance 0.01]")
    }
    let output = URL(fileURLWithPath: args[2])
    var baselineURL: URL?
    var tolerance = 0.01
    var index = 3
    while index < args.count {
        guard index + 1 < args.count else { throw EvaluationError.message("Missing option value") }
        switch args[index] {
        case "--compare": baselineURL = URL(fileURLWithPath: args[index + 1])
        case "--tolerance":
            guard let number = Double(args[index + 1]), number >= 0, number <= 1 else {
                throw EvaluationError.message("Tolerance must be an absolute fraction in [0,1]")
            }
            tolerance = number
        default: throw EvaluationError.message("Unknown option \(args[index])")
        }
        index += 2
    }
    if baselineURL?.standardizedFileURL == output.standardizedFileURL {
        throw EvaluationError.message("Output must not overwrite the comparison baseline")
    }
    let corpusURL = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        .appendingPathComponent("corpus.json")
    let bytes = try Data(contentsOf: corpusURL)
    let corpus = try JSONDecoder().decode(Corpus.self, from: bytes)
    try validate(corpus)
    let sha = SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
    let languages = ["en", "fr", "de", "es", "it", "pt", "ja",
                     NLLanguage.simplifiedChinese.rawValue, NLLanguage.traditionalChinese.rawValue,
                     "ko", "ar", "ru", "hi"]
    let availability = languages.map { code -> [String: Any] in
        let language = NLLanguage(rawValue: code)
        return ["language": code, "sentence": modelInfo(NLEmbedding.sentenceEmbedding(for: language)),
                "word": modelInfo(NLEmbedding.wordEmbedding(for: language))]
    }
    guard let sentence = NLEmbedding.sentenceEmbedding(for: .english) else {
        throw EvaluationError.message("English sentence embedding unavailable; no retrieval result produced. Availability: \(availability)")
    }
    guard sentence.language == .english else {
        throw EvaluationError.message("English sentence embedding reported unexpected language")
    }
    let word = NLEmbedding.wordEmbedding(for: .english)
    func longRecords(targetLead: Int? = nil) -> [Record] {
        corpus.longDocuments.map { record in
            let lead = record.position == "distractor" ? record.lead : targetLead ?? record.lead
            return Record(id: record.id, text: Array(repeating: corpus.longFiller, count: lead)
                .joined(separator: " ") + " " + record.needle + " " +
                Array(repeating: corpus.longFiller, count: 70 - lead).joined(separator: " "))
        }
    }
    let matchedQueries = corpus.longQueries.map {
        Query(id: $0.id, group: "same_six_queries", text: $0.text, relevant: $0.relevant)
    }
    let strategies = ["whole", "fixed180", "playground360", "fixed720",
                      "sentence1", "sentence3_overlap1"]
    var runs: [[String: Any]] = []
    for strategy in strategies {
        runs.append(try retrieval(corpus.documents, corpus.queries, model: sentence,
                                  strategy: strategy, corpusName: "short"))
        runs.append(try retrieval(longRecords(), corpus.longQueries, model: sentence,
                                  strategy: strategy, corpusName: "controlled_long"))
        for (position, lead) in [("early", 0), ("middle", 35), ("late", 70)] {
            runs.append(try retrieval(longRecords(targetLead: lead), matchedQueries, model: sentence,
                                      strategy: strategy, corpusName: "matched_" + position))
        }
    }
    let inputs = ["coffee", "coffees", "AES-256", "HTTP 429", "unknownblorfquizzlet",
                  "the museum archive", "", " ", "   \n  ", "!!!", "😀", "Coffee", "coffee coffee"]
    let crossLanguage: [[String: Any]] = ["en", "de", "it"].map { code in
        let examples: [String: String] = ["en": "A bicycle needs a new tire.",
                                          "de": "Ein Fahrrad braucht einen neuen Reifen.",
                                          "it": "Una bicicletta ha bisogno di un nuovo pneumatico."]
        let model = NLEmbedding.sentenceEmbedding(for: NLLanguage(rawValue: code))
        return ["language": code, "model": modelInfo(model),
                "inputs": ["en", "de", "it"].map { inputCode -> [String: Any] in
                    let text = examples[inputCode]!
                    let vector = model.flatMap {
                        validVector($0.vector(for: text), dimension: $0.dimension)
                    }
                    let reference = model.flatMap {
                        validVector($0.vector(for: examples[code]!), dimension: $0.dimension)
                    }
                    return ["inputLanguage": inputCode, "matched": code == inputCode,
                            "status": model == nil ? "model_unavailable"
                                : vectorStatus(model?.vector(for: text), dimension: model?.dimension),
                            "cosineToMatchedText": vector.flatMap { v in reference.map { cosine(v, $0) } }
                                as Any? ?? NSNull()]
                }]
    }
    #if arch(arm64)
    let architecture = "arm64"
    #elseif arch(x86_64)
    let architecture = "x86_64"
    #else
    let architecture = "other"
    #endif
    let metadata: [String: Any] = [
        "protocolVersion": 2,
        "corpusSchemaVersion": corpus.schemaVersion, "corpusSHA256": sha,
        "modelKind": "NaturalLanguage.NLEmbedding.sentenceEmbedding",
        "language": "en", "revision": sentence.revision,
        "dimension": sentence.dimension,
        "preprocessing": "regex-whitespace-collapse-trim-v1",
        "os": "macOS", "osVersionAndBuild": ProcessInfo.processInfo.operatingSystemVersionString,
        "architecture": architecture
    ]
    let results: [String: Any] = [
        "metadata": metadata, "availability": availability,
        "wordModel": modelInfo(word), "runs": runs, "languageControl": crossLanguage,
        "inputProbes": ["sentence": vectorProbe(sentence, inputs),
                        "word": vectorProbe(word, inputs)],
        "lengthSensitivity": lengthProbe(sentence),
        "provenance": "synthetic corpus; playground policy from #516 / 1d58ce63d57e9454755e1e67cf5a1218aecda03a"
    ]
    let data = try JSONSerialization.data(withJSONObject: results, options: [.prettyPrinted, .sortedKeys])
    try data.write(to: output, options: .atomic)
    if let baselineURL {
        guard let baseline = try JSONSerialization.jsonObject(with: Data(contentsOf: baselineURL)) as? [String: Any] else {
            throw EvaluationError.message("Comparison baseline must be a JSON object")
        }
        try compare(results, baseline: baseline, tolerance: tolerance)
    }
    print("Wrote \(output.path); corpus SHA256 \(sha); \(runs.count) corpus/strategy runs")
}

do {
    try run()
} catch {
    fputs("Apple embedding evaluation: \(error)\n", stderr)
    exit(2)
}
