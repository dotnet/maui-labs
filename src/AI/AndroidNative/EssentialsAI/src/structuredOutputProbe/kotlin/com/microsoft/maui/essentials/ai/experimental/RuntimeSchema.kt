package com.microsoft.maui.essentials.ai.experimental

import com.google.mlkit.genai.schema.guided.GenerableDetail
import org.json.JSONArray
import org.json.JSONObject
import kotlin.reflect.KClass

internal class RuntimeSchema private constructor(
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
        val index: Int = -1,
        val properties: Map<String, Node> = emptyMap(),
        val items: Node? = null,
        val choices: List<String>? = null,
        val minimum: Int? = null,
        val maximum: Int? = null,
        val minItems: Int? = null,
        val maxItems: Int? = null,
    ) {
        private fun kotlinType(): KClass<*> = when (type) {
            "object" -> RuntimeSchemaProviders.classes[index]
            "array" -> List::class
            "string" -> String::class
            "integer" -> Int::class
            "boolean" -> Boolean::class
            else -> error("Uncompiled runtime schema type '$type'.")
        }

        fun guide(name: String) = GenerableDetail.GuideDetail(
            name = name,
            type = kotlinType(),
            nullable = false,
            description = description,
            maxItems = maxItems,
            minItems = minItems,
            maximum = maximum?.toDouble(),
            minimum = minimum?.toDouble(),
            enumValues = (if (type == "array") items?.choices else choices)?.toTypedArray(),
            isList = type == "array",
            listItemType = items?.kotlinType(),
        )

        fun project(value: Any?, path: String): Any = when (type) {
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
            "integer" -> {
                require(value is Number) { "$path must be an integer." }
                val number = value.toDouble()
                require(number.isFinite() && number % 1.0 == 0.0 &&
                    number >= checkNotNull(minimum) && number <= checkNotNull(maximum)) {
                    "$path must be an integer within its declared Int32 bounds."
                }
                number.toInt()
            }
            else -> error("Uncompiled runtime schema type '$type'.")
        }
    }

    companion object {
        fun parse(json: String): RuntimeSchema {
            val objects = mutableListOf<Node?>()
            fun parseNode(schema: JSONObject, path: String): Node {
                val type = schema.getString("type")
                val keywords = setOf("type", "description", "title", "$" + "schema") + when (type) {
                    "object" -> setOf("properties", "required", "additionalProperties")
                    "array" -> setOf("items", "minItems", "maxItems")
                    "string" -> setOf("enum")
                    "integer" -> setOf("minimum", "maximum")
                    "boolean" -> emptySet()
                    else -> error("$path: runtime schema probe cannot represent type '$type'.")
                }
                schema.keys().forEach { key ->
                    require(key in keywords) { "$path: runtime schema probe cannot represent keyword '$key'." }
                }
                val description = if (schema.has("description")) schema.getString("description") else ""
                fun integer(key: String): Int? {
                    if (!schema.has(key)) return null
                    val raw = schema.get(key)
                    require(raw is Number && raw.toDouble().isFinite() &&
                        raw.toDouble() >= Int.MIN_VALUE && raw.toDouble() <= Int.MAX_VALUE &&
                        raw.toDouble() % 1.0 == 0.0) { "$path.$key must be an Int32 value." }
                    return raw.toInt()
                }
                return when (type) {
                    "object" -> {
                        require(schema.has("additionalProperties") && schema.get("additionalProperties") == false) {
                            "$path: runtime object schemas require additionalProperties=false."
                        }
                        val properties = schema.getJSONObject("properties")
                        require(properties.length() > 0) { "$path: empty runtime object schemas are unsupported." }
                        val required = schema.getJSONArray("required")
                        val names = (0 until required.length()).map { required.getString(it) }
                        require(names.size == names.toSet().size && names.toSet() == properties.keys().asSequence().toSet()) {
                            "$path: every property must be required; optional fields are unsupported."
                        }
                        val index = objects.size
                        require(index < RuntimeSchemaProviders.classes.size) {
                            "$path: runtime schema probe supports at most ${RuntimeSchemaProviders.classes.size} object shapes."
                        }
                        objects.add(null)
                        Node(type, description, index,
                            names.associateWith { parseNode(properties.getJSONObject(it), "$path.$it") })
                            .also { objects[index] = it }
                    }
                    "array" -> {
                        val item = parseNode(schema.getJSONObject("items"), "$path[]")
                        require(item.type != "array" && item.type != "integer") {
                            "$path: nested arrays and integer arrays are unsupported by this probe."
                        }
                        val minimum = integer("minItems")
                        val maximum = integer("maxItems")
                        require((minimum == null || minimum >= 0) && (maximum == null || maximum >= 0) &&
                            (minimum == null || maximum == null || minimum <= maximum)) {
                            "$path: invalid array length bounds."
                        }
                        Node(type, description, items = item, minItems = minimum, maxItems = maximum)
                    }
                    "integer" -> {
                        val minimum = integer("minimum")
                        val maximum = integer("maximum")
                        require(minimum != null && maximum != null && minimum <= maximum) {
                            "$path: integers require explicit minimum/maximum within Int32 for lossless map projection."
                        }
                        Node(type, description, minimum = minimum, maximum = maximum)
                    }
                    "string" -> {
                        val values = if (schema.has("enum")) schema.getJSONArray("enum").let { array ->
                            require(array.length() > 0) { "$path: enum must not be empty." }
                            (0 until array.length()).map { array.getString(it) }
                        } else null
                        Node(type, description, choices = values)
                    }
                    else -> Node(type, description)
                }
            }
            val root = parseNode(JSONObject(json), "$")
            require(root.type == "object") { "Runtime structured-output schemas must have an object root." }
            return RuntimeSchema(root, objects.requireNoNulls().toList())
        }
    }
}
