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

    private fun assertRejected(json: String, output: String) {
        assertThrows(output, IllegalArgumentException::class.java) { NativeResponseSchema.parse(json).validate(output) }
    }

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

        NativeResponseSchema.parse(summaries).validate("""{"data":["one","two"]}""")
        NativeResponseSchema.parse(keywords).validate("""{"data":[["a","b"],[],["c"]]}""")
        assertRejected(keywords, """{"data":["a","b"]}""")
        assertRejected(summaries, """{"data":[["a"]]}""")
    }

    @Test
    fun nullableItemsPropertiesAndEnumsStayNullable() {
        val json = meaiWrapper("""{"type":["array","null"],"items":{"type":["array","null"],"items":{"type":["string","null"],"enum":["a",null]}}}""")
        val data = native(json).getJSONObject("properties").getJSONObject("data")
        assertTrue(data.getBoolean("nullable"))
        assertTrue(data.getJSONObject("items").getBoolean("nullable"))
        assertTrue(data.getJSONObject("items").getJSONObject("items").getBoolean("nullable"))

        val compiled = NativeResponseSchema.parse(json)
        compiled.validate("""{"data":null}""")
        compiled.validate("""{"data":[null,["a",null]]}""")
        assertRejected(json, """{"data":[["b"]]}""")
        assertRejected("""{"type":"object","properties":{"v":{"type":"string"}},"required":["v"]}""", """{"v":null}""")
    }

    @Test
    fun deepObjectGraphsHaveNoShapeLimit() {
        var json = """{"type":"string"}"""
        val leafType = native("""{"type":"object","properties":{"v":{"type":"string"}}}""")
            .getJSONObject("properties").getJSONObject("v").get("type")
        var output = "\"leaf\""
        repeat(40) { level ->
            json = """{"type":"object","properties":{"n$level":$json,"id":{"type":"integer"}},"required":["n$level","id"],"additionalProperties":false}"""
            output = """{"n$level":$output,"id":$level}"""
        }
        var native = native(json)
        repeat(40) { level -> native = native.getJSONObject("properties").getJSONObject("n${39 - level}") }
        assertEquals(leafType, native.get("type"))
        NativeResponseSchema.parse(json).validate(output)
    }

    @Test
    fun arrayBoundsAreNative() {
        val array = meaiWrapper("""{"type":"array","items":{"type":"integer"},"minItems":1,"maxItems":3}""")
        val data = native(array).getJSONObject("properties").getJSONObject("data")
        assertEquals(1, data.getInt("minItems"))
        assertEquals(3, data.getInt("maxItems"))
        NativeResponseSchema.parse(array).validate("""{"data":[1,2]}""")
        assertRejected(array, """{"data":[]}""")
        assertRejected(array, """{"data":[1,2,3,4]}""")
    }

    // AICore returns every non-object root as {"wrapper": value} (verified on Nano V3 and V4).
    @Test
    fun nonObjectRootsAreUnwrappedFromTheAicoreWrapper() {
        val array = NativeResponseSchema.parse("""{"type":"array","items":{"type":"string","enum":["red","green"]},"maxItems":2}""")
        assertTrue(array.wrapsRoot)
        assertEquals("array", native("""{"type":"array","items":{"type":"string"}}""").getString("type"))
        assertEquals("""["red","green"]""", array.validate("""{"wrapper": ["red", "green"]}"""))
        assertThrows(IllegalArgumentException::class.java) { array.validate("""["red","green"]""") }
        assertThrows(IllegalArgumentException::class.java) { array.validate("""{"wrapper":["blue"]}""") }
        assertThrows(IllegalArgumentException::class.java) { array.validate("""{"wrapper":["red"],"extra":1}""") }

        assertEquals("\"Paris \\\"Lumi\u00e8re\\\" \\n\"",
            NativeResponseSchema.parse("""{"type":"string"}""").validate("""{"wrapper":"Paris \"Lumi\u00e8re\" \n"}"""))
        assertEquals("8", NativeResponseSchema.parse("""{"type":"integer","minimum":1}""").validate("""{"wrapper":8}"""))
        assertEquals("false", NativeResponseSchema.parse("""{"type":"boolean"}""").validate("""{"wrapper":false}"""))
        assertFalse(NativeResponseSchema.parse(schema).wrapsRoot)
        assertTrue(assertThrows(IllegalArgumentException::class.java) {
            NativeResponseSchema.parse("""{"type":["string","null"]}""")
        }.message!!.contains("nullable"))
    }

    @Test
    fun localReferencesResolveWithOverridesAndRecursionIsRejected() {
        val json = """{"type":"object","${'$'}defs":{"point":{"type":"object","properties":{"x":{"type":"number"}},"required":["x"]}},
            "properties":{"a":{"${'$'}ref":"#/${'$'}defs/point","description":"first"},"b":{"${'$'}ref":"#/${'$'}defs/point"}},"required":["a","b"]}"""
        val native = native(json).getJSONObject("properties")
        assertEquals("first", native.getJSONObject("a").getString("description"))
        assertTrue(native.getJSONObject("b").getJSONObject("properties").has("x"))
        NativeResponseSchema.parse(json).validate("""{"a":{"x":1.5},"b":{"x":-2}}""")

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
            """{"type":"object","properties":{"v":{"type":"string","pattern":"("}}}""",
            """{"type":"object","properties":{"v":{}}}""",
            """{"type":"object","additionalProperties":{"type":"integer"}}""",
            """{"type":"object","properties":{}}""",
            """{"type":"object","properties":{"v":{"type":"string"}},"required":["w"]}""",
            """{"type":"array"}""",
            """{"type":"object","properties":{"v":{"${'$'}ref":"https://example.com/schema"}}}""",
            """{"type":"object","properties":{"v":{"type":"array","items":{"type":"string"},"maxItems":-1}}}""",
            "not json",
        ).forEach { json ->
            assertThrows(json, IllegalArgumentException::class.java) { validateSchema(json) }
        }
    }

    @Test
    fun validationEnforcesTheSchemaOnGeneratedText() {
        val compiled = NativeResponseSchema.parse(schema)
        compiled.validate("""  {"sentiment":"positive","confidence":90}  """)
        compiled.validate("""{"sentiment":"neutral","confidence":5.0}""")
        listOf(
            """{"sentiment":"angry","confidence":90}""",
            """{"sentiment":"positive","confidence":101}""",
            """{"sentiment":"positive","confidence":1.5}""",
            """{"sentiment":"positive"}""",
            """{"sentiment":"positive","confidence":90,"extra":true}""",
            """{"sentiment":1,"confidence":90}""",
            """["positive",90]""",
        ).forEach { assertRejected(schema, it) }

        val open = """{"type":"object","properties":{"v":{"type":"string","pattern":"^[a-z]+${'$'}","format":"custom"}}}"""
        NativeResponseSchema.parse(open).validate("""{"v":"abc","other":1}""")
        NativeResponseSchema.parse(open).validate("""{}""")
        assertRejected(open, """{"v":"ABC"}""")
        assertEquals("custom", native(open).getJSONObject("properties").getJSONObject("v").getString("format"))
    }

    @Test
    fun generatedTextMustBeStrictJson() {
        val json = """{"type":"object","properties":{"v":{"type":"string"}},"required":["v"]}"""
        NativeResponseSchema.parse(json).validate("""{"v":"\u00e9\n\"quoted\""}""")
        listOf(
            "```json\n{\"v\":\"x\"}\n```",
            "{'v':'x'}",
            "{v:\"x\"}",
            "{\"v\":\"x\",}",
            "{\"v\":\"x\"} trailing",
            "{\"v\":\"x\",\"v\":\"y\"}",
            "{\"v\":\"x\" /* comment */}",
            "{\"v\":\"line\nbreak\"}",
            "{\"v\":\"x\"",
            "",
        ).forEach { assertRejected(json, it) }
        val number = """{"type":"object","properties":{"n":{"type":"number"}},"required":["n"]}"""
        assertRejected(number, """{"n":01}""")
        assertRejected(number, """{"n":NaN}""")
        assertRejected(number, """{"n":1.}""")
        assertRejected(json, """{"v":"\u+041"}""")
        assertRejected(json, """{"v":"\u-001"}""")
        assertRejected(json, """{"v":"\u 041"}""")
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
    fun numericBoundsAreValidatedExactly() {
        val decimal = """{"type":"object","properties":{"n":{"type":"number","minimum":0.1,"maximum":0.3}},"required":["n"]}"""
        NativeResponseSchema.parse(decimal).validate("""{"n":0.1}""")
        NativeResponseSchema.parse(decimal).validate("""{"n":0.3}""")
        assertRejected(decimal, """{"n":0.09999}""")
        assertRejected(decimal, """{"n":0.30001}""")
        assertEquals(0.1, native(decimal).getJSONObject("properties").getJSONObject("n").getDouble("minimum"), 0.0)

        val long = """{"type":"object","properties":{"n":{"type":"integer","maximum":9223372036854775807}},"required":["n"]}"""
        NativeResponseSchema.parse(long).validate("""{"n":9223372036854775807}""")
        assertRejected(long, """{"n":9223372036854775808}""")
    }

    @Test
    fun nullableEnumsOnlyAllowNullWhenListed() {
        val excluded = """{"type":"object","properties":{"c":{"type":["string","null"],"enum":["red"]}},"required":["c"]}"""
        assertRejected(excluded, """{"c":null}""")
        NativeResponseSchema.parse(excluded).validate("""{"c":"red"}""")
        assertFalse(native(excluded).getJSONObject("properties").getJSONObject("c").optBoolean("nullable"))

        val listed = """{"type":"object","properties":{"c":{"type":["string","null"],"enum":["red",null]}},"required":["c"]}"""
        NativeResponseSchema.parse(listed).validate("""{"c":null}""")
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
