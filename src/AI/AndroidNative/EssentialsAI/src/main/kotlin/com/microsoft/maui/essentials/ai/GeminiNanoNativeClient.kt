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
            val status = model.checkStatus()
            if (status != FeatureStatus.AVAILABLE) {
                val preparation = when (status) {
                    FeatureStatus.DOWNLOADABLE, FeatureStatus.DOWNLOADING ->
                        " Download Gemini Nano through AICore before retrying."
                    else -> " This device must support Gemini Nano and have the latest AICore configuration."
                }
                error("Gemini Nano is not ready for inference. Current status: $status.$preparation")
            }

            val nativeRequest = buildRequest(model, request)
            val modelName = model.getBaseModelName()
            val tokenCount = model.countTokens(nativeRequest).totalTokens
            val generated = if (streaming) {
                model.generateContent(nativeRequest, object : StreamingCallback {
                    override fun onNewText(additionalText: String) = callback.onText(additionalText)
                    override fun onNewThought(additionalThought: String) = callback.onThought(additionalThought)
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

    private suspend fun buildRequest(
        model: com.google.mlkit.genai.prompt.GenerativeModel,
        request: NativeChatRequest,
    ): GenerateContentRequest {
        val contents = request.messages.map { message ->
            val builder = Content.builder()
            message.parts.forEach { part ->
                when (part.kind) {
                    CONTENT_TEXT -> builder.addPart(TextPart(part.text.orEmpty()))
                    CONTENT_IMAGE -> builder.addPart(
                        ImagePart(requireNotNull(part.image) { "Image part has no data." })
                    )
                    else -> error("Unsupported native content kind '${part.kind}'.")
                }
            }
            builder.build()
        }

        val builder = GenerateContentRequest.Builder(contents)
        val options = request.options
        if (options.systemInstruction != null) {
            if (model.isSystemPromptAvailable()) {
                builder.systemInstruction = SystemInstruction(options.systemInstruction)
            } else {
                val systemContent = Content.builder()
                    .addPart(TextPart("<|system|>\n${options.systemInstruction}\n<|end|>\n"))
                    .build()
                return buildConfiguredRequest(
                    model,
                    GenerateContentRequest.Builder(listOf(systemContent) + contents),
                    options,
                )
            }
        }

        return buildConfiguredRequest(model, builder, options)
    }

    private suspend fun buildConfiguredRequest(
        model: com.google.mlkit.genai.prompt.GenerativeModel,
        builder: GenerateContentRequest.Builder,
        options: NativeChatOptions,
    ): GenerateContentRequest {
        builder.temperature = options.temperature
        builder.topK = options.topK
        builder.seed = options.seed
        builder.maxOutputTokens = options.maxOutputTokens
        if (options.enableThinking) {
            if (!model.isThinkingModeAvailable()) {
                error("The selected Gemini Nano model does not support thinking mode.")
            }
            builder.enableThinking = true
        }
        return builder.build()
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
