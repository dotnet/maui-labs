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
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.util.concurrent.atomic.AtomicBoolean

class GeminiNanoNativeClient {
    private val rootJob = SupervisorJob()
    private val scope = CoroutineScope(rootJob + Dispatchers.Default)
    private val closed = AtomicBoolean()

    fun generate(
        request: NativeChatRequest,
        streaming: Boolean,
        callback: NativeChatCallback,
    ): NativeCancellation {
        val terminated = AtomicBoolean()

        fun complete(response: NativeChatResponse) {
            if (terminated.compareAndSet(false, true)) {
                callback.onComplete(response)
            }
        }

        fun fail(error: NativeChatError) {
            if (terminated.compareAndSet(false, true)) {
                callback.onError(error)
            }
        }

        if (closed.get()) {
            fail(NativeChatError("Gemini Nano client is closed.", 7, null))
            return NativeCancellation(null)
        }

        val job = scope.launch {
            try {
                val model = Generation.getClient()
                val response = try {
                    val status = model.checkStatus()
                    if (status != FeatureStatus.AVAILABLE) {
                        error("Gemini Nano is not ready for inference. Current status: $status")
                    }

                    val nativeRequest = buildRequest(model, request)
                    val modelName = model.getBaseModelName()
                    val tokenCount = model.countTokens(nativeRequest).totalTokens
                    val generated = if (streaming) {
                        model.generateContent(nativeRequest, object : StreamingCallback {
                            override fun onNewText(additionalText: String) {
                                if (!terminated.get()) callback.onText(additionalText)
                            }

                            override fun onNewThought(additionalThought: String) {
                                if (!terminated.get()) callback.onThought(additionalThought)
                            }
                        })
                    } else {
                        model.generateContent(nativeRequest)
                    }

                    generated.toNativeResponse(modelName, tokenCount)
                } finally {
                    model.close()
                }

                complete(response)
            } catch (exception: GenAiException) {
                fail(
                    NativeChatError(
                        exception.message ?: "Gemini Nano inference failed.",
                        exception.errorCode,
                        runCatching { exception.retryDelay.toMillis() }.getOrNull(),
                    )
                )
            } catch (exception: CancellationException) {
                fail(NativeChatError("Gemini Nano operation cancelled.", 7, null))
            } catch (exception: Throwable) {
                fail(
                    NativeChatError(
                        exception.message ?: "Gemini Nano inference failed.",
                        0,
                        null,
                    )
                )
            }
        }

        job.invokeOnCompletion { cause ->
            if (cause is CancellationException) {
                fail(NativeChatError("Gemini Nano operation cancelled.", 7, null))
            }
        }
        if (closed.get()) job.cancel()

        return NativeCancellation(job)
    }

    fun close() {
        if (closed.compareAndSet(false, true)) {
            runBlocking {
                rootJob.cancelAndJoin()
            }
        }
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
        val candidate = candidates.firstOrNull()
        return NativeChatResponse(
            candidate?.text.orEmpty(),
            thoughtProcess.map { it.text }.toTypedArray(),
            candidate?.finishReason ?: FINISH_OTHER,
            modelName,
            inputTokens,
        )
    }
}
