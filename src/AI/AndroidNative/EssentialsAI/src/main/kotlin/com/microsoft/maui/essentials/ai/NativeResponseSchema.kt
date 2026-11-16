package com.microsoft.maui.essentials.ai

import com.google.android.gms.internal.mlkit_genai_prompt.zzip
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.math.BigDecimal

/**
 * A JSON schema compiled for ML Kit's native response-schema field, plus a strict
 * validator for the JSON text that AICore generates under that constraint.
 */
internal class NativeResponseSchema private constructor(private val root: Node) {
    /** AICore returns non-object roots as {"wrapper": value}; the value is unwrapped after validation. */
    val wrapsRoot: Boolean get() = root.type != "object"

    /** Builds ML Kit's internal schema object (the same type its typed API derives from a class). */
    fun native(): zzip = root.native()

    /** Validates generated text and returns the JSON that matches the caller's schema. */
    fun validate(text: String): String {
        val value = StrictJson.parse(text)
        if (!wrapsRoot) {
            root.validate(value, "$")
            return text
        }
        require(value is Map<*, *> && value.keys == setOf(WRAPPER)) {
            "Expected AICore's {\"$WRAPPER\": ...} wrapper for a non-object root."
        }
        val unwrapped = value[WRAPPER]
        root.validate(unwrapped, "$")
        return StrictJson.write(unwrapped)
    }

    private class Node(
        val type: String,
        val nullable: Boolean,
        val description: String?,
        val properties: Map<String, Node> = emptyMap(),
        val required: List<String> = emptyList(),
        val closed: Boolean = false,
        val items: Node? = null,
        val choices: List<String>? = null,
        val format: String? = null,
        val pattern: Regex? = null,
        val minimum: BigDecimal? = null,
        val maximum: BigDecimal? = null,
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
            pattern?.let { builder.zzl(it.pattern) }
            minimum?.let { builder.zzj(it.toDouble()) }
            maximum?.let { builder.zzk(it.toDouble()) }
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

        fun validate(value: Any?, path: String) {
            if (value === StrictJson.Null) {
                require(nullable) { "$path must not be null." }
                return
            }
            when (type) {
                "object" -> {
                    require(value is Map<*, *>) { "$path must be an object." }
                    required.forEach { require(value.containsKey(it)) { "$path is missing required property '$it'." } }
                    value.forEach { (name, item) ->
                        val property = properties[name]
                        require(property != null || !closed) { "$path has unexpected property '$name'." }
                        property?.validate(item, "$path.$name")
                    }
                }
                "array" -> {
                    require(value is List<*>) { "$path must be an array." }
                    require(minItems == null || value.size >= minItems) { "$path has fewer than $minItems items." }
                    require(maxItems == null || value.size <= maxItems) { "$path has more than $maxItems items." }
                    value.forEachIndexed { index, item -> checkNotNull(items).validate(item, "$path[$index]") }
                }
                "string" -> {
                    require(value is String) { "$path must be a string." }
                    require(choices == null || value in choices) { "$path is outside its enum." }
                    require(pattern == null || pattern.containsMatchIn(value)) { "$path does not match its pattern." }
                }
                "boolean" -> require(value is Boolean) { "$path must be a boolean." }
                else -> {
                    require(value is BigDecimal) { "$path must be a number." }
                    require(type == "number" || value.signum() == 0 || value.stripTrailingZeros().scale() <= 0) {
                        "$path must be an integer."
                    }
                    require(minimum == null || value >= minimum) { "$path is below its minimum." }
                    require(maximum == null || value <= maximum) { "$path is above its maximum." }
                }
            }
        }
    }

    companion object {
        private const val WRAPPER = "wrapper"
        private val annotations = setOf("description", "title", "\$schema", "\$defs", "definitions", "\$comment")

        fun parse(json: String): NativeResponseSchema {
            try {
                val document = JSONObject(json)
                val root = parseNode(document, document, "$", emptySet())
                require(!root.nullable) { "The response schema root must not be nullable." }
                return NativeResponseSchema(root)
            } catch (exception: JSONException) {
                throw IllegalArgumentException("Invalid or unsupported JSON response schema: ${exception.message}", exception)
            }
        }

        private fun parseNode(source: JSONObject, document: JSONObject, path: String, references: Set<String>): Node {
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
                return parseNode(resolved, document, path, references + reference)
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

            // Bounds keep their exact JSON value for validation; ML Kit's native schema takes doubles.
            fun number(key: String): BigDecimal? {
                if (!source.has(key)) return null
                val raw = source.get(key)
                require(raw is Number && raw.toDouble().isFinite()) { "$path.$key must be a finite number." }
                return BigDecimal(raw.toString())
            }
            fun count(key: String): Long? {
                val value = number(key) ?: return null
                require(value.signum() >= 0 && value.stripTrailingZeros().scale() <= 0 &&
                    value <= BigDecimal(Int.MAX_VALUE)) {
                    "$path.$key must be a nonnegative integer."
                }
                return value.toLong()
            }

            return when (type) {
                "object" -> {
                    val closed = when (val additional = source.opt("additionalProperties")) {
                        null -> false
                        is Boolean -> !additional
                        else -> throw IllegalArgumentException(
                            "$path: additionalProperties must be a boolean; dictionary schemas are unsupported.")
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
                        nodes[name] = parseNode(properties.getJSONObject(name), document, "$path.$name", references)
                    }
                    Node(type, nullable, description, nodes, required, closed)
                }
                "array" -> {
                    val items = source.optJSONObject("items")
                        ?: throw IllegalArgumentException("$path: array schemas must declare an items schema.")
                    val minimum = count("minItems")
                    val maximum = count("maxItems")
                    require(minimum == null || maximum == null || minimum <= maximum) { "$path: invalid array bounds." }
                    Node(type, nullable, description, items = parseNode(items, document, "$path[]", references),
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
                    val pattern = if (source.has("pattern")) {
                        try {
                            Regex(source.getString("pattern"))
                        } catch (exception: IllegalArgumentException) {
                            throw IllegalArgumentException("$path: invalid pattern: ${exception.message}", exception)
                        }
                    } else null
                    Node(type, nullable && (enum == null || enumAllowsNull), description, choices = choices,
                        format = if (source.has("format")) source.getString("format") else null, pattern = pattern)
                }
                else -> Node(type, nullable, description)
            }
        }
    }
}

/** RFC 8259 parser: Android's org.json accepts single quotes, comments and unquoted names. */
internal object StrictJson {
    object Null

    fun write(value: Any?): String = StringBuilder().also { write(value, it) }.toString()

    private fun write(value: Any?, out: StringBuilder) {
        when (value) {
            null, Null -> out.append("null")
            is Boolean -> out.append(value)
            is BigDecimal -> out.append(value.toString())
            is String -> {
                out.append('"')
                value.forEach { char ->
                    when {
                        char == '"' -> out.append("\\\"")
                        char == '\\' -> out.append("\\\\")
                        char == '\n' -> out.append("\\n")
                        char == '\r' -> out.append("\\r")
                        char == '\t' -> out.append("\\t")
                        char < ' ' -> out.append("\\u%04x".format(char.code))
                        else -> out.append(char)
                    }
                }
                out.append('"')
            }
            is List<*> -> {
                out.append('[')
                value.forEachIndexed { index, item -> if (index > 0) out.append(','); write(item, out) }
                out.append(']')
            }
            is Map<*, *> -> {
                out.append('{')
                value.entries.forEachIndexed { index, (key, item) ->
                    if (index > 0) out.append(',')
                    write(key as String, out)
                    out.append(':')
                    write(item, out)
                }
                out.append('}')
            }
            else -> error("Unexpected JSON value ${value.javaClass.name}.")
        }
    }

    fun parse(text: String): Any {
        val reader = Reader(text)
        reader.whitespace()
        val value = reader.value(0)
        reader.whitespace()
        reader.require(reader.index == text.length, "Unexpected trailing content")
        return value
    }

    private class Reader(val text: String) {
        var index = 0

        fun fail(message: String): Nothing =
            throw IllegalArgumentException("Generated output is not valid JSON: $message at offset $index.")

        fun require(condition: Boolean, message: String) {
            if (!condition) fail(message)
        }

        fun whitespace() {
            while (index < text.length && text[index] in " \t\r\n") index++
        }

        fun value(depth: Int): Any {
            require(depth < 512, "Nesting is too deep")
            require(index < text.length, "Unexpected end")
            return when (text[index]) {
                '{' -> obj(depth)
                '[' -> array(depth)
                '"' -> string()
                't' -> literal("true", true)
                'f' -> literal("false", false)
                'n' -> literal("null", Null)
                else -> number()
            }
        }

        private fun literal(word: String, result: Any): Any {
            require(text.startsWith(word, index), "Invalid literal")
            index += word.length
            return result
        }

        private fun obj(depth: Int): Map<String, Any?> {
            index++
            val result = linkedMapOf<String, Any?>()
            whitespace()
            if (index < text.length && text[index] == '}') { index++; return result }
            while (true) {
                whitespace()
                require(index < text.length && text[index] == '"', "Expected a property name")
                val name = string()
                require(name !in result, "Duplicate property '$name'")
                whitespace()
                require(index < text.length && text[index] == ':', "Expected ':'")
                index++
                whitespace()
                result[name] = value(depth + 1)
                whitespace()
                require(index < text.length, "Unexpected end")
                when (text[index++]) {
                    ',' -> continue
                    '}' -> return result
                    else -> { index--; fail("Expected ',' or '}'") }
                }
            }
        }

        private fun array(depth: Int): List<Any?> {
            index++
            val result = mutableListOf<Any?>()
            whitespace()
            if (index < text.length && text[index] == ']') { index++; return result }
            while (true) {
                whitespace()
                result.add(value(depth + 1))
                whitespace()
                require(index < text.length, "Unexpected end")
                when (text[index++]) {
                    ',' -> continue
                    ']' -> return result
                    else -> { index--; fail("Expected ',' or ']'") }
                }
            }
        }

        private fun string(): String {
            index++
            val result = StringBuilder()
            while (true) {
                require(index < text.length, "Unterminated string")
                val char = text[index++]
                when {
                    char == '"' -> return result.toString()
                    char == '\\' -> {
                        require(index < text.length, "Unterminated escape")
                        when (val escape = text[index++]) {
                            '"', '\\', '/' -> result.append(escape)
                            'b' -> result.append('\b')
                            'f' -> result.append('\u000C')
                            'n' -> result.append('\n')
                            'r' -> result.append('\r')
                            't' -> result.append('\t')
                            'u' -> {
                                require(index + 4 <= text.length, "Invalid unicode escape")
                                val digits = text.substring(index, index + 4)
                                require(digits.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' },
                                    "Invalid unicode escape")
                                result.append(digits.toInt(16).toChar())
                                index += 4
                            }
                            else -> fail("Invalid escape")
                        }
                    }
                    else -> {
                        require(char >= ' ', "Unescaped control character")
                        result.append(char)
                    }
                }
            }
        }

        private fun number(): BigDecimal {
            val start = index
            if (index < text.length && text[index] == '-') index++
            require(index < text.length && text[index].isAsciiDigit(), "Invalid value")
            if (text[index] == '0') index++ else while (index < text.length && text[index].isAsciiDigit()) index++
            if (index < text.length && text[index] == '.') {
                index++
                require(index < text.length && text[index].isAsciiDigit(), "Invalid number")
                while (index < text.length && text[index].isAsciiDigit()) index++
            }
            if (index < text.length && text[index] in "eE") {
                index++
                if (index < text.length && text[index] in "+-") index++
                require(index < text.length && text[index].isAsciiDigit(), "Invalid number")
                while (index < text.length && text[index].isAsciiDigit()) index++
            }
            return BigDecimal(text.substring(start, index))
        }

        private fun Char.isAsciiDigit() = this in '0'..'9'
    }
}
