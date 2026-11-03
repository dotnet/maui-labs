package com.microsoft.maui.essentials.ai.experimental

import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.prompt.Feature
import com.google.mlkit.genai.prompt.Generation
import com.google.mlkit.genai.prompt.TextPart
import com.google.mlkit.genai.prompt.TypedCandidate.TypedFinishReason
import com.google.mlkit.genai.prompt.generateContentRequest
import com.google.mlkit.genai.prompt.generateTypedContentRequest
import com.microsoft.maui.essentials.ai.modelConfiguration

internal data class RuntimeStructuredOutputResult(
    val json: String,
    val inputTokens: Int,
    val modelName: String,
)

internal object RuntimeStructuredOutputProbe {
    suspend fun generate(prompt: String, schemaJson: String, modelId: String? = null): RuntimeStructuredOutputResult {
        val schema = RuntimeSchema.parse(schemaJson)
        return RuntimeSchemaProviders.withSchema(schema) {
            val model = Generation.getClient(modelConfiguration(modelId))
            try {
                check(model.checkStatus() == FeatureStatus.AVAILABLE) { "The selected Gemini Nano model is not ready." }
                check(Feature.STRUCTURED_OUTPUT in model.getAvailableFeatures()) {
                    "The selected Gemini Nano model does not support native structured output."
                }
                val base = generateContentRequest(TextPart(prompt)) { maxOutputTokens = 512 }
                val request = generateTypedContentRequest(base, RuntimeObject0::class)
                val inputTokens = model.countTokens(request).totalTokens
                val candidate = checkNotNull(model.generateContent(request).candidates.firstOrNull()) {
                    "Gemini Nano returned no runtime structured candidate."
                }
                check(candidate.finishReason == TypedFinishReason.STOP) {
                    "Gemini Nano runtime structured generation failed: ${candidate.finishReason}."
                }
                val response = checkNotNull(candidate.response) { "Gemini Nano returned no parsed runtime object." }
                RuntimeStructuredOutputResult(schema.project(response), inputTokens, model.getBaseModelName())
            } finally {
                model.close()
            }
        }
    }
}
