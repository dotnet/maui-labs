@file:JvmName("GeminiNanoNative")

package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.GenAiException
import com.google.mlkit.genai.common.DownloadStatus
import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.prompt.Content
import com.google.mlkit.genai.prompt.Feature
import com.google.mlkit.genai.prompt.GenerateContentRequest
import com.google.mlkit.genai.prompt.GenerateContentResponse
import com.google.mlkit.genai.prompt.Generation
import com.google.mlkit.genai.prompt.GenerationConfig
import com.google.mlkit.genai.prompt.ModelConfig
import com.google.mlkit.genai.prompt.ModelPreference
import com.google.mlkit.genai.prompt.ModelReleaseStage
import com.google.mlkit.genai.prompt.ImagePart
import com.google.mlkit.genai.prompt.SystemInstruction
import com.google.mlkit.genai.prompt.TextPart
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.util.concurrent.atomic.AtomicBoolean

internal fun modelConfiguration(modelId: String?): GenerationConfig {
    val (stage, modelPreference) = when (modelId) {
        null, "gemini-nano", "gemini-nano-full" -> ModelReleaseStage.STABLE to ModelPreference.FULL
        "gemini-nano-fast" -> ModelReleaseStage.STABLE to ModelPreference.FAST
        "gemini-nano-preview-full" -> ModelReleaseStage.PREVIEW to ModelPreference.FULL
        "gemini-nano-preview-fast" -> ModelReleaseStage.PREVIEW to ModelPreference.FAST
        else -> throw IllegalArgumentException(
            "Unsupported Gemini Nano model ID '$modelId'. Use gemini-nano, gemini-nano-fast, " +
                "gemini-nano-full, gemini-nano-preview-fast, or gemini-nano-preview-full."
        )
    }
    return GenerationConfig.Builder().apply {
        modelConfig = ModelConfig.Builder().apply {
            releaseStage = stage
            preference = modelPreference
        }.build()
    }.build()
}

fun checkStatus(
    modelId: String?,
    callback: NativeCheckStatusCallback,
): NativeCancellation {
    val terminated = AtomicBoolean()
    fun fail(cause: Throwable) {
        if (terminated.compareAndSet(false, true)) callback.onError(toNativeError(cause))
    }
    val job = CoroutineScope(SupervisorJob() + Dispatchers.Default).launch {
        val status = try {
            val model = Generation.getClient(modelConfiguration(modelId))
            try {
                ensureActive()
                val featureStatus = model.checkStatus()
                NativeModelStatus(
                    featureStatus,
                    featureStatus == FeatureStatus.AVAILABLE &&
                        Feature.THINKING_MODE in model.getAvailableFeatures(),
                    model.getBaseModelName(),
                )
            } finally {
                model.close()
            }.also { ensureActive() }
        } catch (cause: Throwable) {
            fail(cause)
            return@launch
        }
        if (terminated.compareAndSet(false, true)) callback.onComplete(status)
    }
    job.invokeOnCompletion { cause -> if (cause != null) fail(cause) }
    return NativeCancellation(job)
}

fun downloadModel(
    modelId: String?,
    callback: NativeDownloadCallback,
): NativeCancellation {
    val terminated = AtomicBoolean()
    fun fail(cause: Throwable) {
        if (terminated.compareAndSet(false, true)) callback.onError(toNativeError(cause))
    }
    val job = CoroutineScope(SupervisorJob() + Dispatchers.Default).launch {
        var completed = false
        try {
            val model = Generation.getClient(modelConfiguration(modelId))
            try {
                ensureActive()
                model.download().collect { update ->
                    ensureActive()
                    when (update) {
                        is DownloadStatus.DownloadStarted ->
                            callback.onStarted(update.bytesToDownload)
                        is DownloadStatus.DownloadProgress ->
                            callback.onProgress(update.totalBytesDownloaded)
                        DownloadStatus.DownloadCompleted -> completed = true
                        is DownloadStatus.DownloadFailed -> throw update.e
                    }
                }
                check(completed) { "Gemini Nano download Flow ended without completing." }
            } finally {
                model.close()
            }
            ensureActive()
        } catch (cause: Throwable) {
            fail(cause)
            return@launch
        }
        if (terminated.compareAndSet(false, true)) {
            callback.onComplete()
        }
    }
    job.invokeOnCompletion { cause -> if (cause != null) fail(cause) }
    return NativeCancellation(job)
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

class GeminiNanoNativeClient {
    private val operationJob = SupervisorJob()
    private val operationScope = CoroutineScope(operationJob + Dispatchers.Default)
    private val closed = AtomicBoolean()

    fun generate(
        request: NativeChatRequest,
        streaming: Boolean,
        callback: NativeChatCallback,
    ): NativeCancellation {
        val terminated = AtomicBoolean()

        fun fail(cause: Throwable) {
            if (terminated.compareAndSet(false, true)) callback.onError(toNativeError(cause))
        }

        if (closed.get()) {
            fail(CancellationException("Gemini Nano client is closed."))
            return NativeCancellation(null)
        }

        val job = operationScope.launch {
            var stream: NativeChatStream? = null
            val response = try {
                val model = Generation.getClient(modelConfiguration(request.options.modelId))
                val generatedResponse = try {
                    ensureActive()
                    val featureStatus = model.checkStatus()
                    check(featureStatus == FeatureStatus.AVAILABLE) {
                        "Gemini Nano is not ready (ML Kit FeatureStatus $featureStatus). Download the model explicitly before retrying."
                    }
                    val nativeRequest = request.toNative()
                    val features = if (nativeRequest.systemInstruction != null || nativeRequest.enableThinking) {
                        model.getAvailableFeatures()
                    } else {
                        emptyList()
                    }
                    if (nativeRequest.systemInstruction != null) {
                        check(Feature.SYSTEM_PROMPT in features) {
                            "The selected Gemini Nano model does not support native system instructions (requires Nano V3 or later)."
                        }
                    }
                    if (nativeRequest.enableThinking) {
                        check(Feature.THINKING_MODE in features) {
                            "The selected Gemini Nano model does not support thinking mode (requires Nano V4 or later)."
                        }
                    }
                    val modelName = model.getBaseModelName()
                    val tokenCount = model.countTokens(nativeRequest).totalTokens
                    val generated = if (streaming) {
                        val nativeStream = NativeChatStream(this, callback, modelName)
                        stream = nativeStream
                        model.generateContent(nativeRequest, nativeStream)
                    } else {
                        model.generateContent(nativeRequest)
                    }

                    val response = generated.toNativeResponse(modelName, tokenCount)
                    stream?.finish(response)
                    response
                } finally {
                    model.close()
                }
                ensureActive()
                generatedResponse
            } catch (cause: Throwable) {
                stream?.cancel()
                fail(cause)
                return@launch
            }

            if (terminated.compareAndSet(false, true)) callback.onComplete(response)
        }

        job.invokeOnCompletion { cause ->
            if (cause != null) fail(cause)
        }
        if (closed.get()) job.cancel()
        return NativeCancellation(job)
    }

    fun close() {
        runBlocking {
            closed.set(true)
            operationJob.cancelAndJoin()
        }
    }

    private fun GenerateContentResponse.toNativeResponse(
        modelName: String,
        inputTokens: Int,
    ): NativeChatResponse {
        val candidate = checkNotNull(candidates.firstOrNull()) {
            "Gemini Nano returned no response candidate."
        }
        return NativeChatResponse(
            checkNotNull(candidate.text) { "Gemini Nano returned a response candidate without text." },
            thoughtProcess.map { it.text }.toTypedArray(),
            candidate.finishReason ?: FINISH_UNSPECIFIED,
            modelName,
            inputTokens,
        )
    }
}

internal fun NativeChatRequest.toNative(): GenerateContentRequest {
    val instructions = mutableListOf<String>()
    val contents = mutableListOf<Content>()
    val hasAssistantHistory = messages.any {
        it.role.equals("assistant", ignoreCase = true) || it.role.equals("tool", ignoreCase = true)
    }
    messages.forEach { message ->
        val role = message.role.lowercase()
        when (role) {
            "system" -> {
                val instruction = message.parts.joinToString("") { part ->
                    require(part.kind == CONTENT_TEXT) {
                        "Gemini Nano system instructions support text only."
                    }
                    part.text.orEmpty()
                }
                if (instruction.isNotBlank()) instructions.add(instruction)
            }
            "user", "assistant", "tool" -> {
                val builder = Content.builder()
                if (hasAssistantHistory) {
                    builder.addPart(TextPart(when (role) {
                        "user" -> "User:\n"
                        "assistant" -> "Assistant:\n"
                        else -> "Tool:\n"
                    }))
                }
                if (message.parts.isEmpty()) builder.addPart(TextPart(""))
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
            else -> Unit
        }
    }
    require(messages.any {
        it.role.equals("user", ignoreCase = true) || it.role.equals("assistant", ignoreCase = true)
    }) {
        "At least one user or assistant message is required."
    }

    val builder = GenerateContentRequest.Builder(contents)
    if (instructions.isNotEmpty()) {
        builder.systemInstruction = SystemInstruction(instructions.joinToString("\n\n"))
    }

    builder.temperature = options.temperature
    builder.topK = options.topK
    builder.seed = options.seed
    builder.maxOutputTokens = options.maxOutputTokens
    builder.enableThinking = options.enableThinking
    return builder.build()
}
