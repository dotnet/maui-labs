package com.microsoft.maui.essentials.ai.experimental

import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.prompt.GenerateTypedContentRequest
import com.google.mlkit.genai.prompt.Feature
import com.google.mlkit.genai.prompt.Generation
import com.google.mlkit.genai.prompt.TextPart
import com.google.mlkit.genai.prompt.TypedCandidate
import com.google.mlkit.genai.prompt.TypedCandidate.TypedFinishReason
import com.google.mlkit.genai.prompt.generateContentRequest
import com.google.mlkit.genai.prompt.generateTypedContentRequest
import com.google.mlkit.genai.schema.annotations.Generable
import com.google.mlkit.genai.schema.annotations.Guide
import com.microsoft.maui.essentials.ai.modelConfiguration

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
    val modelName: String,
)

internal object GeminiNanoStructuredOutputProbe {
    suspend fun generate(prompt: String, modelId: String? = null): StructuredOutputProbeResult {
        val model = Generation.getClient(modelConfiguration(modelId))
        try {
            val request = prepareStructuredOutputProbe(
                prompt, { Feature.STRUCTURED_OUTPUT in model.getAvailableFeatures() }, model::checkStatus,
            )
            val inputTokens = model.countTokens(request).totalTokens
            val response = model.generateContent(request)
            return StructuredOutputProbeResult(
                requireStructuredOutputProbeResponse(response.candidates.firstOrNull()),
                inputTokens,
                model.getBaseModelName(),
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
    val status = checkStatus()
    check(status == FeatureStatus.AVAILABLE) {
        "Gemini Nano is not ready for structured inference. Current status: $status."
    }
    check(isStructuredOutputFeatureAvailable()) {
        "The selected Gemini Nano model does not support native structured output."
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
