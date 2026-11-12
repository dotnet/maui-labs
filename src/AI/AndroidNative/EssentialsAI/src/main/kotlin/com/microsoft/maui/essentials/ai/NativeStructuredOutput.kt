@file:JvmName("NativeStructuredOutput")

package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.prompt.GenerateContentRequest
import com.google.mlkit.genai.prompt.GenerativeModel
import com.google.mlkit.genai.prompt.TypedCandidate.TypedFinishReason
import com.google.mlkit.genai.prompt.generateTypedContentRequest

fun validateSchema(json: String) {
    NativeResponseSchema.parse(json)
}

internal suspend fun generateStructuredResponse(
    model: GenerativeModel,
    request: GenerateContentRequest,
    schema: NativeResponseSchema,
    modelName: String,
): NativeChatResponse {
    val typed = generateTypedContentRequest(request, NativeResponseObject0::class)
    val tokens = model.countTokens(typed).totalTokens
    val candidate = checkNotNull(model.generateContent(typed).candidates.firstOrNull()) {
        "Gemini Nano returned no structured response candidate."
    }
    check(candidate.finishReason == TypedFinishReason.STOP || candidate.finishReason == TypedFinishReason.MAX_TOKENS) {
        "Gemini Nano structured generation failed: ${candidate.finishReason}."
    }
    val response = checkNotNull(candidate.response) { "Gemini Nano returned no parsed structured response." }
    return NativeChatResponse(schema.project(response), emptyArray(),
        checkNotNull(candidate.finishReason), modelName, tokens)
}
