package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.FunctionArgValue
import com.google.mlkit.genai.common.FunctionCall
import com.google.mlkit.genai.prompt.FunctionCallPart
import com.google.mlkit.genai.prompt.FunctionResponsePart
import com.google.mlkit.genai.schema.tools.FunctionDeclaration
import com.google.mlkit.genai.schema.tools.FunctionSchema
import com.google.mlkit.genai.schema.tools.FunctionType
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

internal fun nativeTools(json: String): List<FunctionDeclaration> =
    JSONArray(json).let { tools ->
        (0 until tools.length()).map { index ->
            val tool = tools.getJSONObject(index)
            val parameters = functionSchema(tool.getJSONObject("parameters"))
            require(parameters.type == FunctionType.OBJECT) {
                "ML Kit function parameters must have an object schema."
            }
            val parameterKeys = tool.getJSONObject("parameters").keys().asSequence().toSet()
            val parameterless = parameters.properties.isNullOrEmpty() && parameters.required.isNullOrEmpty() &&
                parameterKeys.all { it in setOf("type", "properties", "required", "title", "description", "$" + "schema") }
            // AICore's tokenizer rejects empty object schemas; no schema denotes a parameterless tool.
            FunctionDeclaration(tool.getString("name"), tool.getString("description"),
                if (parameterless) null else parameters)
        }
    }

internal fun functionSchema(json: JSONObject): FunctionSchema {
    val supported = setOf("type", "description", "properties", "required", "enum", "format",
        "nullable", "items", "minItems", "maxItems", "minimum", "maximum", "minLength",
        "maxLength", "pattern", "anyOf", "title", "minProperties", "maxProperties", "$" + "schema")
    for (key in json.keys()) {
        require(key in supported) { "ML Kit cannot represent tool schema keyword '$key'." }
    }
    fun string(key: String) = if (json.has(key)) json.getString(key) else null
    fun long(key: String) = if (json.has(key)) json.getLong(key) else null
    fun double(key: String) = if (json.has(key)) json.getDouble(key) else null
    fun obj(key: String) = if (json.has(key)) json.getJSONObject(key) else null
    fun array(key: String) = if (json.has(key)) json.getJSONArray(key) else null
    fun strings(key: String): List<String>? = array(key)?.let { values ->
        (0 until values.length()).map { values.getString(it) }
    }
    val typeValue = json.opt("type")
    val types = when (typeValue) {
        is String -> listOf(typeValue)
        is JSONArray -> (0 until typeValue.length()).map { typeValue.getString(it) }
        null -> emptyList()
        else -> error("Tool schema type must be a string or a nullable type array.")
    }
    require(types.filter { it != "null" }.size <= 1) {
        "ML Kit tool schemas support one type plus null; use anyOf for unions."
    }
    val type = when (val name = types.firstOrNull { it != "null" }) {
        null -> if (types.contains("null")) FunctionType.NULL else FunctionType.UNSPECIFIED
        "string" -> FunctionType.STRING
        "number" -> FunctionType.NUMBER
        "integer" -> FunctionType.INTEGER
        "boolean" -> FunctionType.BOOLEAN
        "array" -> FunctionType.ARRAY
        "object" -> FunctionType.OBJECT
        else -> error("Unsupported ML Kit tool schema type '$name'.")
    }
    return FunctionSchema(
        type = type,
        description = string("description"),
        properties = obj("properties")?.let { properties ->
            properties.keys().asSequence().associateWith { functionSchema(properties.getJSONObject(it)) }
        },
        required = strings("required"),
        enum = strings("enum"),
        format = string("format"),
        nullable = if ("null" in types) true else if (json.has("nullable")) json.getBoolean("nullable") else null,
        items = obj("items")?.let(::functionSchema),
        minItems = long("minItems"),
        maxItems = long("maxItems"),
        minimum = double("minimum"),
        maximum = double("maximum"),
        minLength = long("minLength"),
        maxLength = long("maxLength"),
        pattern = string("pattern"),
        anyOf = array("anyOf")?.let { values ->
            (0 until values.length()).map { functionSchema(values.getJSONObject(it)) }
        },
        title = string("title"),
        minProperties = long("minProperties"),
        maxProperties = long("maxProperties"),
    )
}

internal fun functionCallPart(json: String): FunctionCallPart {
    val call = JSONObject(json)
    val arguments = if (call.isNull("arguments")) JSONObject() else call.getJSONObject("arguments")
    return FunctionCallPart(FunctionCall(call.getString("name"),
        arguments.keys().asSequence().associateWith { argumentValue(arguments.get(it)) },
        call.getString("callId")))
}

private fun argumentValue(value: Any): FunctionArgValue = when (value) {
    is String -> FunctionArgValue.StringValue(value)
    is Boolean -> FunctionArgValue.BoolValue(value)
    is Int -> FunctionArgValue.IntValue(value)
    is Long -> FunctionArgValue.LongValue(value)
    is Number -> FunctionArgValue.DoubleValue(value.toDouble())
    is JSONArray -> FunctionArgValue.ListValue((0 until value.length()).map { argumentValue(value.get(it)) })
    is JSONObject -> FunctionArgValue.MapValue(value.keys().asSequence().associateWith { argumentValue(value.get(it)) })
    else -> error("ML Kit function arguments cannot represent null values.")
}

internal fun functionResultPart(json: String, names: Map<String, String>): FunctionResponsePart {
    val result = JSONObject(json)
    val id = result.getString("callId")
    val name = requireNotNull(names[id]) { "Tool result '$id' has no matching function call in history." }
    // MEAI permits scalar results; the native API requires an object.
    return FunctionResponsePart(id, name, mapOf("result" to jsonValue(result.get("result"))))
}

private fun jsonValue(value: Any): Any = when (value) {
    JSONObject.NULL -> throw IllegalArgumentException("ML Kit cannot preserve null values in native function results.")
    is JSONObject -> value.keys().asSequence().associateWith { jsonValue(value.get(it)) }
    is JSONArray -> (0 until value.length()).map { jsonValue(value.get(it)) }
    else -> value
}

internal fun FunctionCall.toNativeCall(): NativeFunctionCall =
    NativeFunctionCall(id ?: UUID.randomUUID().toString(), name,
        JSONObject(arguments.mapValues { (_, value) -> value.toJsonValue() }).toString())

private fun FunctionArgValue.toJsonValue(): Any = when (this) {
    is FunctionArgValue.StringValue -> value
    is FunctionArgValue.BoolValue -> value
    is FunctionArgValue.IntValue -> value
    is FunctionArgValue.LongValue -> value
    is FunctionArgValue.DoubleValue -> value
    is FunctionArgValue.ListValue -> JSONArray(values.map { it.toJsonValue() })
    is FunctionArgValue.MapValue -> JSONObject(map.mapValues { (_, value) -> value.toJsonValue() })
}
