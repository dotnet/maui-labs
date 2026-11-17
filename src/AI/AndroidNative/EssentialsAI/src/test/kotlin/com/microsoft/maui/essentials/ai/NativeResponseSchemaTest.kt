package com.microsoft.maui.essentials.ai

import com.google.android.gms.internal.mlkit_genai_prompt.zzaap
import com.google.android.gms.internal.mlkit_genai_prompt.zzacq
import com.google.android.gms.internal.mlkit_genai_prompt.zzip
import com.google.mlkit.genai.prompt.GenerateContentRequest
import com.google.mlkit.genai.prompt.TextPart
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class NativeResponseSchemaTest {
    private val schema = """
        {"type":"object","additionalProperties":false,
         "required":["sentiment","confidence"],
         "properties":{"sentiment":{"type":"string","enum":["positive","negative","neutral"],"description":"Overall tone"},
                       "confidence":{"type":"integer","minimum":0,"maximum":100}}}
    """.trimIndent()

    // MEAI GetResponseAsync<T> wraps non-object roots in {"data": ...} and keeps the nested "$schema".
    private fun meaiWrapper(data: String) =
        """{"type":"object","properties":{"data":$data},"additionalProperties":false,"required":["data"]}"""

    private fun native(json: String): JSONObject = NativeResponseSchema.parse(json).native().zzv()

    private fun textMessage(role: String, vararg text: String) =
        NativeChatMessage(role, text.map { NativeContentPart(CONTENT_TEXT, it, null) }.toTypedArray())

    @Test
    fun nativeSchemaCarriesTypesDescriptionsEnumsBoundsAndRequiredProperties() {
        val native = native(schema)
        val properties = native.getJSONObject("properties")
        assertEquals(listOf("sentiment", "confidence"),
            native.getJSONArray("required").let { array -> (0 until array.length()).map(array::getString) })
        assertEquals("Overall tone", properties.getJSONObject("sentiment").getString("description"))
        assertEquals(3, properties.getJSONObject("sentiment").getJSONArray("enum").length())
        assertEquals(0.0, properties.getJSONObject("confidence").getDouble("minimum"), 0.0)
        assertEquals(100.0, properties.getJSONObject("confidence").getDouble("maximum"), 0.0)
        assertNotEquals(properties.getJSONObject("sentiment").get("type"), properties.getJSONObject("confidence").get("type"))
    }

    @Test
    fun dataIngestionCollectionSchemasMapDirectlyWithoutWrappers() {
        val summaries = meaiWrapper("""{"${'$'}schema":"https://json-schema.org/draft/2020-12/schema","type":"array","items":{"type":"string"}}""")
        val keywords = meaiWrapper("""{"${'$'}schema":"https://json-schema.org/draft/2020-12/schema","type":"array","items":{"type":"array","items":{"type":"string"}}}""")

        val nested = native(keywords).getJSONObject("properties").getJSONObject("data")
        assertTrue(nested.getJSONObject("items").has("items"))
        assertFalse(nested.getJSONObject("items").getJSONObject("items").has("properties"))
        assertFalse(native(summaries).getJSONObject("properties").getJSONObject("data").getJSONObject("items").has("items"))
    }

    @Test
    fun nullableItemsPropertiesAndEnumsStayNullable() {
        val json = meaiWrapper("""{"type":["array","null"],"items":{"type":["array","null"],"items":{"type":["string","null"],"enum":["a",null]}}}""")
        val data = native(json).getJSONObject("properties").getJSONObject("data")
        assertTrue(data.getBoolean("nullable"))
        assertTrue(data.getJSONObject("items").getBoolean("nullable"))
        assertTrue(data.getJSONObject("items").getJSONObject("items").getBoolean("nullable"))
        assertFalse(native("""{"type":"object","properties":{"v":{"type":"string"}},"required":["v"]}""")
            .getJSONObject("properties").getJSONObject("v").optBoolean("nullable"))
    }

    @Test
    fun deepObjectGraphsMapNatively() {
        var json = """{"type":"string"}"""
        val leafType = native("""{"type":"object","properties":{"v":{"type":"string"}}}""")
            .getJSONObject("properties").getJSONObject("v").get("type")
        repeat(40) { level ->
            json = """{"type":"object","properties":{"n$level":$json,"id":{"type":"integer"}},"required":["n$level","id"],"additionalProperties":false}"""
        }
        var native = native(json)
        repeat(40) { level -> native = native.getJSONObject("properties").getJSONObject("n${39 - level}") }
        assertEquals(leafType, native.get("type"))
    }

    @Test
    fun arrayBoundsAreNative() {
        val array = meaiWrapper("""{"type":"array","items":{"type":"integer"},"minItems":1,"maxItems":3}""")
        val data = native(array).getJSONObject("properties").getJSONObject("data")
        assertEquals(1, data.getInt("minItems"))
        assertEquals(3, data.getInt("maxItems"))
    }

    // AICore only generates object roots; OpenAI structured outputs have the same rule.
    @Test
    fun rootMustBeANonNullObject() {
        listOf(
            """{"type":"array","items":{"type":"string"}}""",
            """{"type":"string","enum":["red","green"]}""",
            """{"type":"integer","minimum":1}""",
            """{"type":"boolean"}""",
            """{"type":["object","null"],"properties":{"v":{"type":"string"}}}""",
        ).forEach { json ->
            assertTrue(json, assertThrows(json, IllegalArgumentException::class.java) { NativeResponseSchema.parse(json) }
                .message!!.contains("non-null object"))
        }
    }

    @Test
    fun localReferencesResolveWithOverridesAndRecursionIsRejected() {
        val json = """{"type":"object","${'$'}defs":{"point":{"type":"object","properties":{"x":{"type":"number"}},"required":["x"]}},
            "properties":{"a":{"${'$'}ref":"#/${'$'}defs/point","description":"first"},"b":{"${'$'}ref":"#/${'$'}defs/point"}},"required":["a","b"]}"""
        val native = native(json).getJSONObject("properties")
        assertEquals("first", native.getJSONObject("a").getString("description"))
        assertTrue(native.getJSONObject("b").getJSONObject("properties").has("x"))

        val recursive = """{"type":"object","properties":{"next":{"${'$'}ref":"#"}}}"""
        assertTrue(assertThrows(IllegalArgumentException::class.java) { NativeResponseSchema.parse(recursive) }
            .message!!.contains("recursive"))
    }

    @Test
    fun unsupportedSchemasFailBeforeInference() {
        listOf(
            """{"type":"object","properties":{"v":{"anyOf":[{"type":"string"},{"type":"integer"}]}}}""",
            """{"type":"object","properties":{"v":{"type":["string","integer"]}}}""",
            """{"type":"object","properties":{"v":{"type":"string","minLength":2}}}""",
            """{"type":"object","properties":{"v":{"type":"integer","exclusiveMinimum":2}}}""",
            """{"type":"object","properties":{"v":{"type":"integer","enum":[1,2]}}}""",
            """{"type":"object","properties":{"v":{}}}""",
            """{"type":"object","additionalProperties":{"type":"integer"}}""",
            """{"type":"object","properties":{}}""",
            """{"type":"object","properties":{"v":{"type":"string"}},"required":["w"]}""",
            """{"type":"array"}""",
            """{"type":"object","properties":{"v":{"${'$'}ref":"https://example.com/schema"}}}""",
            """{"type":"object","properties":{"v":{"type":"array","items":{"type":"string"},"maxItems":-1}}}""",
            "not json",
        ).forEach { json ->
            assertThrows(json, IllegalArgumentException::class.java) {
                NativeChatOptions(null, null, null, null, false, null).responseSchemaJson = json
            }
        }
    }

    @Test
    fun patternAndFormatArePassedToAicore() {
        val json = """{"type":"object","properties":{"v":{"type":"string","pattern":"^[a-z]+${'$'}","format":"custom"}}}"""
        val v = native(json).getJSONObject("properties").getJSONObject("v")
        assertEquals("^[a-z]+${'$'}", v.getString("pattern"))
        assertEquals("custom", v.getString("format"))
    }

    @Test
    fun onlyACompletedToolTurnCanLeadToTheStructuredAnswer() {
        checkToolTurnFinished(com.google.mlkit.genai.prompt.Candidate.FinishReason.STOP)
        val truncated = assertThrows(IllegalStateException::class.java) {
            checkToolTurnFinished(com.google.mlkit.genai.prompt.Candidate.FinishReason.MAX_TOKENS)
        }
        assertTrue(truncated.message!!.contains("MaxOutputTokens"))
        assertThrows(IllegalStateException::class.java) {
            checkToolTurnFinished(com.google.mlkit.genai.prompt.Candidate.FinishReason.OTHER)
        }
        assertThrows(IllegalStateException::class.java) { checkToolTurnFinished(FINISH_UNSPECIFIED) }
    }

    @Test
    fun numericBoundsMapToNativeDoubles() {
        val decimal = """{"type":"object","properties":{"n":{"type":"number","minimum":0.1,"maximum":0.3}},"required":["n"]}"""
        val n = native(decimal).getJSONObject("properties").getJSONObject("n")
        assertEquals(0.1, n.getDouble("minimum"), 0.0)
        assertEquals(0.3, n.getDouble("maximum"), 0.0)
        NativeResponseSchema.parse("""{"type":"object","properties":{"n":{"type":"integer","maximum":9223372036854775807}},"required":["n"]}""")
        assertThrows(IllegalArgumentException::class.java) {
            NativeResponseSchema.parse("""{"type":"object","properties":{"n":{"type":"number","minimum":2,"maximum":1}}}""")
        }
    }

    @Test
    fun nestingIsLimited() {
        fun nested(levels: Int): String {
            var json = """{"type":"string"}"""
            repeat(levels) { json = """{"type":"object","properties":{"v":$json},"required":["v"]}""" }
            return json
        }
        NativeResponseSchema.parse(nested(NativeResponseSchema.MAX_DEPTH - 1))
        assertThrows(IllegalArgumentException::class.java) { NativeResponseSchema.parse(nested(NativeResponseSchema.MAX_DEPTH)) }
    }

    @Test
    fun responseSchemaSetterValidatesAndKeepsTheParsedSchema() {
        val options = NativeChatOptions(null, null, null, null, false, null)
        options.responseSchemaJson = schema
        assertNotNull(options.responseSchema)
        options.responseSchemaJson = null
        assertNull(options.responseSchema)
        assertThrows(IllegalArgumentException::class.java) { options.responseSchemaJson = """{"type":"object"}""" }
    }

    @Test
    fun nullableEnumsOnlyAllowNullWhenListed() {
        val excluded = """{"type":"object","properties":{"c":{"type":["string","null"],"enum":["red"]}},"required":["c"]}"""
        assertFalse(native(excluded).getJSONObject("properties").getJSONObject("c").optBoolean("nullable"))

        val listed = """{"type":"object","properties":{"c":{"type":["string","null"],"enum":["red",null]}},"required":["c"]}"""
        assertTrue(native(listed).getJSONObject("properties").getJSONObject("c").getBoolean("nullable"))
    }

    @Test
    fun schemaPromptMatchesMlKitPlacementOnTheLastTextPart() {
        val options = NativeChatOptions(null, null, null, null, false, null).apply { responseSchemaJson = schema }
        val nativeSchema = NativeResponseSchema.parse(schema).native()
        val messages = arrayOf(textMessage("system", "Be terse."), textMessage("user", "first", "second"))
        val request = NativeChatRequest(messages, options).toNative(nativeSchema)

        val texts = request.contents.flatMap { it.parts }.map { (it as TextPart).textString }
        assertEquals(listOf("first", zzaap.zze("second", nativeSchema)), texts)
        assertTrue(texts.last().startsWith("second"))
        assertTrue(texts.last().contains(nativeSchema.zzv().toString()))
        assertEquals("Be terse.", request.systemInstruction!!.textString)
        assertEquals(listOf("first", "second"), NativeChatRequest(messages, options).toNative()
            .contents.flatMap { it.parts }.map { (it as TextPart).textString })
    }

    @Test
    fun responseSchemaIsAppliedToTheSingleAicoreSchemaField() {
        val nativeSchema = NativeResponseSchema.parse(schema).native()
        val built = zzaap.zzg(zzacq(GenerateContentRequest.Builder(TextPart("hi")).build(), null, false), null, null)
        val field = generateSequence<Class<*>>(built.javaClass) { it.superclass }
            .flatMap { it.declaredFields.asSequence() }
            .single { it.type == zzip::class.java }
            .apply { isAccessible = true }
        assertNull(field.get(built))
        assertSame(built, applyResponseSchema(built, nativeSchema))
        assertSame(nativeSchema, field.get(built))
    }
}
