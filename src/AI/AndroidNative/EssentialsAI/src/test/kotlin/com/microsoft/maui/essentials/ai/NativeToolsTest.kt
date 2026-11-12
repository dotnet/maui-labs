package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.FunctionArgValue
import com.google.mlkit.genai.common.FunctionCall
import com.google.mlkit.genai.prompt.Content
import com.google.mlkit.genai.prompt.FunctionCallPart
import com.google.mlkit.genai.prompt.FunctionResponsePart
import com.google.mlkit.genai.schema.tools.FunctionType
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class NativeToolsTest {
    @Test
    fun mapsFunctionSchemasWithoutInventingReturnConstraints() {
        val tools = nativeTools("""[{"name":"lookup","description":"Find an item","parameters":{
            "type":"object","properties":{
                "key":{"type":"string","description":"Item key","minLength":1},
                "limit":{"type":["integer","null"],"minimum":1,"maximum":10},
                "tags":{"type":"array","items":{"type":"string","enum":["a","b"]},"maxItems":3}
            },"required":["key"]}}]""")
        val tool = tools.single()
        assertEquals("lookup", tool.name)
        assertNull(tool.responseSchema)
        assertEquals(FunctionType.OBJECT, tool.parameters!!.type)
        assertEquals(listOf("key"), tool.parameters!!.required)
        val properties = tool.parameters!!.properties!!
        assertEquals(1L, properties["key"]!!.minLength)
        assertTrue(properties["limit"]!!.nullable!!)
        assertEquals(10.0, properties["limit"]!!.maximum!!, 0.0)
        assertEquals(listOf("a", "b"), properties["tags"]!!.items!!.enum)
        val parameterless = nativeTools("""[{"name":"clock","description":"Current time",
            "parameters":{"type":"object","properties":{}}}]""").single()
        assertNull(parameterless.parameters)
        val constrained = nativeTools("""[{"name":"object","description":"Constrained",
            "parameters":{"type":"object","maxProperties":0}}]""").single()
        assertEquals(0L, constrained.parameters!!.maxProperties)
    }

    @Test
    fun rejectsUnrepresentableSchemaRatherThanDroppingConstraints() {
        for (schema in listOf(
            """{"type":"object","additionalProperties":{"type":"string"}}""",
            """{"type":"string","const":"fixed"}""",
            """{"type":["string","integer"]}""",
            """{"${"$"}ref":"#/${"$"}defs/item"}""",
        )) {
            assertThrows(IllegalArgumentException::class.java) { functionSchema(JSONObject(schema)) }
        }
    }

    @Test
    fun roundTripsNestedArgumentsAndAssignsMissingNativeCallIds() {
        val original = FunctionCall("lookup", mapOf(
            "text" to FunctionArgValue.StringValue("quote\"\nline"),
            "flag" to FunctionArgValue.BoolValue(true),
            "count" to FunctionArgValue.LongValue(9007199254740991L),
            "items" to FunctionArgValue.ListValue(listOf(
                FunctionArgValue.MapValue(mapOf("x" to FunctionArgValue.DoubleValue(1.5))))),
        ), null)
        val native = original.toNativeCall()
        assertTrue(native.id.isNotBlank())
        val replay = functionCallPart("""{"callId":"${native.id}","name":"lookup","arguments":${native.argumentsJson}}""")
        assertEquals(original.name, replay.functionCall.name)
        assertEquals(original.arguments, replay.functionCall.arguments)
        assertEquals(native.id, replay.functionCall.id)
    }

    @Test
    fun functionResultsRetainCallIdentityAndRejectUnrepresentableNullsAndUnmatchedCalls() {
        val result = functionResultPart("""{"callId":"id","result":"secret"}""", mapOf("id" to "lookup"))
        assertEquals("id", result.id)
        assertEquals("lookup", result.name)
        assertEquals("secret", result.response["result"])
        assertThrows(IllegalArgumentException::class.java) {
            functionResultPart("""{"callId":"id","result":null}""", mapOf("id" to "lookup"))
        }
        assertThrows(IllegalArgumentException::class.java) {
            functionResultPart("""{"callId":"missing","result":1}""", emptyMap())
        }
    }

    @Test
    fun registeredToolsUseNativeRolesCallsAndResponsesNotPromptLabels() {
        val options = NativeChatOptions(null, null, null, null, false, "gemini-nano-preview-fast").apply {
            toolsJson = """[{"name":"lookup","description":"Lookup","parameters":{"type":"object"}}]"""
        }
        val result = NativeChatRequest(arrayOf(
            NativeChatMessage("user", arrayOf(NativeContentPart(CONTENT_TEXT, "Find it", null))),
            NativeChatMessage("assistant", arrayOf(NativeContentPart(CONTENT_FUNCTION_CALL,
                """{"callId":"id","name":"lookup","arguments":{"key":"orchard"}}""", null))),
            NativeChatMessage("tool", arrayOf(NativeContentPart(CONTENT_FUNCTION_RESULT,
                """{"callId":"id","result":"secret"}""", null))),
        ), options).toNative()
        assertEquals(listOf(Content.Role.USER, Content.Role.MODEL, Content.Role.USER),
            result.contents.map { it.role })
        assertTrue(result.contents[1].parts.single() is FunctionCallPart)
        assertTrue(result.contents[2].parts.single() is FunctionResponsePart)
        assertEquals("lookup", result.dynamicTools!!.single().name)
    }
}
