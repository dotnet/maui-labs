import Foundation
import FoundationModels

class JsonSchemaDecoder {
    struct SchemaError: Error, LocalizedError {
        let path: String
        let reason: String
        fileprivate let isImpossible: Bool

        init(path: String, reason: String, isImpossible: Bool = false) {
            self.path = path
            self.reason = reason
            self.isImpossible = isImpossible
        }

        var errorDescription: String? {
            "Invalid JSON schema at \(path): \(reason)"
        }
    }

    private struct NumericTokens {
        private enum Scope { case schema, properties, schemas, bound, ignored }
        private let bytes: [UInt8]
        private var index = 0

        init(_ json: String) { bytes = Array(json.utf8) }

        mutating func validate() throws {
            try value(path: "$", scope: .schema)
        }

        private mutating func whitespace() {
            while index < bytes.count && [9, 10, 13, 32].contains(bytes[index]) { index += 1 }
        }

        private mutating func string() throws -> String {
            let start = index
            index += 1
            while index < bytes.count {
                let byte = bytes[index]
                index += 1
                if byte == 92 { index += 1 }
                else if byte == 34 {
                    return try JSONDecoder().decode(String.self, from: Data(bytes[start..<index]))
                }
            }
            throw SchemaError(path: "$", reason: "Unterminated JSON string.")
        }

        // JSONDecoder validates syntax first. This walk retains numeric lexemes and schema paths,
        // without treating text in descriptions, defaults, or escaped strings as constraints.
        private mutating func value(path: String, scope: Scope) throws {
            whitespace()
            guard index < bytes.count else { throw SchemaError(path: path, reason: "Missing JSON value.") }
            switch bytes[index] {
            case 123:
                index += 1
                whitespace()
                if bytes[index] == 125 { index += 1; return }
                while index < bytes.count {
                    let key = try string()
                    whitespace()
                    index += 1
                    let child: Scope
                    switch (scope, key) {
                    case (.schema, "minimum"), (.schema, "maximum"),
                         (.schema, "minItems"), (.schema, "maxItems"): child = .bound
                    case (.schema, "properties"): child = .properties
                    case (.schema, "items"): child = .schema
                    case (.schema, "anyOf"): child = .schemas
                    case (.properties, _): child = .schema
                    default: child = .ignored
                    }
                    try value(path: "\(path).\(key)", scope: child)
                    whitespace()
                    let end = bytes[index] == 125
                    index += 1
                    if end { return }
                    whitespace()
                }
            case 91:
                index += 1
                whitespace()
                if bytes[index] == 93 { index += 1; return }
                var offset = 0
                while index < bytes.count {
                    try value(path: "\(path)[\(offset)]", scope: scope == .schemas ? .schema : .ignored)
                    offset += 1
                    whitespace()
                    let end = bytes[index] == 93
                    index += 1
                    if end { return }
                }
            case 34: _ = try string()
            case 116, 110: index += 4
            case 102: index += 5
            default:
                let start = index
                while index < bytes.count && ![9, 10, 13, 32, 44, 93, 125].contains(bytes[index]) { index += 1 }
                if scope == .bound {
                    let token = String(decoding: bytes[start..<index], as: UTF8.self)
                    let decimal = try JSONDecoder().decode(Decimal.self, from: Data(token.utf8))
                    let roundTrip = NSDecimalNumber(decimal: decimal).stringValue
                    guard let exact = Self.canonical(token), exact == Self.canonical(roundTrip) else {
                        throw SchemaError(path: path, reason: "Cannot preserve the exact numeric token before native mapping; use a bound representable without rounding.")
                    }
                }
            }
        }

        private static func canonical(_ token: String) -> String? {
            let parts = token.lowercased().split(separator: "e", omittingEmptySubsequences: false)
            guard parts.count <= 2, let exponent = parts.count == 2 ? Int(parts[1]) : 0 else { return nil }
            let negative = parts[0].first == "-"
            let mantissa = negative ? parts[0].dropFirst() : parts[0][...]
            let fraction = mantissa.firstIndex(of: ".").map { mantissa.distance(from: mantissa.index(after: $0), to: mantissa.endIndex) } ?? 0
            let digits = Array(mantissa.utf8.filter { $0 != 46 })
            guard let first = digits.firstIndex(where: { $0 != 48 }) else { return "0" }
            let last = digits.lastIndex(where: { $0 != 48 })!
            let (scale, overflow) = exponent.subtractingReportingOverflow(fraction)
            let (normalized, overflow2) = scale.addingReportingOverflow(digits.count - last - 1)
            guard !overflow, !overflow2 else { return nil }
            return (negative ? "-" : "") + String(decoding: digits[first...last], as: UTF8.self) + "e\(normalized)"
        }
    }

    private final class JsonSchema: Decodable {
        let types: [String]?
        let title: String?
        let description: String?
        let properties: [String: JsonSchema]?
        let required: [String]?
        let items: JsonSchema?
        let enumValues: [String?]?
        let constant: StringLiteral?
        let pattern: String?
        let minimum: Decimal?
        let maximum: Decimal?
        let minItems: Int?
        let maxItems: Int?
        let anyOf: [JsonSchema]?
        let constraintKeys: Set<String>
        let path: String

        struct StringLiteral: Decodable {
            let value: String?

            init(from decoder: Decoder) throws {
                let container = try decoder.singleValueContainer()
                if container.decodeNil() {
                    value = nil
                } else if let string = try? container.decode(String.self) {
                    value = string
                } else {
                    throw SchemaError(path: JsonSchemaDecoder.path(decoder.codingPath),
                                      reason: "Only string or null enum/const values are supported; Foundation Models has no numeric, boolean, array, or object constant guide.")
                }
            }
        }

        private struct Key: CodingKey {
            let stringValue: String
            let intValue: Int? = nil
            init?(stringValue: String) { self.stringValue = stringValue }
            init?(intValue: Int) { return nil }
        }

        init(from decoder: Decoder) throws {
            path = JsonSchemaDecoder.path(decoder.codingPath)
            let container = try decoder.container(keyedBy: Key.self)
            let keys = Set(container.allKeys.map(\.stringValue))
            let supported: Set<String> = [
                "type", "title", "description", "properties", "required", "items",
                "additionalProperties", "enum", "const", "pattern", "minimum", "maximum",
                "minItems", "maxItems", "anyOf",
                "$schema", "$id", "$comment", "$defs", "definitions", "default", "examples", "format"
            ]
            if let unsupported = keys.subtracting(supported).sorted().first {
                throw SchemaError(path: "\(path).\(unsupported)",
                                  reason: "'\(unsupported)' is not supported by Foundation Models; inline references and use representable types or simple anyOf branches.")
            }
            constraintKeys = keys.subtracting([
                "$schema", "$id", "$comment", "$defs", "definitions", "title", "description", "default", "examples", "format"
            ])
            if keys.contains("additionalProperties"),
               (try? container.decode(Bool.self, forKey: Key(stringValue: "additionalProperties")!)) != false {
                throw SchemaError(path: "\(path).additionalProperties",
                                  reason: "Dictionaries and open-ended additional properties are not supported; use an object with named properties and additionalProperties: false.")
            }
            func decode<T: Decodable>(_ type: T.Type, _ key: String) throws -> T? {
                let key = Key(stringValue: key)!
                return container.contains(key) ? try container.decode(type, forKey: key) : nil
            }
            if keys.contains("type") {
                let key = Key(stringValue: "type")!
                if let type = try? container.decode(String.self, forKey: key) {
                    types = [type]
                } else {
                    types = try container.decode([String].self, forKey: key)
                }
                guard let types, !types.isEmpty, Set(types).count == types.count else {
                    throw SchemaError(path: "\(path).type", reason: "Specify a type string or a non-empty array of distinct type strings.")
                }
            } else {
                types = nil
            }
            title = try decode(String.self, "title")
            description = try decode(String.self, "description")
            properties = try decode([String: JsonSchema].self, "properties")
            required = try decode([String].self, "required")
            items = try decode(JsonSchema.self, "items")
            enumValues = try decode([StringLiteral].self, "enum")?.map(\.value)
            constant = try decode(StringLiteral.self, "const")
            pattern = try decode(String.self, "pattern")
            minimum = try decode(Decimal.self, "minimum")
            maximum = try decode(Decimal.self, "maximum")
            minItems = try decode(Int.self, "minItems")
            maxItems = try decode(Int.self, "maxItems")
            anyOf = try decode([JsonSchema].self, "anyOf")

            if anyOf != nil {
                let siblings = constraintKeys.subtracting(["anyOf"])
                guard siblings.isEmpty else {
                    throw SchemaError(path: path, reason: "anyOf combined with sibling constraints is not supported; put each complete schema inside its branch.")
                }
            }
        }
    }

    private final class SchemaNames {
        private let titleCounts: [String: Int]
        private var used: Set<String> = []

        init(_ root: JsonSchema) {
            var counts: [String: Int] = [:]
            func visit(_ schema: JsonSchema) {
                if let title = schema.title { counts[title, default: 0] += 1 }
                schema.properties?.values.forEach(visit)
                if let items = schema.items { visit(items) }
                schema.anyOf?.forEach(visit)
            }
            visit(root)
            titleCounts = counts
        }

        func make(for schema: JsonSchema, suffix: String = "") -> String {
            let title = schema.title.flatMap { titleCounts[$0] == 1 ? $0 : nil }
            let preferred = (title ?? (schema.path == "$" ? "Object" : schema.path)) + suffix
            var name = preferred
            var collision = 0
            // Native schemas use names as definition identities, not just display titles.
            while used.contains(name) || (titleCounts[name] == 1 && name != title) {
                collision += 1
                name = "\(schema.path)\(suffix)_\(collision)"
            }
            used.insert(name)
            return name
        }
    }

    /// The optional return type is retained for existing callers; invalid schemas always throw.
    static func parse(_ jsonString: String) throws -> GenerationSchema? {
        do {
            let schema = try JSONDecoder().decode(JsonSchema.self, from: Data(jsonString.utf8))
            var tokens = NumericTokens(jsonString)
            try tokens.validate()
            return try GenerationSchema(root: toDynamicSchema(schema, names: SchemaNames(schema)), dependencies: [])
        } catch let error as SchemaError {
            throw error
        } catch let error as DecodingError {
            switch error {
            case .typeMismatch(_, let context), .valueNotFound(_, let context),
                 .keyNotFound(_, let context), .dataCorrupted(let context):
                throw SchemaError(path: path(context.codingPath), reason: context.debugDescription)
            @unknown default:
                throw SchemaError(path: "$", reason: error.localizedDescription)
            }
        } catch {
            throw SchemaError(path: "$", reason: error.localizedDescription)
        }
    }

    private static func path(_ codingPath: [CodingKey]) -> String {
        codingPath.reduce("$") { path, key in
            key.intValue.map { "\(path)[\($0)]" } ?? "\(path).\(key.stringValue)"
        }
    }

    private static func toDynamicSchema(_ schema: JsonSchema, names: SchemaNames) throws -> DynamicGenerationSchema {
        if let branches = schema.anyOf {
            guard !branches.isEmpty else {
                throw SchemaError(path: "\(schema.path).anyOf", reason: "anyOf requires at least one representable branch.")
            }
            var choices: [DynamicGenerationSchema] = []
            for branch in branches {
                do { choices.append(try toDynamicSchema(branch, names: names)) }
                catch let error as SchemaError where error.isImpossible { continue }
            }
            guard !choices.isEmpty else {
                throw SchemaError(path: schema.path, reason: "No anyOf branch permits a value.", isImpossible: true)
            }
            if choices.count == 1 { return choices[0] }
            return DynamicGenerationSchema(name: names.make(for: schema), description: schema.description, anyOf: choices)
        }

        var literals = schema.enumValues
        if let constant = schema.constant {
            literals = literals.map { $0.filter { sameLiteral($0, constant.value) } } ?? [constant.value]
        }
        if let literals, literals.isEmpty {
            throw SchemaError(path: schema.path, reason: "The enum/const constraints have no common values.", isImpossible: true)
        }
        let literalTypes = literals.map { values in
            (values.contains(where: { $0 != nil }) ? ["string"] : []) + (values.contains(nil) ? ["null"] : [])
        }
        // Literal values imply a type; type-specific constraints such as minimum do not.
        // Retain {} for no-argument tools, but never infer a type from other constraints.
        guard let types = schema.types ?? literalTypes ??
                (schema.path == "$" && schema.constraintKeys.isEmpty ? ["object"] : nil) else {
            let constraint = schema.constraintKeys.sorted().first
            throw SchemaError(path: constraint.map { "\(schema.path).\($0)" } ?? schema.path,
                              reason: "Specify an explicit type or a simple anyOf union; type-specific constraints alone do not restrict the JSON value's type.")
        }
        if (schema.properties != nil || schema.required != nil) && !types.contains("object") {
            throw SchemaError(path: schema.path, reason: "properties and required need an object type.")
        }
        if (schema.items != nil || schema.minItems != nil || schema.maxItems != nil) && !types.contains("array") {
            throw SchemaError(path: schema.path, reason: "items and item limits need an array type.")
        }
        if schema.pattern != nil && !types.contains("string") {
            throw SchemaError(path: "\(schema.path).pattern", reason: "A pattern requires an explicit string type.")
        }
        if (schema.minimum != nil || schema.maximum != nil) && !types.contains(where: { $0 == "integer" || $0 == "number" }) {
            throw SchemaError(path: "\(schema.path).\(schema.minimum != nil ? "minimum" : "maximum")",
                              reason: "Numeric bounds require an explicit integer or number type.")
        }

        var choices: [DynamicGenerationSchema] = []
        var impossibleReason: String?
        for type in types {
            if literals != nil && ["object", "array", "integer", "number", "boolean"].contains(type) { continue }
            do {
                switch type {
                case "object":
                    let properties = schema.properties ?? [:]
                    if let missing = schema.required?.first(where: { properties[$0] == nil }) {
                        throw SchemaError(path: "\(schema.path).required", reason: "Required property '\(missing)' has no schema; define every required property.")
                    }
                    let props = try properties.sorted(by: { $0.key < $1.key }).compactMap { name, value -> DynamicGenerationSchema.Property? in
                        do {
                            return DynamicGenerationSchema.Property(
                                name: name, description: value.description, schema: try toDynamicSchema(value, names: names),
                                isOptional: schema.required?.contains(name) != true)
                        } catch let error as SchemaError where error.isImpossible && schema.required?.contains(name) != true {
                            return nil
                        }
                    }
                    choices.append(DynamicGenerationSchema(name: names.make(for: schema), description: schema.description, properties: props))
                case "array":
                    if let maximum = schema.maxItems, maximum < (schema.minItems ?? 0), maximum >= 0 {
                        impossibleReason = "Array item limits must be non-negative and minItems must not exceed maxItems."
                        continue
                    }
                    guard let items = schema.items else {
                        throw SchemaError(path: "\(schema.path).items", reason: "Array schema missing 'items'; specify a representable item schema.")
                    }
                    guard (schema.minItems ?? 0) >= 0, (schema.maxItems ?? Int.max) >= (schema.minItems ?? 0) else {
                        throw SchemaError(path: schema.path, reason: "Array item limits must be non-negative and minItems must not exceed maxItems.")
                    }
                    let itemSchema: DynamicGenerationSchema
                    do { itemSchema = try toDynamicSchema(items, names: names) }
                    catch let error as SchemaError where error.isImpossible && (schema.minItems ?? 0) == 0 {
                        throw SchemaError(path: items.path, reason: "An impossible item schema permitting only an empty array is not supported; specify maxItems: 0 with a representable item schema.")
                    }
                    choices.append(DynamicGenerationSchema(arrayOf: itemSchema,
                                                           minimumElements: schema.minItems, maximumElements: schema.maxItems))
                case "string":
                    let regex = try schema.pattern.map { try nativePattern($0, path: "\(schema.path).pattern") }
                    if regex != nil && literals != nil {
                        throw SchemaError(path: "\(schema.path).pattern",
                                          reason: "Compound pattern intersections with enum/const are not supported; supply exactly filtered choices without a pattern.")
                    }
                    if let values = literals {
                        let strings = values.compactMap { $0 }
                        if !strings.isEmpty {
                            choices.append(DynamicGenerationSchema(type: String.self, guides:
                                schema.constant != nil ? [.constant(strings[0])] : [.anyOf(strings)]))
                        }
                    } else {
                        choices.append(DynamicGenerationSchema(type: String.self, guides: regex.map { [.pattern($0)] } ?? []))
                    }
                case "integer":
                    let lower = schema.minimum.map { roundedIntegerBound($0, minimum: true) }
                    let upper = schema.maximum.map { roundedIntegerBound($0, minimum: false) }
                    if let lower, let upper, lower > upper {
                        impossibleReason = "No integer satisfies the minimum and maximum bounds."
                        continue
                    }
                    let minimum = try lower.map { try integerBound($0, path: "\(schema.path).minimum") }
                    let maximum = try upper.map { try integerBound($0, path: "\(schema.path).maximum") }
                    let guides: [GenerationGuide<Int>]
                    switch (minimum, maximum) {
                    case (.some(let minimum), .some(let maximum)): guides = [.range(minimum...maximum)]
                    case (.some(let minimum), nil): guides = [.minimum(minimum)]
                    case (nil, .some(let maximum)): guides = [.maximum(maximum)]
                    default: guides = []
                    }
                    choices.append(try verifyBounds(
                        .init(type: Int.self, guides: guides),
                        minimum: minimum.map { Decimal($0) }, maximum: maximum.map { Decimal($0) }, path: schema.path))
                case "number":
                    if let minimum = schema.minimum, let maximum = schema.maximum, minimum > maximum {
                        impossibleReason = "minimum must not exceed maximum."
                        continue
                    }
                    if schema.minimum != nil || schema.maximum != nil {
                        let minimum = try schema.minimum.map { try numberBound($0, path: "\(schema.path).minimum") }
                        let maximum = try schema.maximum.map { try numberBound($0, path: "\(schema.path).maximum") }
                        let guides: [GenerationGuide<Double>]
                        switch (minimum, maximum) {
                        case (.some(let minimum), .some(let maximum)): guides = [.range(minimum...maximum)]
                        case (.some(let minimum), nil): guides = [.minimum(minimum)]
                        case (nil, .some(let maximum)): guides = [.maximum(maximum)]
                        default: guides = []
                        }
                        choices.append(try verifyBounds(
                            .init(type: Double.self, guides: guides),
                            minimum: schema.minimum, maximum: schema.maximum, path: schema.path))
                    } else {
                        choices.append(DynamicGenerationSchema(type: Double.self))
                    }
                case "boolean": choices.append(DynamicGenerationSchema(type: Bool.self))
                case "null":
                    if literals?.contains(nil) != false {
                        guard #available(iOS 26.4, macOS 26.4, visionOS 26.4, *) else {
                            throw SchemaError(path: schema.path, reason: "Explicit null schemas require OS 26.4 or later.")
                        }
                        choices.append(.null)
                    }
                default:
                    throw SchemaError(path: "\(schema.path).type", reason: "Unsupported JSON schema type '\(type)'; use object, array, string, integer, number, boolean, or null.")
                }
            } catch let error as SchemaError where error.isImpossible {
                impossibleReason = error.reason
            }
        }
        guard !choices.isEmpty else {
            throw SchemaError(path: schema.path,
                              reason: impossibleReason ?? "The enum/const constraints contain no values allowed by the declared type.",
                              isImpossible: true)
        }
        if choices.count == 1 { return choices[0] }
        // Nullability belongs to the value schema; only the parent's required list controls omission.
        return DynamicGenerationSchema(name: names.make(for: schema, suffix: "_Union"), description: schema.description, anyOf: choices)
    }

    private static func sameLiteral(_ left: String?, _ right: String?) -> Bool {
        switch (left, right) {
        case (nil, nil): return true
        case (.some(let left), .some(let right)): return left.utf8.elementsEqual(right.utf8)
        default: return false
        }
    }

    private static func nativePattern(_ pattern: String, path: String) throws -> Regex<AnyRegexOutput> {
        let bytes = Array(pattern.utf8)
        let error = SchemaError(path: path, reason:
            "Pattern is outside the supported ASCII regex subset: alphanumeric/space/_/- literals, positive ASCII classes, concatenation, optional ^/$ anchors, and greedy ?, *, +, {n}, {n,m}, {n,} quantifiers. Escapes, non-ASCII literals, dot, negated classes, groups, alternation, lookarounds, flags and lazy/possessive quantifiers are not supported.")
        func literal(_ byte: UInt8) -> Bool {
            (48...57).contains(byte) || (65...90).contains(byte) || (97...122).contains(byte) || byte == 32 || byte == 95
        }
        var index = bytes.first == 94 ? 1 : 0
        let end = bytes.last == 36 ? bytes.count - 1 : bytes.count
        func count() throws -> Int {
            let start = index
            while index < end && (48...57).contains(bytes[index]) { index += 1 }
            guard index > start, index - start == 1 || bytes[start] != 48,
                  let value = Int(String(decoding: bytes[start..<index], as: UTF8.self)) else { throw error }
            return value
        }
        while index < end {
            if bytes[index] == 91 {
                index += 1
                let start = index
                guard let close = bytes[index..<end].firstIndex(of: 93), close > start else { throw error }
                while index < close {
                    let byte = bytes[index]
                    if byte == 45 && (index == start || index == close - 1) { index += 1; continue }
                    guard literal(byte) else { throw error }
                    if index + 2 < close && bytes[index + 1] == 45 {
                        guard literal(bytes[index + 2]), byte <= bytes[index + 2] else { throw error }
                        index += 3
                    } else {
                        index += 1
                    }
                }
                index += 1
            } else {
                guard literal(bytes[index]) || bytes[index] == 45 else { throw error }
                index += 1
            }
            if index < end && [42, 43, 63].contains(bytes[index]) { index += 1 }
            else if index < end && bytes[index] == 123 {
                index += 1
                let minimum = try count()
                if index < end && bytes[index] == 44 {
                    index += 1
                    if index < end && bytes[index] != 125 {
                        guard try count() >= minimum else { throw error }
                    }
                }
                guard index < end && bytes[index] == 125 else { throw error }
                index += 1
            }
        }
        // Positive ASCII atoms have the same code-point language in ECMAScript and scalar-mode
        // Swift Regex. Encode scalar mode in the native pattern, not merely in local matching.
        do { return try Regex<AnyRegexOutput>(pattern).matchingSemantics(.unicodeScalar) }
        catch { throw error }
    }

    private static func roundedIntegerBound(_ value: Decimal, minimum: Bool) -> Decimal {
        var value = value
        var rounded = Decimal()
        NSDecimalRound(&rounded, &value, 0, minimum ? .up : .down)
        return rounded
    }

    private static func integerBound(_ value: Decimal, path: String) throws -> Int {
        guard let bound = Int(NSDecimalNumber(decimal: value).stringValue) else {
            throw SchemaError(path: path, reason: "The bound cannot be represented by the native Int schema.")
        }
        return bound
    }

    private static func numberBound(_ value: Decimal, path: String) throws -> Double {
        guard let bound = Double(NSDecimalNumber(decimal: value).stringValue), bound.isFinite else {
            throw SchemaError(path: path, reason: "The bound cannot be represented by the native number schema.")
        }
        return bound
    }

    private static func verifyBounds(
        _ schema: DynamicGenerationSchema, minimum: Decimal?, maximum: Decimal?, path: String
    ) throws -> DynamicGenerationSchema {
        guard minimum != nil || maximum != nil else { return schema }
        struct NativeBounds: Decodable {
            let minimum: Decimal?
            let maximum: Decimal?
        }
        // Native numeric guides may round even Decimal/Int bounds; never advertise a changed constraint.
        let native = try GenerationSchema(root: schema, dependencies: [])
        let encoded = try JSONEncoder().encode(native)
        let bounds = try JSONDecoder().decode(NativeBounds.self, from: encoded)
        for (keyword, expected, actual) in [("minimum", minimum, bounds.minimum), ("maximum", maximum, bounds.maximum)] {
            if expected != actual {
                throw SchemaError(path: "\(path).\(keyword)",
                                  reason: "The native schema cannot preserve this numeric bound exactly; use a representable bound.")
            }
        }
        return schema
    }
}
