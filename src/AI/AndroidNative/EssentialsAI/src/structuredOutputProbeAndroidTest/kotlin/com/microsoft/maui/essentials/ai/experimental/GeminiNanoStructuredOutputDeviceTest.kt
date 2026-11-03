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
}
