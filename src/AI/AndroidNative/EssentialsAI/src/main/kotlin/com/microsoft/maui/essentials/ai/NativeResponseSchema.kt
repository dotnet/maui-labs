package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.schema.guided.GenerableDetail
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import kotlin.reflect.KClass

internal class NativeResponseSchema private constructor(
    private val root: Node,
    private val objects: List<Node>,
) {
    fun detail(index: Int): GenerableDetail<*> {
        val node = objects[index]
        return GenerableDetail<Any>(node.description,
            node.properties.map { (name, value) -> value.guide(name) })
    }

    fun project(value: Map<String, Any?>): String = root.project(value, "$").toString()

    private data class Node(
        val type: String,
        val description: String,
        val nullable: Boolean = false,
        val index: Int = -1,
        val properties: Map<String, Node> = emptyMap(),
        val items: Node? = null,
        val choices: List<String>? = null,
        val minimum: Double? = null,
        val maximum: Double? = null,
        val minItems: Int? = null,
        val maxItems: Int? = null,
    ) {
        private fun kotlinType(): KClass<*> = when (type) {
            "object" -> NativeResponseProviders.classes[index]
            "array" -> List::class
            "string" -> String::class
            "integer" -> Long::class
            "number" -> Double::class
            "boolean" -> Boolean::class
            else -> error("Uncompiled response schema type '$type'.")
        }

        fun guide(name: String) = GenerableDetail.GuideDetail(
            name = name,
            type = kotlinType(),
            nullable = nullable,
            description = description,
            maxItems = maxItems,
            minItems = minItems,
            maximum = maximum ?: if (type == "integer") MAX_EXACT_INTEGER else null,
            minimum = minimum ?: if (type == "integer") -MAX_EXACT_INTEGER else null,
            enumValues = (if (type == "array") items?.choices else choices)?.toTypedArray(),
            isList = type == "array",
            listItemType = items?.kotlinType(),
        )

        fun project(value: Any?, path: String): Any {
            if (value == null || value === JSONObject.NULL) {
                require(nullable) { "$path must not be null." }
                return JSONObject.NULL
            }
            return when (type) {
                "object" -> {
                    require(value is Map<*, *> && value.keys == properties.keys) {
                        "$path must contain exactly the required object properties."
                    }
                    JSONObject().apply {
                        properties.forEach { (name, node) -> put(name, node.project(value[name], "$path.$name")) }
                    }
                }
                "array" -> {
                    require(value is List<*>) { "$path must be an array." }
                    require(minItems == null || value.size >= minItems) { "$path has too few items." }
                    require(maxItems == null || value.size <= maxItems) { "$path has too many items." }
                    JSONArray().apply {
                        value.forEachIndexed { index, item -> put(checkNotNull(items).project(item, "$path[$index]")) }
                    }
                }
                "string" -> {
                    require(value is String) { "$path must be a string." }
                    require(choices == null || value in choices) { "$path is outside its enum." }
                    value
                }
                "boolean" -> {
                    require(value is Boolean) { "$path must be a boolean." }
                    value
                }
                "integer", "number" -> {
                    require(value is Number) { "$path must be a number." }
                    val number = value.toDouble()
                    require(number.isFinite() && (minimum == null || number >= minimum) &&
                        (maximum == null || number <= maximum)) { "$path is outside its numeric bounds." }
                    if (type == "integer") {
                        // The SDK's Map<String, Any?> deserializer represents numbers as Double.
                        require(number % 1.0 == 0.0 && number in -MAX_EXACT_INTEGER..MAX_EXACT_INTEGER) {
                            "$path must be an exactly representable integer between -2^53+1 and 2^53-1."
                        }
                        number.toLong()
                    } else number
                }
                else -> error("Uncompiled response schema type '$type'.")
            }
        }
    }

    companion object {
        private const val MAX_EXACT_INTEGER = 9007199254740991.0

        fun parse(json: String): NativeResponseSchema {
            try {
                val document = JSONObject(json)
                val objects = mutableListOf<Node?>()
                fun parseNode(source: JSONObject, path: String, references: Set<String>): Node {
                    if (source.has("$" + "ref")) {
                        val reference = source.getString("$" + "ref")
                        require(reference.startsWith("#/") && reference !in references) {
                            "$path: only acyclic local JSON schema references are supported."
                        }
                        source.keys().forEach { key ->
                            require(key in setOf("$" + "ref", "description", "title",
                                "$" + "schema", "$" + "defs", "definitions")) {
                                "$path: unsupported reference keyword '$key'."
                            }
                        }
                        var target: Any = document
                        reference.removePrefix("#/").split("/").forEach { token ->
                            target = (target as? JSONObject)?.get(token.replace("~1", "/").replace("~0", "~"))
                                ?: throw IllegalArgumentException("$path: invalid schema reference '$reference'.")
                        }
                        require(target is JSONObject) { "$path: reference must identify a schema object." }
                        val resolved = JSONObject(target.toString())
                        if (source.has("description")) resolved.put("description", source.getString("description"))
                        return parseNode(resolved, path, references + reference)
                    }
                    val types = when (val raw = source.get("type")) {
                        is String -> listOf(raw)
                        is JSONArray -> (0 until raw.length()).map { raw.getString(it) }
                        else -> throw IllegalArgumentException("$path: schema type must be a string or a nullable type array.")
                    }
                    val nonNull = types.filter { it != "null" }
                    require(nonNull.size == 1 && types.size == types.toSet().size) {
                        "$path: schemas support one type with optional null, not unions."
                    }
                    val type = nonNull.single()
                    val keywords = setOf("type", "description", "title", "$" + "schema", "$" + "defs", "definitions") + when (type) {
                        "object" -> setOf("properties", "required", "additionalProperties")
                        "array" -> setOf("items", "minItems", "maxItems")
                        "string" -> setOf("enum")
                        "integer", "number" -> setOf("minimum", "maximum")
                        "boolean" -> emptySet()
                        else -> throw IllegalArgumentException("$path: unsupported response schema type '$type'.")
                    }
                    source.keys().forEach { key ->
                        require(key in keywords) { "$path: unsupported response schema keyword '$key'." }
                    }
                    val description = if (source.has("description")) source.getString("description") else ""
                    val nullable = "null" in types
                    fun number(key: String): Double? {
                        if (!source.has(key)) return null
                        val raw = source.get(key)
                        require(raw is Number && raw.toDouble().isFinite()) { "$path.$key must be a finite number." }
                        return raw.toDouble()
                    }
                    fun integer(key: String): Int? {
                        val value = number(key) ?: return null
                        require(value in 0.0..Int.MAX_VALUE.toDouble() && value % 1.0 == 0.0) {
                            "$path.$key must be a nonnegative Int32 value."
                        }
                        return value.toInt()
                    }
                    return when (type) {
                        "object" -> {
                            require(source.has("additionalProperties") && source.get("additionalProperties") == false) {
                                "$path: object schemas require additionalProperties=false."
                            }
                            val properties = source.getJSONObject("properties")
                            require(properties.length() > 0) { "$path: empty object schemas are unsupported." }
                            val required = source.getJSONArray("required")
                            val names = (0 until required.length()).map { required.getString(it) }
                            require(names.size == names.toSet().size && names.toSet() == properties.keys().asSequence().toSet()) {
                                "$path: every declared property must be required."
                            }
                            val index = objects.size
                            require(index < NativeResponseProviders.classes.size) {
                                "$path: schemas support at most ${NativeResponseProviders.classes.size} object shapes."
                            }
                            objects.add(null)
                            Node(type, description, nullable, index,
                                names.associateWith { parseNode(properties.getJSONObject(it), "$path.$it", references) })
                                .also { objects[index] = it }
                        }
                        "array" -> {
                            val item = parseNode(source.getJSONObject("items"), "$path[]", references)
                            require(item.type != "array" && !item.nullable &&
                                item.minimum == null && item.maximum == null) {
                                "$path: nested arrays, nullable items, and numeric item bounds are unsupported."
                            }
                            val minimum = integer("minItems")
                            val maximum = integer("maxItems")
                            require(minimum == null || maximum == null || minimum <= maximum) { "$path: invalid array bounds." }
                            Node(type, description, nullable, items = item, minItems = minimum, maxItems = maximum)
                        }
                        "integer", "number" -> {
                            val minimum = number("minimum")
                            val maximum = number("maximum")
                            require(minimum == null || maximum == null || minimum <= maximum) { "$path: invalid numeric bounds." }
                            if (type == "integer") {
                                require((minimum == null || minimum >= -MAX_EXACT_INTEGER) &&
                                    (maximum == null || maximum <= MAX_EXACT_INTEGER)) {
                                    "$path: integer bounds must stay within -2^53+1 to 2^53-1."
                                }
                            }
                            Node(type, description, nullable, minimum = minimum, maximum = maximum)
                        }
                        "string" -> {
                            var enumAllowsNull = false
                            val values = if (source.has("enum")) source.getJSONArray("enum").let { array ->
                                require(array.length() > 0) { "$path: enum must not be empty." }
                                enumAllowsNull = (0 until array.length()).any { array.isNull(it) }
                                require(!enumAllowsNull || nullable) { "$path: null enum values require a nullable type." }
                                (0 until array.length()).filterNot { array.isNull(it) }.map { array.getString(it) }
                                    .also { require(it.isNotEmpty()) { "$path: null-only enums are unsupported." } }
                            } else null
                            Node(type, description, nullable && (values == null || enumAllowsNull), choices = values)
                        }
                        else -> Node(type, description, nullable)
                    }
                }
                val root = parseNode(document, "$", emptySet())
                require(root.type == "object" && !root.nullable) { "Response schemas require a non-null object root." }
                return NativeResponseSchema(root, objects.requireNoNulls().toList())
            } catch (exception: JSONException) {
                throw IllegalArgumentException("Invalid or unsupported JSON response schema: ${exception.message}", exception)
            }
        }
    }
}
