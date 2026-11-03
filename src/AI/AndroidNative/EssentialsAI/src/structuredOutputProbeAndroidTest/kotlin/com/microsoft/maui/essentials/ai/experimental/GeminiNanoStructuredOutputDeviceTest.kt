package com.microsoft.maui.essentials.ai.experimental

import android.util.Log
import androidx.test.ext.junit.rules.ActivityScenarioRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.prompt.Generation
import com.microsoft.maui.essentials.ai.modelConfiguration
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.Rule
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class GeminiNanoStructuredOutputDeviceTest {
    @get:Rule
    val activity = ActivityScenarioRule(StructuredOutputProbeActivity::class.java)

    private val modelId: String
        get() = InstrumentationRegistry.getArguments().getString("modelId")
            ?: "gemini-nano-preview-fast"

    @Test
    fun reportsNativeFeaturesWithoutPreparingOrDownloadingModels() = runBlocking {
        for (profile in listOf("gemini-nano-full", "gemini-nano-preview-fast")) {
            val model = Generation.getClient(modelConfiguration(profile))
            try {
                withTimeout(60_000) {
                    val status = model.checkStatus()
                    val features = if (status == FeatureStatus.AVAILABLE) model.getAvailableFeatures() else emptyList()
                    Log.i("GeminiStructuredProbe",
                        "PROFILE requested=$profile name=${model.getBaseModelName()} status=$status features=$features")
                }
            } finally {
                model.close()
            }
        }
    }

    @Test
    fun positiveTextReturnsTheSdkParsedConstrainedObject() = runBlocking {
        val result = withTimeout(120_000) {
            GeminiNanoStructuredOutputProbe.generate("I love this wonderful holiday. Everything is fantastic!", modelId)
        }
        Log.i("GeminiStructuredProbe", "TYPED requested=$modelId result=$result")
        assertEquals("positive", result.response.sentiment)
        assertTrue(result.response.confidence in 0..100)
        assertTrue(result.inputTokens > 0)
        assertFalse(result.modelName.isBlank())
    }

    @Test
    fun negativeTextReturnsTheSdkParsedConstrainedObject() = runBlocking {
        val result = withTimeout(120_000) {
            GeminiNanoStructuredOutputProbe.generate("I hate this awful holiday. Everything is terrible!", modelId)
        }
        Log.i("GeminiStructuredProbe", "TYPED requested=$modelId result=$result")
        assertEquals("negative", result.response.sentiment)
        assertTrue(result.response.confidence in 0..100)
        assertTrue(result.inputTokens > 0)
        assertFalse(result.modelName.isBlank())
    }

    @Test
    fun differentRuntimeSchemasGenerateNativeObjectsWithoutAnnotatedClasses() = runBlocking {
        withTimeout(180_000) {
            coroutineScope {
                val city = async {
                    RuntimeStructuredOutputProbe.generate(
                        "Extract this trip: the destination is Paris and the number of travelers is 3.",
                        """{"type":"object","additionalProperties":false,"required":["destination","travelers"],
                            "properties":{"destination":{"type":"string","description":"Destination city"},
                            "travelers":{"type":"integer","description":"Number of travelers","minimum":1,"maximum":10}}}""",
                        modelId,
                    )
                }
                val order = async {
                    RuntimeStructuredOutputProbe.generate(
                        "Order AB12 ships to Berlin. Priority is true. Its labels are fragile and urgent.",
                        """{"type":"object","additionalProperties":false,"required":["orderId","shipTo","labels"],
                            "properties":{"orderId":{"type":"string","description":"Order identifier"},
                            "shipTo":{"type":"object","additionalProperties":false,"required":["city","priority"],
                              "properties":{"city":{"type":"string"},"priority":{"type":"boolean"}}},
                            "labels":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"string","enum":["fragile","urgent"]}}}}""",
                        modelId,
                    )
                }
                val trip = city.await()
                val shipment = order.await()
                Log.i("GeminiStructuredProbe", "RUNTIME requested=$modelId trip=$trip order=$shipment")
                val tripJson = JSONObject(trip.json)
                assertEquals("Paris", tripJson.getString("destination"))
                assertEquals(3, tripJson.getInt("travelers"))
                val orderJson = JSONObject(shipment.json)
                assertEquals("AB12", orderJson.getString("orderId"))
                assertEquals("Berlin", orderJson.getJSONObject("shipTo").getString("city"))
                assertTrue(orderJson.getJSONObject("shipTo").getBoolean("priority"))
                val labels = orderJson.getJSONArray("labels")
                assertEquals(setOf("fragile", "urgent"), (0 until labels.length()).map { labels.getString(it) }.toSet())
                for (result in listOf(trip, shipment)) {
                    assertTrue(result.inputTokens > 0)
                    assertFalse(result.modelName.isBlank())
                }
            }

        }
    }

    @Test
    fun runtimeObjectArraysPreserveNestedNamesAndBoundedIntegers() = runBlocking {
        val result = withTimeout(120_000) {
            RuntimeStructuredOutputProbe.generate(
                "Plan exactly two stops in this order: Paris for 2 nights, then Berlin for 3 nights.",
                """{"type":"object","additionalProperties":false,"required":["stops"],
                    "properties":{"stops":{"type":"array","minItems":2,"maxItems":2,
                      "items":{"type":"object","additionalProperties":false,"required":["city","nights"],
                        "properties":{"city":{"type":"string"},
                          "nights":{"type":"integer","minimum":1,"maximum":5}}}}}}""",
                modelId,
            )
        }
        Log.i("GeminiStructuredProbe", "RUNTIME_ARRAY requested=$modelId result=$result")
        val stops = JSONObject(result.json).getJSONArray("stops")
        assertEquals(2, stops.length())
        assertEquals("Paris", stops.getJSONObject(0).getString("city"))
        assertEquals(2, stops.getJSONObject(0).getInt("nights"))
        assertEquals("Berlin", stops.getJSONObject(1).getString("city"))
        assertEquals(3, stops.getJSONObject(1).getInt("nights"))
        assertTrue(result.inputTokens > 0)
        assertFalse(result.modelName.isBlank())
    }
}
