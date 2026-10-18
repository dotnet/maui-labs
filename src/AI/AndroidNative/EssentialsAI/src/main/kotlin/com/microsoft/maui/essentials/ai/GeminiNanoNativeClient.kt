package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.common.GenAiException
import com.google.mlkit.genai.common.StreamingCallback
import com.google.mlkit.genai.prompt.Content
import com.google.mlkit.genai.prompt.GenerateContentRequest
import com.google.mlkit.genai.prompt.GenerateContentResponse
import com.google.mlkit.genai.prompt.Generation
import com.google.mlkit.genai.prompt.ImagePart
import com.google.mlkit.genai.prompt.SystemInstruction
import com.google.mlkit.genai.prompt.TextPart
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.runBlocking

class GeminiNanoNativeClient {
    private val operations = NativeChatOperations(
        createModel = { Generation.getClient() },
        closeModel = { it.close() },
        infer = { model, request, streaming, callback ->
            val nativeRequest = request.toNative(
                model::isSystemPromptAvailable,
                model::isThinkingModeAvailable,
            )
            val status = model.checkStatus()
            if (status != FeatureStatus.AVAILABLE) {
                val preparation = when (status) {
                    FeatureStatus.DOWNLOADABLE, FeatureStatus.DOWNLOADING ->
                        " Download Gemini Nano through AICore before retrying."
                    else -> " This device must support Gemini Nano and have the latest AICore configuration."
                }
                error("Gemini Nano is not ready for inference. Current status: $status.$preparation")
            }

            val modelName = model.getBaseModelName()
            val tokenCount = model.countTokens(nativeRequest).totalTokens
            val generated = if (streaming) {
                model.generateContent(nativeRequest, object : StreamingCallback {
                    override fun onNewText(additionalText: String) = callback.onText(additionalText, modelName)
                    override fun onNewThought(additionalThought: String) = callback.onThought(additionalThought, modelName)
                })
            } else {
                model.generateContent(nativeRequest)
            }

            generated.toNativeResponse(modelName, tokenCount)
        },
        mapError = ::toNativeError,
    )

    fun generate(
        request: NativeChatRequest,
        streaming: Boolean,
        callback: NativeChatCallback,
    ): NativeCancellation = operations.generate(request, streaming, callback)

    fun close() {
        runBlocking { operations.close() }
    }

    private fun toNativeError(exception: Throwable): NativeChatError = when (exception) {
        is GenAiException -> NativeChatError(
            exception.message ?: "Gemini Nano inference failed.",
            exception.errorCode,
            exception.retryDelay.let { delay ->
                try {
                    delay.toMillis()
                } catch (_: ArithmeticException) {
                    Long.MAX_VALUE
                }
            },
        )
        is CancellationException -> NativeChatError(
            exception.message ?: "Gemini Nano operation cancelled.", 7, null,
        )
        else -> NativeChatError(exception.message ?: "Gemini Nano inference failed.", 0, null)
    }

    private fun GenerateContentResponse.toNativeResponse(
        modelName: String,
        inputTokens: Int,
    ): NativeChatResponse {
        val candidate = checkNotNull(candidates.firstOrNull()) {
            "Gemini Nano returned no response candidate."
        }
        return NativeChatResponse(
            candidate.text,
            thoughtProcess.map { it.text }.toTypedArray(),
            candidate.finishReason ?: FINISH_OTHER,
            modelName,
            inputTokens,
        )
    }
}

internal suspend fun NativeChatRequest.toNative(
    isSystemPromptAvailable: suspend () -> Boolean,
    isThinkingModeAvailable: suspend () -> Boolean,
): GenerateContentRequest {
    val instructions = mutableListOf<String>()
    val contents = mutableListOf<Content>()
    val hasAssistantHistory = messages.any { it.role == "assistant" }
    messages.forEach { message ->
        when (message.role) {
            "system" -> message.parts.forEach { part ->
                require(part.kind == CONTENT_TEXT) {
                    "Gemini Nano system instructions support text only."
                }
                instructions.add(part.text.orEmpty())
            }
            "user", "assistant" -> {
                val builder = Content.builder()
                if (hasAssistantHistory) {
                    builder.addPart(TextPart(if (message.role == "user") "User:\n" else "Assistant:\n"))
                }
                message.parts.forEach { part ->
                    when (part.kind) {
                        CONTENT_TEXT -> builder.addPart(TextPart(part.text.orEmpty()))
                        CONTENT_IMAGE -> builder.addPart(
                            ImagePart(requireNotNull(part.image) { "Image part has no data." })
                        )
                        else -> error("Unsupported native content kind '${part.kind}'.")
                    }
                }
                if (hasAssistantHistory) builder.addPart(TextPart("\n\n"))
                contents.add(builder.build())
            }
            else -> error(
                "Gemini Nano does not support chat role '${message.role}'."
            )
        }
    }
    require(contents.isNotEmpty()) { "At least one user or assistant message is required." }

    val builder = GenerateContentRequest.Builder(contents)
    if (instructions.isNotEmpty()) {
        check(isSystemPromptAvailable()) {
            "The selected Gemini Nano model does not support native system instructions (requires Nano V3 or later)."
        }
        builder.systemInstruction = SystemInstruction(instructions.joinToString("\n\n"))
    }

    builder.temperature = options.temperature
    builder.topK = options.topK
    builder.seed = options.seed
    builder.maxOutputTokens = options.maxOutputTokens
    if (options.enableThinking) {
        check(isThinkingModeAvailable()) {
            "The selected Gemini Nano model does not support thinking mode (requires Nano V4 or later)."
        }
        builder.enableThinking = true
    }
    return builder.build()
}
