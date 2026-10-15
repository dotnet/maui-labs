package com.microsoft.maui.essentials.ai.experimental

import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.prompt.TextPart
import com.google.mlkit.genai.prompt.TypedCandidate
import com.google.mlkit.genai.prompt.TypedCandidate.TypedFinishReason
import com.google.mlkit.genai.schema.guided.GenerableProvider
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.util.ServiceLoader

class GeminiNanoStructuredOutputProbeTest {
    @Test
    fun officialGeneratedProviderIsDiscoverableForTheFixedKotlinClass() {
        val provider = ServiceLoader.load(GenerableProvider::class.java)
            .single { it.targetClass == SentimentProbe::class }

        assertTrue(provider is SentimentProbe_GeneratedProvider)
        assertEquals("Sentiment extracted from the input text", provider.getGenerableDetail().description)
    }

    @Test
    fun generatedSchemaPreservesRequiredFieldsTypesAndGuides() {
        val schema = SentimentProbe_GeneratedProvider.generableDetail
        assertEquals(listOf("sentiment", "confidence"), schema.guideDetails.map { it.name })
        val sentiment = schema.guideDetails[0]
        assertEquals(String::class, sentiment.type)
        assertFalse(sentiment.nullable)
        assertFalse(sentiment.isList)
        assertEquals("The sentiment of the text", sentiment.description)
        assertArrayEquals(arrayOf("positive", "negative", "neutral"), sentiment.enumValues)

        val confidence = schema.guideDetails[1]
        assertEquals(Int::class, confidence.type)
        assertFalse(confidence.nullable)
        assertFalse(confidence.isList)
        assertEquals("Confidence as a percentage", confidence.description)
        assertEquals(0.0, confidence.minimum!!, 0.0)
        assertEquals(100.0, confidence.maximum!!, 0.0)
    }

    @Test
    fun typedRequestKeepsPromptUntouchedAndUsesOfficialSchemaInclusion() = runBlocking {
        val calls = mutableListOf<String>()
        val prompt = "I enjoyed the trip."
        val request = prepareStructuredOutputProbe(
            prompt,
            { calls.add("capability"); true },
            { calls.add("readiness"); FeatureStatus.AVAILABLE },
        )

        assertEquals(listOf("capability", "readiness"), calls)
        assertEquals(SentimentProbe::class, request.outputClass)
        assertTrue(request.includeSchemaInPrompt)
        val base = request.generateContentRequest
        assertEquals(prompt, (base.contents.single().parts.single() as TextPart).textString)
        assertEquals(128, base.maxOutputTokens)
        assertNull(base.systemInstruction)
        assertNull(base.promptPrefix)
        assertFalse(base.enableThinking)
    }

    @Test
    fun unavailableStructuredOutputFailsBeforeReadinessOrRequestCreation() = runBlocking {
        val error = runCatching {
            prepareStructuredOutputProbe("input", { false }, { error("Must not query readiness.") })
        }.exceptionOrNull()

        assertTrue(error is IllegalStateException)
        assertEquals("Gemini Nano native structured output is unavailable on this device.", error!!.message)
    }

    @Test
    fun everyNonReadyStatusFailsWithoutDownloadOrInference() = runBlocking {
        for (status in listOf(FeatureStatus.UNAVAILABLE, FeatureStatus.DOWNLOADABLE, FeatureStatus.DOWNLOADING)) {
            val error = runCatching {
                prepareStructuredOutputProbe("input", { true }, { status })
            }.exceptionOrNull()
            assertTrue(error is IllegalStateException)
            assertEquals(
                "Gemini Nano is not ready for structured inference. Current status: $status.",
                error!!.message,
            )
        }
    }

    @Test
    fun capabilityAndReadinessErrorsAreNotReplacedWithFallbackResults() = runBlocking {
        val failure = IllegalStateException("native failure")
        assertSame(failure, runCatching {
            prepareStructuredOutputProbe("input", { throw failure }, { FeatureStatus.AVAILABLE })
        }.exceptionOrNull())
        assertSame(failure, runCatching {
            prepareStructuredOutputProbe("input", { true }, { throw failure })
        }.exceptionOrNull())
    }

    @Test
    fun missingCandidateFailsExplicitly() {
        val error = runCatching { requireStructuredOutputProbeResponse(null) }.exceptionOrNull()
        assertTrue(error is IllegalStateException)
        assertEquals("Gemini Nano returned no structured response candidate.", error!!.message)
    }

    @Test
    fun stopWithNullParsedResponseIsNotSuccess() {
        val error = runCatching {
            requireStructuredOutputProbeResponse(candidate(null, TypedFinishReason.STOP))
        }.exceptionOrNull()

        assertTrue(error is IllegalStateException)
        assertEquals("Gemini Nano returned a null structured response. Typed finish reason: 0.", error!!.message)
    }

    @Test
    fun stopReturnsTheSdkParsedObjectWithoutReinterpretingIt() {
        val response = SentimentProbe("positive", 90)
        assertSame(response, requireStructuredOutputProbeResponse(candidate(response, TypedFinishReason.STOP)))
    }

    @Test
    fun incompleteOtherUnknownAndNullFinishReasonsFailEvenWithAParsedObject() {
        for (reason in listOf(TypedFinishReason.MAX_TOKENS, TypedFinishReason.OTHER, Int.MAX_VALUE, null)) {
            for (response in listOf(SentimentProbe("positive", 90), null)) {
                val error = runCatching {
                    requireStructuredOutputProbeResponse(candidate(response, reason))
                }.exceptionOrNull()
                assertTrue(error is IllegalStateException)
                assertEquals(
                    "Gemini Nano structured generation failed. Typed finish reason: $reason.",
                    error!!.message,
                )
            }
        }
    }

    // The SDK's Kotlin-private constructor is used only to create deterministic response fixtures.
    @Suppress("UNCHECKED_CAST")
    private fun candidate(response: SentimentProbe?, reason: Int?): TypedCandidate<SentimentProbe> =
        TypedCandidate::class.java.declaredConstructors.single { it.parameterCount == 3 }
            .newInstance(response, reason, null) as TypedCandidate<SentimentProbe>
}
