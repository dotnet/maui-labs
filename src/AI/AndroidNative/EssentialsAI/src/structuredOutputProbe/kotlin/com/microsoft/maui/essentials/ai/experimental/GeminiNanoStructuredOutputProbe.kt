package com.microsoft.maui.essentials.ai.experimental

import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.prompt.GenerateTypedContentRequest
import com.google.mlkit.genai.prompt.Generation
import com.google.mlkit.genai.prompt.TextPart
import com.google.mlkit.genai.prompt.TypedCandidate
import com.google.mlkit.genai.prompt.TypedCandidate.TypedFinishReason
import com.google.mlkit.genai.prompt.generateContentRequest
import com.google.mlkit.genai.prompt.generateTypedContentRequest
import com.google.mlkit.genai.schema.annotations.Generable
import com.google.mlkit.genai.schema.annotations.Guide

@Generable("Sentiment extracted from the input text")
data class SentimentProbe(
    @param:Guide(description = "The sentiment of the text", enumValues = ["positive", "negative", "neutral"])
    val sentiment: String,
    @param:Guide(description = "Confidence as a percentage", minimum = 0.0, maximum = 100.0)
    val confidence: Int,
)

internal data class StructuredOutputProbeResult(
    val response: SentimentProbe,
    val inputTokens: Int,
)

internal object GeminiNanoStructuredOutputProbe {
    suspend fun generate(prompt: String): StructuredOutputProbeResult {
        val model = Generation.getClient()
        try {
            val request = prepareStructuredOutputProbe(
                prompt, model::isStructuredOutputFeatureAvailable, model::checkStatus,
            )
            val inputTokens = model.countTokens(request).totalTokens
            val response = model.generateContent(request)
            return StructuredOutputProbeResult(
                requireStructuredOutputProbeResponse(response.candidates.firstOrNull()),
                inputTokens,
            )
        } finally {
            model.close()
        }
    }
}

internal suspend fun prepareStructuredOutputProbe(
    prompt: String,
    isStructuredOutputFeatureAvailable: suspend () -> Boolean,
    checkStatus: suspend () -> Int,
): GenerateTypedContentRequest<SentimentProbe> {
    check(isStructuredOutputFeatureAvailable()) {
        "Gemini Nano native structured output is unavailable on this device."
    }
    val status = checkStatus()
    check(status == FeatureStatus.AVAILABLE) {
        "Gemini Nano is not ready for structured inference. Current status: $status."
    }
    val baseRequest = generateContentRequest(TextPart(prompt)) {
        maxOutputTokens = 128
    }
    return generateTypedContentRequest(
        generateContentRequest = baseRequest,
        outputClass = SentimentProbe::class,
    )
}

internal fun requireStructuredOutputProbeResponse(
    candidate: TypedCandidate<SentimentProbe>?,
): SentimentProbe {
    checkNotNull(candidate) { "Gemini Nano returned no structured response candidate." }
    check(candidate.finishReason == TypedFinishReason.STOP) {
        "Gemini Nano structured generation failed. Typed finish reason: ${candidate.finishReason}."
    }
    return checkNotNull(candidate.response) {
        "Gemini Nano returned a null structured response. Typed finish reason: ${candidate.finishReason}."
    }
}
