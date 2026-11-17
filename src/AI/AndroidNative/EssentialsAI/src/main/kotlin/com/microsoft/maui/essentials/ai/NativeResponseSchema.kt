package com.microsoft.maui.essentials.ai

import com.google.android.gms.internal.mlkit_genai_prompt.zzip
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject

/** A JSON schema converted to ML Kit's native response schema. */
internal class NativeResponseSchema private constructor(private val root: Node) {
    /** Builds ML Kit's internal schema object (the same type its typed API derives from a class). */
    fun native(): zzip = root.native()

    private class Node(
        val type: String,
        val nullable: Boolean,
        val description: String?,
        val properties: Map<String, Node> = emptyMap(),
        val required: List<String> = emptyList(),
        val items: Node? = null,
        val choices: List<String>? = null,
        val format: String? = null,
        val pattern: String? = null,
        val minimum: Double? = null,
        val maximum: Double? = null,
        val minItems: Long? = null,
        val maxItems: Long? = null,
    ) {
        fun native(): zzip {
            val builder = zzip.zzu().zzn(when (type) {
                "string" -> 2
                "number" -> 3
                "integer" -> 4
                "boolean" -> 5
                "array" -> 6
                else -> 7
            })
            if (nullable) builder.zzc(true)
            description?.let { builder.zzb(it) }
            format?.let { builder.zza(it) }
            pattern?.let { builder.zzl(it) }
            minimum?.let { builder.zzj(it) }
            maximum?.let { builder.zzk(it) }
            minItems?.let { builder.zzg(it) }
            maxItems?.let { builder.zzf(it) }
            choices?.let { builder.zzo(it) }
            items?.let { builder.zze(it.native()) }
            if (type == "object") {
                builder.zzi(properties.mapValues { it.value.native() })
                builder.zzh(required)
            }
            return builder.zzm()
        }
    }

    companion object {
        /** System.Text.Json's default maximum depth. */
        const val MAX_DEPTH = 64
        private val annotations = setOf("description", "title", "\$schema", "\$defs", "definitions", "\$comment")

        fun parse(json: String): NativeResponseSchema {
            try {
                val document = JSONObject(json)
                val root = parseNode(document, document, "$", emptySet(), 0)
                // AICore only generates object roots, as OpenAI structured outputs require.
                require(root.type == "object" && !root.nullable) { "The response schema root must be a non-null object." }
                return NativeResponseSchema(root)
            } catch (exception: JSONException) {
                throw IllegalArgumentException("Invalid or unsupported JSON response schema: ${exception.message}", exception)
            }
        }

        private fun parseNode(source: JSONObject, document: JSONObject, path: String, references: Set<String>, depth: Int): Node {
            require(depth < MAX_DEPTH) { "$path: schemas support at most $MAX_DEPTH nested levels." }
            if (source.has("\$ref")) {
                val reference = source.getString("\$ref")
                require(reference == "#" || reference.startsWith("#/")) {
                    "$path: only local JSON schema references are supported."
                }
                require(reference !in references) { "$path: recursive schema reference '$reference' is unsupported." }
                source.keys().forEach { key ->
                    require(key == "\$ref" || key in annotations) { "$path: unsupported reference keyword '$key'." }
                }
                var target: Any = document
                reference.removePrefix("#").split("/").filter { it.isNotEmpty() }.forEach { token ->
                    target = (target as? JSONObject)?.opt(token.replace("~1", "/").replace("~0", "~"))
                        ?: throw IllegalArgumentException("$path: invalid schema reference '$reference'.")
                }
                require(target is JSONObject) { "$path: reference must identify a schema object." }
                val resolved = JSONObject((target as JSONObject).toString())
                if (source.has("description")) resolved.put("description", source.getString("description"))
                return parseNode(resolved, document, path, references + reference, depth)
            }
            val types = when (val raw = source.opt("type")) {
                is String -> listOf(raw)
                is JSONArray -> (0 until raw.length()).map { raw.getString(it) }
                null -> throw IllegalArgumentException("$path: schemas must declare a type.")
                else -> throw IllegalArgumentException("$path: schema type must be a string or type array.")
            }
            val nonNull = types.filter { it != "null" }
            require(nonNull.size == 1 && types.size == types.toSet().size) {
                "$path: schemas support one type with optional null, not unions."
            }
            val type = nonNull.single()
            val nullable = "null" in types
            val keywords = annotations + "type" + when (type) {
                "object" -> setOf("properties", "required", "additionalProperties")
                "array" -> setOf("items", "minItems", "maxItems")
                "string" -> setOf("enum", "format", "pattern")
                "integer", "number" -> setOf("minimum", "maximum")
                "boolean" -> emptySet()
                else -> throw IllegalArgumentException("$path: unsupported response schema type '$type'.")
            }
            source.keys().forEach { key ->
                require(key in keywords) { "$path: unsupported response schema keyword '$key' for type '$type'." }
            }
            val description = if (source.has("description")) source.getString("description") else null

            fun number(key: String): Double? {
                if (!source.has(key)) return null
                val raw = source.get(key)
                require(raw is Number && raw.toDouble().isFinite()) { "$path.$key must be a finite number." }
                return raw.toDouble()
            }
            fun count(key: String): Long? {
                val value = number(key) ?: return null
                require(value >= 0 && value % 1.0 == 0.0 && value <= Int.MAX_VALUE) {
                    "$path.$key must be a nonnegative integer."
                }
                return value.toLong()
            }

            return when (type) {
                "object" -> {
                    require(source.opt("additionalProperties").let { it == null || it is Boolean }) {
                        "$path: additionalProperties must be a boolean; dictionary schemas are unsupported."
                    }
                    val properties = source.optJSONObject("properties")
                    require(properties != null && properties.length() > 0) {
                        "$path: object schemas must declare at least one property."
                    }
                    val required = source.optJSONArray("required")?.let { array ->
                        (0 until array.length()).map { array.getString(it) }
                    } ?: emptyList()
                    require(required.size == required.toSet().size && properties.keys().asSequence().toSet().containsAll(required)) {
                        "$path: required properties must be unique declared properties."
                    }
                    val nodes = linkedMapOf<String, Node>()
                    properties.keys().forEach { name ->
                        nodes[name] = parseNode(properties.getJSONObject(name), document, "$path.$name", references, depth + 1)
                    }
                    Node(type, nullable, description, nodes, required)
                }
                "array" -> {
                    val items = source.optJSONObject("items")
                        ?: throw IllegalArgumentException("$path: array schemas must declare an items schema.")
                    val minimum = count("minItems")
                    val maximum = count("maxItems")
                    require(minimum == null || maximum == null || minimum <= maximum) { "$path: invalid array bounds." }
                    Node(type, nullable, description, items = parseNode(items, document, "$path[]", references, depth + 1),
                        minItems = minimum, maxItems = maximum)
                }
                "integer", "number" -> {
                    val minimum = number("minimum")
                    val maximum = number("maximum")
                    require(minimum == null || maximum == null || minimum <= maximum) { "$path: invalid numeric bounds." }
                    Node(type, nullable, description, minimum = minimum, maximum = maximum)
                }
                "string" -> {
                    val enum = source.optJSONArray("enum")
                    // A nullable type is only nullable when an enum, if present, also lists null.
                    val enumAllowsNull = enum != null && (0 until enum.length()).any { enum.isNull(it) }
                    val choices = enum?.let { array ->
                        require(array.length() > 0) { "$path: enum must not be empty." }
                        require(!enumAllowsNull || nullable) { "$path: null enum values require a nullable type." }
                        (0 until array.length()).filterNot { array.isNull(it) }.map {
                            require(array.get(it) is String) { "$path: enum values must be strings." }
                            array.getString(it)
                        }.also { require(it.isNotEmpty()) { "$path: null-only enums are unsupported." } }
                    }
                    Node(type, nullable && (enum == null || enumAllowsNull), description, choices = choices,
                        format = if (source.has("format")) source.getString("format") else null,
                        pattern = if (source.has("pattern")) source.getString("pattern") else null)
                }
                else -> Node(type, nullable, description)
            }
        }
    }
}
