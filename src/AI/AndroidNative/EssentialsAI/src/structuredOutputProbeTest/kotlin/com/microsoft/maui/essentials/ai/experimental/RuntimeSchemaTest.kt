package com.microsoft.maui.essentials.ai.experimental

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
class RuntimeSchemaTest {
    private val schema = """
        {"type":"object","additionalProperties":false,
         "required":["sentiment","confidence"],
         "properties":{"sentiment":{"type":"string","enum":["positive","negative","neutral"]},
                       "confidence":{"type":"integer","minimum":0,"maximum":100}}}
    """.trimIndent()

    @Test
    fun handwrittenProvidersCoexistWithTheKspProviderWithoutAnnotatedCarrierFields() {
        val providers = ServiceLoader.load(GenerableProvider::class.java).toList()
        assertEquals(1, providers.count { it.targetClass == SentimentProbe::class })
        RuntimeSchemaProviders.classes.forEach { carrier ->
            assertEquals(1, providers.count { it.targetClass == carrier })
            assertNull(carrier.java.getAnnotation(Generable::class.java))
            assertTrue(carrier.java.declaredFields.isEmpty())
        }
    }

    @Test
    fun runtimeMetadataPreservesNamesTypesAndNativeConstraints() {
        val detail = RuntimeSchema.parse(schema).detail(0)
        assertEquals(listOf("sentiment", "confidence"), detail.guideDetails.map { it.name })
        assertEquals(String::class, detail.guideDetails[0].type)
        assertArrayEquals(arrayOf("positive", "negative", "neutral"), detail.guideDetails[0].enumValues)
        assertEquals(Int::class, detail.guideDetails[1].type)
        assertEquals(0.0, detail.guideDetails[1].minimum!!, 0.0)
        assertEquals(100.0, detail.guideDetails[1].maximum!!, 0.0)
    }

    @Test
    fun doubleBackedSdkIntegersAreProjectedWithoutChangingTheirValue() {
        val result = RuntimeSchema.parse(schema).project(mapOf("sentiment" to "positive", "confidence" to 93.0))
        assertEquals(93, JSONObject(result).getInt("confidence"))
        assertTrue(result.contains("\"confidence\":93"))
        assertFalse(result.contains("93.0"))
    }

    @Test
    fun projectionRejectsConstraintsTheSdkFixtureValidatorDoesNotEnforce() {
        val compiled = RuntimeSchema.parse(schema)
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
        val compiled = RuntimeSchema.parse("""
            {"type":"object","additionalProperties":false,"required":["address","labels"],
             "properties":{
               "address":{"type":"object","additionalProperties":false,"required":["city","enabled"],
                          "properties":{"city":{"type":"string"},"enabled":{"type":"boolean"}}},
               "labels":{"type":"array","minItems":1,"maxItems":2,
                         "items":{"type":"string","enum":["urgent","fragile"]}}}}
        """.trimIndent())
        assertEquals(RuntimeObject1::class, compiled.detail(0).guideDetails[0].type)
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
    fun unsupportedSchemasFailBeforeAnyModelOrRegistryUse() {
        val invalid = listOf(
            schema.replace("\"maximum\":100", "\"maximum\":9007199254740993"),
            schema.replace(",\"maximum\":100", ""),
            schema.replace("\"maximum\":100", "\"maximum\":100,\"multipleOf\":2"),
            schema.replace("\"required\":[\"sentiment\",\"confidence\"]", "\"required\":[\"sentiment\"]"),
            schema.replace("\"additionalProperties\":false", "\"additionalProperties\":true"),
            schema.replace("\"type\":\"string\",\"enum\"", "\"type\":\"number\",\"enum\""),
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
            """{"type":"array","items":{"type":"string"}}""",
            """{"type":"object","properties":{"a":{"type":"array","items":{"type":"integer","minimum":0,"maximum":10}}},
                "required":["a"],"additionalProperties":false}""",
        )
        invalid.forEach { json -> assertNotNull(runCatching { RuntimeSchema.parse(json) }.exceptionOrNull()) }
    }

    @Test
    fun objectShapeLimitIsExplicitInsteadOfReusingIncorrectMetadata() {
        var nested = """{"type":"string"}"""
        repeat(5) {
            nested = """{"type":"object","properties":{"child":$nested},"required":["child"],"additionalProperties":false}"""
        }
        val error = runCatching { RuntimeSchema.parse(nested) }.exceptionOrNull()
        assertTrue(error is IllegalArgumentException)
        assertTrue(error!!.message!!.contains("at most 4 object shapes"))
    }

    @Test
    fun concurrentSchemasRemainIsolatedAcrossSuspensionAndThreadChanges() = runTest {
        val first = RuntimeSchema.parse(schema)
        val second = RuntimeSchema.parse(schema.replace("sentiment", "different"))
        val firstEntered = CompletableDeferred<Unit>()
        val releaseFirst = CompletableDeferred<Unit>()
        val secondEntered = CompletableDeferred<Unit>()
        val a = async {
            RuntimeSchemaProviders.withSchema(first) {
                firstEntered.complete(Unit)
                releaseFirst.await()
                withContext(Dispatchers.Default) {
                    assertEquals("sentiment", RuntimeObjectProvider0().getGenerableDetail().guideDetails[0].name)
                }
            }
        }
        firstEntered.await()
        val b = async {
            RuntimeSchemaProviders.withSchema(second) {
                secondEntered.complete(Unit)
                assertEquals("different", RuntimeObjectProvider0().getGenerableDetail().guideDetails[0].name)
            }
        }
        runCurrent()
        assertFalse(secondEntered.isCompleted)
        releaseFirst.complete(Unit)
        a.await()
        b.await()
        assertTrue(runCatching { RuntimeSchemaProviders.detail(0) }.exceptionOrNull() is IllegalStateException)
    }

    @Test
    fun cancellationFailureAndCancelledWaitersReleaseSchemaState() = runTest {
        val compiled = RuntimeSchema.parse(schema)
        val entered = CompletableDeferred<Unit>()
        val running = launch {
            RuntimeSchemaProviders.withSchema(compiled) {
                entered.complete(Unit)
                awaitCancellation()
            }
        }
        entered.await()
        val waiter = launch { RuntimeSchemaProviders.withSchema(compiled) { error("Cancelled waiter entered.") } }
        runCurrent()
        waiter.cancelAndJoin()
        running.cancelAndJoin()
        assertTrue(runCatching { RuntimeSchemaProviders.detail(0) }.exceptionOrNull() is IllegalStateException)
        val failure = IllegalStateException("inference failed")
        val observed = runCatching { RuntimeSchemaProviders.withSchema(compiled) { throw failure } }.exceptionOrNull()
        assertSame(failure, observed)
        RuntimeSchemaProviders.withSchema(compiled) {
            assertEquals("sentiment", RuntimeSchemaProviders.detail(0).guideDetails[0].name)
        }
    }
}
