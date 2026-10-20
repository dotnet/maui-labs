import Foundation
import FoundationModels

class JsonSchemaDecoder {
    struct SchemaError: Error, LocalizedError {
        let path: String
        let reason: String

        var errorDescription: String? {
            "Invalid JSON schema at \(path): \(reason)"
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
        let minItems: Int?
        let maxItems: Int?
        let anyOf: [JsonSchema]?
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
            let supported: Set<String> = [
                "type", "title", "description", "properties", "required", "items",
                "additionalProperties", "enum", "minItems", "maxItems", "anyOf",
                "$schema", "$id", "$comment", "$defs", "definitions", "default", "examples"
            ]
            if let unsupported = keys.subtracting(supported).sorted().first {
                throw SchemaError(path: "\(path).\(unsupported)",
                                  reason: "'\(unsupported)' is not supported by Foundation Models; inline references and use representable types or simple anyOf branches.")
            }
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
            enumValues = try decode([String?].self, "enum")
            minItems = try decode(Int.self, "minItems")
            maxItems = try decode(Int.self, "maxItems")
            anyOf = try decode([JsonSchema].self, "anyOf")

            if anyOf != nil {
                let siblings = keys.intersection([
                    "type", "properties", "required", "items", "additionalProperties", "enum", "minItems", "maxItems"
                ])
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
            return DynamicGenerationSchema(name: names.make(for: schema), description: schema.description,
                                           anyOf: try branches.map { try toDynamicSchema($0, names: names) })
        }

        // Preserve the no-argument tool schema ({}) while rejecting unconstrained nested values.
        guard let types = schema.types ?? (schema.properties != nil || schema.path == "$" ? ["object"] : nil) else {
            throw SchemaError(path: schema.path, reason: "Specify a type or a simple anyOf union.")
        }
        if schema.enumValues != nil && !Set(types).isSubset(of: ["string", "null"]) {
            throw SchemaError(path: "\(schema.path).enum", reason: "Only string enums (with optional null) are supported.")
        }
        if (schema.properties != nil || schema.required != nil) && !types.contains("object") {
            throw SchemaError(path: schema.path, reason: "properties and required need an object type.")
        }
        if (schema.items != nil || schema.minItems != nil || schema.maxItems != nil) && !types.contains("array") {
            throw SchemaError(path: schema.path, reason: "items and item limits need an array type.")
        }

        var choices: [DynamicGenerationSchema] = []
        for type in types {
            switch type {
            case "object":
                let properties = schema.properties ?? [:]
                if let missing = schema.required?.first(where: { properties[$0] == nil }) {
                    throw SchemaError(path: "\(schema.path).required", reason: "Required property '\(missing)' has no schema; define every required property.")
                }
                let props = try properties.sorted(by: { $0.key < $1.key }).map { name, value in
                    DynamicGenerationSchema.Property(
                        name: name, description: value.description, schema: try toDynamicSchema(value, names: names),
                        isOptional: schema.required?.contains(name) != true)
                }
                choices.append(DynamicGenerationSchema(name: names.make(for: schema), description: schema.description, properties: props))
            case "array":
                guard let items = schema.items else {
                    throw SchemaError(path: "\(schema.path).items", reason: "Array schema missing 'items'; specify a representable item schema.")
                }
                guard (schema.minItems ?? 0) >= 0, (schema.maxItems ?? Int.max) >= (schema.minItems ?? 0) else {
                    throw SchemaError(path: schema.path, reason: "Array item limits must be non-negative and minItems must not exceed maxItems.")
                }
                choices.append(DynamicGenerationSchema(arrayOf: try toDynamicSchema(items, names: names),
                                                       minimumElements: schema.minItems, maximumElements: schema.maxItems))
            case "string":
                if let values = schema.enumValues {
                    let strings = values.compactMap { $0 }
                    if !strings.isEmpty {
                        choices.append(DynamicGenerationSchema(type: String.self, guides: [.anyOf(strings)]))
                    }
                } else {
                    choices.append(DynamicGenerationSchema(type: String.self))
                }
            case "integer": choices.append(DynamicGenerationSchema(type: Int.self))
            case "number": choices.append(DynamicGenerationSchema(type: Double.self))
            case "boolean": choices.append(DynamicGenerationSchema(type: Bool.self))
            case "null":
                if schema.enumValues?.contains(nil) != false {
                    guard #available(iOS 26.4, macOS 26.4, visionOS 26.4, *) else {
                        throw SchemaError(path: schema.path, reason: "Explicit null schemas require OS 26.4 or later.")
                    }
                    choices.append(.null)
                }
            default:
                throw SchemaError(path: "\(schema.path).type", reason: "Unsupported JSON schema type '\(type)'; use object, array, string, integer, number, boolean, or null.")
            }
        }
        guard !choices.isEmpty else {
            throw SchemaError(path: schema.path, reason: "The enum contains no values allowed by the declared type.")
        }
        if choices.count == 1 { return choices[0] }
        // Nullability belongs to the value schema; only the parent's required list controls omission.
        return DynamicGenerationSchema(name: names.make(for: schema, suffix: "_Union"), description: schema.description, anyOf: choices)
    }
}
