import Foundation
import FoundationModels

/// Maps the managed Apple JSON Schema profile, not arbitrary caller JSON Schema.
class JsonSchemaDecoder {
    struct SchemaError: Error, LocalizedError {
        let path: String
        let reason: String
        var errorDescription: String? { "Invalid Apple JSON schema at \(path): \(reason)" }
    }

    private final class Schema: Decodable {
        let type: String?
        let title: String?
        let description: String?
        let properties: [String: Schema]?
        let required: [String]?
        let additionalProperties: Bool?
        let items: Schema?
        let enumValues: [String]?
        let constant: String?
        let integerMinimum: Int?
        let integerMaximum: Int?
        let numberMinimum: Double?
        let numberMaximum: Double?
        let minItems: Int?
        let maxItems: Int?
        let anyOf: [Schema]?
        let path: String

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
            func decode<T: Decodable>(_ type: T.Type, _ key: String) throws -> T? {
                do {
                    let codingKey = Key(stringValue: key)!
                    return container.contains(codingKey) ? try container.decode(type, forKey: codingKey) : nil
                } catch let error as DecodingError {
                    throw SchemaError(path: JsonSchemaDecoder.member(JsonSchemaDecoder.path(decoder.codingPath), key),
                                      reason: error.localizedDescription)
                }
            }
            type = try decode(String.self, "type")
            title = try decode(String.self, "title")
            description = try decode(String.self, "description")
            anyOf = try decode([Schema].self, "anyOf")
            var allowed: Set<String> = ["type", "description"]
            if anyOf != nil {
                allowed = ["title", "description", "anyOf"]
            } else {
                switch type {
                case "object": allowed.formUnion(["title", "properties", "required", "additionalProperties"])
                case "array": allowed.formUnion(["items", "minItems", "maxItems"])
                case "string": allowed.formUnion(["enum", "const"])
                case "integer", "number": allowed.formUnion(["minimum", "maximum"])
                case "boolean", "null": break
                default: throw SchemaError(path: "\(path).type", reason: "Expected a canonical single type or anyOf.")
                }
            }
            if let unexpected = keys.subtracting(allowed).sorted().first {
                throw SchemaError(path: JsonSchemaDecoder.member(path, unexpected),
                                  reason: "Unexpected canonical field '\(unexpected)'; compile the original schema in managed code.")
            }
            properties = try decode([String: Schema].self, "properties")
            required = try decode([String].self, "required")
            additionalProperties = try decode(Bool.self, "additionalProperties")
            items = try decode(Schema.self, "items")
            enumValues = try decode([String].self, "enum")
            constant = try decode(String.self, "const")
            integerMinimum = type == "integer" ? try decode(Int.self, "minimum") : nil
            integerMaximum = type == "integer" ? try decode(Int.self, "maximum") : nil
            numberMinimum = type == "number" ? try decode(Double.self, "minimum") : nil
            numberMaximum = type == "number" ? try decode(Double.self, "maximum") : nil
            minItems = try decode(Int.self, "minItems")
            maxItems = try decode(Int.self, "maxItems")
        }
    }

    /// The optional return type is retained for existing callers; invalid profiles always throw.
    static func parse(_ json: String) throws -> GenerationSchema? {
        do {
            let schema = try JSONDecoder().decode(Schema.self, from: Data(json.utf8))
            return try GenerationSchema(root: map(schema), dependencies: [])
        } catch let error as SchemaError {
            throw error
        } catch let error as DecodingError {
            switch error {
            case .typeMismatch(_, let context), .valueNotFound(_, let context),
                 .keyNotFound(_, let context), .dataCorrupted(let context):
                throw SchemaError(path: path(context.codingPath), reason: context.debugDescription)
            @unknown default: throw SchemaError(path: "$", reason: error.localizedDescription)
            }
        } catch {
            throw SchemaError(path: "$", reason: error.localizedDescription)
        }
    }

    private static func member(_ path: String, _ key: String) -> String {
        let ascii = Array(key.utf8)
        func letter(_ c: UInt8) -> Bool { (65...90).contains(c) || (97...122).contains(c) || c == 95 }
        if let first = ascii.first, letter(first), ascii.allSatisfy({ letter($0) || (48...57).contains($0) }) {
            return "\(path).\(key)"
        }
        let escaped = String(decoding: try! JSONEncoder().encode(key), as: UTF8.self)
        return "\(path)[\(escaped)]"
    }

    private static func path(_ keys: [CodingKey]) -> String {
        keys.reduce("$") { path, key in key.intValue.map { "\(path)[\($0)]" } ?? member(path, key.stringValue) }
    }

    private static func name(_ schema: Schema) throws -> String {
        guard let title = schema.title, !title.isEmpty else {
            throw SchemaError(path: "\(schema.path).title", reason: "Canonical objects and unions require a managed identity title.")
        }
        return title
    }

    private static func map(_ schema: Schema) throws -> DynamicGenerationSchema {
        if let branches = schema.anyOf {
            guard branches.count >= 2 else {
                throw SchemaError(path: "\(schema.path).anyOf", reason: "Canonical unions require at least two branches.")
            }
            return try .init(name: name(schema), description: schema.description, anyOf: branches.map(map))
        }
        switch schema.type {
        case "object":
            guard let properties = schema.properties, let required = schema.required, schema.additionalProperties == false,
                  Set(required).count == required.count, required.allSatisfy({ properties[$0] != nil }) else {
                throw SchemaError(path: schema.path, reason: "Canonical objects require properties, required and additionalProperties: false.")
            }
            let mapped = try properties.sorted { $0.key.utf16.lexicographicallyPrecedes($1.key.utf16) }.map { key, value in
                DynamicGenerationSchema.Property(name: key, description: value.description,
                                                  schema: try map(value), isOptional: !required.contains(key))
            }
            return try .init(name: name(schema), description: schema.description, properties: mapped)
        case "array":
            guard let items = schema.items else {
                throw SchemaError(path: "\(schema.path).items", reason: "Canonical arrays require items.")
            }
            guard (schema.minItems ?? 0) >= 0, (schema.maxItems ?? Int.max) >= (schema.minItems ?? 0) else {
                throw SchemaError(path: schema.path, reason: "Invalid canonical array bounds.")
            }
            return try .init(arrayOf: map(items), minimumElements: schema.minItems, maximumElements: schema.maxItems)
        case "string":
            guard !(schema.enumValues != nil && schema.constant != nil),
                  schema.enumValues?.isEmpty != true else {
                throw SchemaError(path: schema.path, reason: "Canonical string guides must be nonempty and mutually exclusive.")
            }
            if let constant = schema.constant { return .init(type: String.self, guides: [.constant(constant)]) }
            if let values = schema.enumValues { return .init(type: String.self, guides: [.anyOf(values)]) }
            return .init(type: String.self)
        case "integer":
            let minimum = schema.integerMinimum
            let maximum = schema.integerMaximum
            guard !(minimum != nil && maximum != nil && minimum! > maximum!) else {
                throw SchemaError(path: schema.path, reason: "Invalid canonical integer bounds.")
            }
            let guides: [GenerationGuide<Int>]
            switch (minimum, maximum) {
            case (.some(let a), .some(let b)): guides = [.range(a...b)]
            case (.some(let a), nil): guides = [.minimum(a)]
            case (nil, .some(let b)): guides = [.maximum(b)]
            default: guides = []
            }
            return try verifyBounds(.init(type: Int.self, guides: guides),
                                    minimum: minimum.map(String.init), maximum: maximum.map(String.init), path: schema.path)
        case "number":
            let minimum = schema.numberMinimum
            let maximum = schema.numberMaximum
            guard minimum?.isFinite != false, maximum?.isFinite != false,
                  !(minimum != nil && maximum != nil && minimum! > maximum!) else {
                throw SchemaError(path: schema.path, reason: "Invalid canonical number bounds.")
            }
            let guides: [GenerationGuide<Double>]
            switch (minimum, maximum) {
            case (.some(let a), .some(let b)): guides = [.range(a...b)]
            case (.some(let a), nil): guides = [.minimum(a)]
            case (nil, .some(let b)): guides = [.maximum(b)]
            default: guides = []
            }
            return try verifyBounds(.init(type: Double.self, guides: guides),
                                    minimum: minimum.map { String($0) }, maximum: maximum.map { String($0) }, path: schema.path)
        case "boolean": return .init(type: Bool.self)
        case "null":
            guard #available(iOS 26.4, macOS 26.4, visionOS 26.4, *) else {
                throw SchemaError(path: schema.path, reason: "Explicit null schemas require OS 26.4 or later.")
            }
            return .null
        default: throw SchemaError(path: "\(schema.path).type", reason: "Expected a canonical type.")
        }
    }

    private static func verifyBounds(
        _ schema: DynamicGenerationSchema, minimum: String?, maximum: String?, path: String
    ) throws -> DynamicGenerationSchema {
        guard minimum != nil || maximum != nil else { return schema }
        let native = try GenerationSchema(root: schema, dependencies: [])
        let encoded = String(decoding: try JSONEncoder().encode(native), as: UTF8.self)
        // This is only JSONEncoder's native numeric root (no descriptions, children or caller
        // JSON). Inspect its encoded bounds, not decoded Double/Decimal values: the latter hide
        // rounding near 2^53, and Decimal cannot represent the full finite Double exponent range.
        let bounds = try NSRegularExpression(pattern: #""(minimum|maximum)"\s*:\s*(-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)"#)
        var actual: [String: String] = [:]
        for match in bounds.matches(in: encoded, range: NSRange(encoded.startIndex..., in: encoded)) {
            let key = String(encoded[Range(match.range(at: 1), in: encoded)!])
            actual[key] = String(encoded[Range(match.range(at: 2), in: encoded)!])
        }
        for (key, expected) in [("minimum", minimum), ("maximum", maximum)] {
            guard expected.map(decimalIdentity) == actual[key].map(decimalIdentity) else {
                throw SchemaError(path: "\(path).\(key)",
                                  reason: "The native schema cannot preserve this numeric bound exactly; use a representable bound.")
            }
        }
        return schema
    }

    /// Equality of already-encoded native numeric tokens, without a fixed precision/exponent ceiling.
    private static func decimalIdentity(_ token: String) -> String {
        let parts = token.lowercased().split(separator: "e")
        let negative = parts[0].first == "-"
        let mantissa = negative ? parts[0].dropFirst() : parts[0][...]
        let fraction = mantissa.firstIndex(of: ".").map { mantissa.distance(from: mantissa.index(after: $0), to: mantissa.endIndex) } ?? 0
        var digits = mantissa.filter { $0 != "." }.drop(while: { $0 == "0" })
        if digits.isEmpty { return "0" }
        var exponent = (parts.count == 2 ? Int(parts[1])! : 0) - fraction
        while digits.last == "0" { digits = digits.dropLast(); exponent += 1 }
        return (negative ? "-" : "") + digits + "e\(exponent)"
    }
}
