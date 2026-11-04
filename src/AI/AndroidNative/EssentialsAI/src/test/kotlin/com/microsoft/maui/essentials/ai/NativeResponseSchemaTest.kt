package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.schema.annotations.Generable
import com.google.mlkit.genai.schema.guided.GenerableProvider
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.util.ServiceLoader

@OptIn(ExperimentalCoroutinesApi::class)
class NativeResponseSchemaTest {
    private val schema = """
        {"type":"object","additionalProperties":false,
         "required":["sentiment","confidence"],
         "properties":{"sentiment":{"type":"string","enum":["positive","negative","neutral"]},
                       "confidence":{"type":"integer","minimum":0,"maximum":100}}}
    """.trimIndent()

    @Test
    fun providersAreDiscoverableWithoutAnnotatedCarrierFields() {
        val providers = ServiceLoader.load(GenerableProvider::class.java).toList()
        assertEquals(16, providers.size)
        NativeResponseProviders.classes.forEach { carrier ->
            assertEquals(1, providers.count { it.targetClass == carrier })
            assertNull(carrier.java.getAnnotation(Generable::class.java))
            assertTrue(carrier.java.declaredFields.isEmpty())
        }
    }

    @Test
    fun runtimeMetadataPreservesNamesTypesAndNativeConstraints() {
        val detail = NativeResponseSchema.parse(schema).detail(0)
        assertEquals(listOf("sentiment", "confidence"), detail.guideDetails.map { it.name })
        assertEquals(String::class, detail.guideDetails[0].type)
        assertArrayEquals(arrayOf("positive", "negative", "neutral"), detail.guideDetails[0].enumValues)
        assertEquals(Long::class, detail.guideDetails[1].type)
        assertEquals(0.0, detail.guideDetails[1].minimum!!, 0.0)
        assertEquals(100.0, detail.guideDetails[1].maximum!!, 0.0)
    }

    @Test
    fun doubleBackedSdkIntegersAreProjectedWithoutChangingTheirValue() {
        val result = NativeResponseSchema.parse(schema).project(mapOf("sentiment" to "positive", "confidence" to 93.0))
        assertEquals(93, JSONObject(result).getInt("confidence"))
        assertTrue(result.contains("\"confidence\":93"))
        assertFalse(result.contains("93.0"))
    }

    @Test
    fun normalTypedSchemasDoNotNeedArtificialIntegerBounds() {
        val compiled = NativeResponseSchema.parse(schema.replace(",\"minimum\":0,\"maximum\":100", ""))
        assertEquals(9007199254740991L,
            JSONObject(compiled.project(mapOf("sentiment" to "positive", "confidence" to 9007199254740991.0)))
                .getLong("confidence"))
        assertTrue(runCatching {
            compiled.project(mapOf("sentiment" to "positive", "confidence" to 9007199254740992.0))
        }.exceptionOrNull() is IllegalArgumentException)
    }

    @Test
    fun projectionRejectsConstraintsTheSdkFixtureValidatorDoesNotEnforce() {
        val compiled = NativeResponseSchema.parse(schema)
        for (invalid in listOf(
            mapOf("sentiment" to "invalid", "confidence" to 93.0),
            mapOf("sentiment" to "positive", "confidence" to 101.0),
            mapOf("sentiment" to "positive", "confidence" to 1.5),
            mapOf("sentiment" to "positive", "confidence" to Double.NaN),
            mapOf("sentiment" to "positive", "confidence" to Double.POSITIVE_INFINITY),
            mapOf("sentiment" to "positive", "confidence" to 9007199254740992.0),
            mapOf("sentiment" to "positive"),
            mapOf("sentiment" to "positive", "confidence" to 93, "extra" to true),
        )) {
            assertTrue(runCatching { compiled.project(invalid) }.exceptionOrNull() is IllegalArgumentException)
        }
    }

    @Test
    fun nestedObjectsAndStringListsProjectUsingDistinctCarrierMetadata() {
        val compiled = NativeResponseSchema.parse("""
            {"type":"object","additionalProperties":false,"required":["address","labels"],
             "properties":{
               "address":{"type":"object","additionalProperties":false,"required":["city","enabled"],
                          "properties":{"city":{"type":"string"},"enabled":{"type":"boolean"}}},
               "labels":{"type":"array","minItems":1,"maxItems":2,
                         "items":{"type":"string","enum":["urgent","fragile"]}}}}
        """.trimIndent())
        assertEquals(NativeResponseObject1::class, compiled.detail(0).guideDetails[0].type)
        assertEquals(listOf("city", "enabled"), compiled.detail(1).guideDetails.map { it.name })
        val values = mapOf("address" to mapOf("city" to "Berlin", "enabled" to true),
            "labels" to listOf("fragile", "urgent"))
        val result = JSONObject(compiled.project(values))
        assertEquals("Berlin", result.getJSONObject("address").getString("city"))
        assertEquals(2, result.getJSONArray("labels").length())
        for (labels in listOf(emptyList(), listOf("unknown"), listOf("urgent", "urgent", "urgent"))) {
            assertTrue(runCatching { compiled.project(values + ("labels" to labels)) }
                .exceptionOrNull() is IllegalArgumentException)
        }
    }

    @Test
    fun nullableNumbersAndLocalReferencesRemainTyped() {
        val compiled = NativeResponseSchema.parse("""
            {"type":"object","required":["value","other"],"additionalProperties":false,
             "properties":{"value":{"${"$"}ref":"#/${"$"}defs/value"},
                           "other":{"type":["string","null"]}},
             "${"$"}defs":{"value":{"type":"number","minimum":1,"maximum":2}}}
        """.trimIndent())
        assertEquals(Double::class, compiled.detail(0).guideDetails[0].type)
        assertTrue(compiled.detail(0).guideDetails[1].nullable)
        val result = JSONObject(compiled.project(mapOf("value" to 1.25, "other" to null)))
        assertEquals(1.25, result.getDouble("value"), 0.0)
        assertTrue(result.isNull("other"))
        assertTrue(runCatching { compiled.project(mapOf("value" to 3.0, "other" to "x")) }
            .exceptionOrNull() is IllegalArgumentException)
    }

    @Test
    fun rootReferencesRetainTheirDefinitionsAndRejectCycles() {
        val json = """{"${"$"}ref":"#/${"$"}defs/result",
            "${"$"}defs":{"result":{"type":"object","properties":{"value":{"type":"string"}},
                "required":["value"],"additionalProperties":false}}}"""
        val schema = NativeResponseSchema.parse(json)
        assertEquals("hello", JSONObject(schema.project(mapOf("value" to "hello"))).getString("value"))
        val cyclic = """{"${"$"}ref":"#/${"$"}defs/result",
            "${"$"}defs":{"result":{"${"$"}ref":"#/${"$"}defs/result"}}}"""
        assertTrue(runCatching { NativeResponseSchema.parse(cyclic) }.exceptionOrNull() is IllegalArgumentException)
    }

    @Test
    fun nullableEnumsPreserveWhetherNullIsActuallyAllowed() {
        val json = """{"type":"object","properties":{"value":{"type":["string","null"],"enum":["yes",null]}},
            "required":["value"],"additionalProperties":false}"""
        val nullable = NativeResponseSchema.parse(json)
        assertTrue(nullable.detail(0).guideDetails.single().nullable)
        assertTrue(JSONObject(nullable.project(mapOf("value" to null))).isNull("value"))
        val nonNull = NativeResponseSchema.parse(json.replace("[\"yes\",null]", "[\"yes\"]"))
        assertFalse(nonNull.detail(0).guideDetails.single().nullable)
        assertTrue(runCatching { nonNull.project(mapOf("value" to null)) }.exceptionOrNull() is IllegalArgumentException)
    }

    @Test
    fun unsupportedSchemasFailBeforeAnyModelOrRegistryUse() {
        val invalid = listOf(
            schema.replace("\"maximum\":100", "\"maximum\":9007199254740993"),
            schema.replace("\"maximum\":100", "\"maximum\":100,\"multipleOf\":2"),
            schema.replace("\"required\":[\"sentiment\",\"confidence\"]", "\"required\":[\"sentiment\"]"),
            schema.replace("\"additionalProperties\":false", "\"additionalProperties\":true"),
            schema.replace("\"type\":\"string\",\"enum\"", "\"type\":\"number\",\"enum\""),
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
            """{"type":"array","items":{"type":"string"}}""",
            """{"type":"object","properties":{"a":{"type":"array","items":{"type":"integer","minimum":0,"maximum":10}}},
                "required":["a"],"additionalProperties":false}""",
            """{"type":"object","properties":{"a":{"${"$"}ref":"#"}},
                "required":["a"],"additionalProperties":false}""",
        )
        invalid.forEach { json -> assertTrue(runCatching { NativeResponseSchema.parse(json) }.exceptionOrNull() is IllegalArgumentException) }
    }

    @Test
    fun objectShapeLimitIsExplicitInsteadOfReusingIncorrectMetadata() {
        var nested = """{"type":"string"}"""
        repeat(17) {
            nested = """{"type":"object","properties":{"child":$nested},"required":["child"],"additionalProperties":false}"""
        }
        val error = runCatching { NativeResponseSchema.parse(nested) }.exceptionOrNull()
        assertTrue(error is IllegalArgumentException)
        assertTrue(error!!.message!!.contains("at most 16 object shapes"))
    }

    @Test
    fun concurrentSchemasRemainIsolatedAcrossSuspensionAndThreadChanges() = runTest {
        val first = NativeResponseSchema.parse(schema)
        val second = NativeResponseSchema.parse(schema.replace("sentiment", "different"))
        val firstEntered = CompletableDeferred<Unit>()
        val releaseFirst = CompletableDeferred<Unit>()
        val secondEntered = CompletableDeferred<Unit>()
        val a = async {
            NativeResponseProviders.withSchema(first) {
                firstEntered.complete(Unit)
                releaseFirst.await()
                withContext(Dispatchers.Default) {
                    assertEquals("sentiment", NativeResponseProvider0().getGenerableDetail().guideDetails[0].name)
                }
            }
        }
        firstEntered.await()
        val b = async {
            NativeResponseProviders.withSchema(second) {
                secondEntered.complete(Unit)
                assertEquals("different", NativeResponseProvider0().getGenerableDetail().guideDetails[0].name)
            }
        }
        runCurrent()
        assertFalse(secondEntered.isCompleted)
        releaseFirst.complete(Unit)
        a.await()
        b.await()
        assertTrue(runCatching { NativeResponseProviders.detail(0) }.exceptionOrNull() is IllegalStateException)
    }

    @Test
    fun cancellationFailureAndCancelledWaitersReleaseSchemaState() = runTest {
        val compiled = NativeResponseSchema.parse(schema)
        val entered = CompletableDeferred<Unit>()
        val running = launch {
            NativeResponseProviders.withSchema(compiled) {
                entered.complete(Unit)
                awaitCancellation()
            }
        }
        entered.await()
        val waiter = launch { NativeResponseProviders.withSchema(compiled) { error("Cancelled waiter entered.") } }
        runCurrent()
        waiter.cancelAndJoin()
        running.cancelAndJoin()
        assertTrue(runCatching { NativeResponseProviders.detail(0) }.exceptionOrNull() is IllegalStateException)
        val failure = IllegalStateException("inference failed")
        assertSame(failure, runCatching { NativeResponseProviders.withSchema(compiled) { throw failure } }.exceptionOrNull())
        NativeResponseProviders.withSchema(compiled) {
            assertEquals("sentiment", NativeResponseProviders.detail(0).guideDetails[0].name)
        }
    }
}
