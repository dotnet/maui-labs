@file:JvmName("GeminiNanoNative")

package com.microsoft.maui.essentials.ai

import com.google.android.gms.internal.mlkit_genai_prompt.zzip
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
                val features = if (featureStatus == FeatureStatus.AVAILABLE) model.getAvailableFeatures() else emptyList()
                NativeModelStatus(
                    featureStatus,
                    Feature.THINKING_MODE in features,
                    model.getBaseModelName(),
                ).apply {
                    toolCallingAvailable = Feature.FUNCTION_CALLING in features
                    structuredOutputAvailable = Feature.STRUCTURED_OUTPUT in features
                }
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
                val schema = request.options.responseSchemaJson?.let { NativeResponseSchema.parse(it) }
                val model = Generation.getClient(modelConfiguration(request.options.modelId))
                val generatedResponse = try {
                    ensureActive()
                    val featureStatus = model.checkStatus()
                    check(featureStatus == FeatureStatus.AVAILABLE) {
                        "Gemini Nano is not ready (ML Kit FeatureStatus $featureStatus). Download the model explicitly before retrying."
                    }
                    val nativeSchema = schema?.native()
                    val nativeRequest = request.toNative(nativeSchema)
                    val features = if (nativeRequest.systemInstruction != null ||
                        nativeRequest.enableThinking || request.options.toolsJson != null || schema != null) {
                        model.getAvailableFeatures()
                    } else {
                        emptyList()
                    }
                    if (request.options.toolsJson != null) {
                        check(Feature.FUNCTION_CALLING in features) {
                            "The selected Gemini Nano model does not support native function calling."
                        }
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
                    if (schema != null) {
                        check(Feature.STRUCTURED_OUTPUT in features) {
                            "The selected Gemini Nano model does not support native structured output."
                        }
                    }
                    val modelName = model.getBaseModelName()
                    val processor = nativeSchema?.let {
                        NativeSchemaProcessor(modelConfiguration(request.options.modelId), it)
                    }
                    try {
                        run generation@{
                            val nativeStream = if (streaming && schema?.wrapsRoot != true) {
                                NativeChatStream(this, callback, modelName,
                                    deferToolText = request.options.toolsJson != null && schema == null).also { stream = it }
                            } else null
                            if (processor == null) {
                                val tokenCount = model.countTokens(nativeRequest).totalTokens
                                val generated = if (nativeStream != null) model.generateContent(nativeRequest, ThoughtHeaderFilter(nativeStream))
                                    else model.generateContent(nativeRequest)
                                val response = generated.toNativeResponse(modelName, tokenCount)
                                nativeStream?.finish(response)
                                return@generation response
                            }
                            var toolTurnTokens = 0
                            if (request.options.toolsJson != null) {
                                // ML Kit beta5: AICore emits no function calls while a response schema constrains
                                // decoding, and the typed API cannot return calls. The model chooses tools without the
                                // schema; only its final answer is generated under the schema. Remove when ML Kit
                                // supports both in one request.
                                val toolRequest = request.toNative()
                                toolTurnTokens = model.countTokens(toolRequest).totalTokens
                                val toolTurn = model.generateContent(toolRequest).toNativeResponse(modelName, toolTurnTokens)
                                if (toolTurn.functionCalls.isNotEmpty()) {
                                    val calls = NativeChatResponse("", toolTurn.thoughts, toolTurn.finishReason, modelName,
                                        toolTurnTokens).apply { functionCalls = toolTurn.functionCalls }
                                    if (nativeStream != null) {
                                        nativeStream.finish(calls)
                                    } else if (streaming) {
                                        ensureActive()
                                        calls.thoughts.forEach { callback.onThought(it, modelName) }
                                    }
                                    return@generation calls
                                }
                                checkToolTurnFinished(toolTurn.finishReason)
                            }
                            val tokenCount = toolTurnTokens + countStructuredTokens(processor, nativeRequest)
                            val generated = generateStructuredContent(processor, nativeRequest, nativeStream)
                                .toNativeResponse(modelName, tokenCount)
                            val text = validateStructuredResponse(checkNotNull(schema), generated)
                            val response = NativeChatResponse(text, generated.thoughts, generated.finishReason, modelName,
                                tokenCount)
                            if (nativeStream != null) {
                                nativeStream.finish(response)
                            } else if (streaming) {
                                // AICore wraps non-object roots; the unwrapped value is emitted once validated.
                                ensureActive()
                                response.thoughts.forEach { callback.onThought(it, modelName) }
                                callback.onText(response.text, modelName)
                            }
                            response
                        }
                    } finally {
                        processor?.close()
                    }
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
            if (candidate.functionCalls.isNotEmpty()) candidate.text.orEmpty()
            else checkNotNull(candidate.text) { "Gemini Nano returned a response candidate without text." },
            thoughtProcess.map { it.text }.toTypedArray(),
            candidate.finishReason ?: FINISH_UNSPECIFIED,
            modelName,
            inputTokens,
        ).apply {
            functionCalls = candidate.functionCalls.map { it.toNativeCall() }.toTypedArray()
        }
    }
}

internal fun NativeChatRequest.toNative(responseSchema: zzip? = null): GenerateContentRequest {
    val instructions = mutableListOf<String>()
    val contents = mutableListOf<Content>()
    val useNativeTools = options.toolsJson != null
    val callNames = mutableMapOf<String, String>()
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
                if (useNativeTools) {
                    builder.setRole(if (role == "assistant") Content.Role.MODEL else Content.Role.USER)
                } else if (hasAssistantHistory) {
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
                        CONTENT_FUNCTION_CALL -> {
                            val part = functionCallPart(requireNotNull(part.text))
                            val id = requireNotNull(part.functionCall.id)
                            require(callNames.put(id, part.functionCall.name) == null) {
                                "Duplicate function call ID '$id' in history."
                            }
                            builder.addPart(part)
                        }
                        CONTENT_FUNCTION_RESULT -> builder.addPart(
                            functionResultPart(requireNotNull(part.text), callNames))
                        else -> error("Unsupported native content kind '${part.kind}'.")
                    }
                }
                if (!useNativeTools && hasAssistantHistory) builder.addPart(TextPart("\n\n"))
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

    responseSchema?.let { appendSchemaPrompt(contents, it) }

    val builder = GenerateContentRequest.Builder(contents)
    if (instructions.isNotEmpty()) {
        builder.systemInstruction = SystemInstruction(instructions.joinToString("\n\n"))
    }

    builder.temperature = options.temperature
    builder.topK = options.topK
    builder.seed = options.seed
    builder.maxOutputTokens = options.maxOutputTokens ?: if (options.responseSchemaJson != null) 512 else null
    builder.enableThinking = options.enableThinking
    options.toolsJson?.let { builder.setTools(nativeTools(it)) }
    return builder.build()
}

// Matches ML Kit's typed path: the schema prompt goes on the last text part, or a new user part.
private fun appendSchemaPrompt(contents: MutableList<Content>, schema: zzip) {
    val contentIndex = contents.indexOfLast { content -> content.parts.any { it is TextPart } }
    if (contentIndex < 0) {
        contents.add(Content.builder().setRole(Content.Role.USER).addPart(TextPart(schemaPrompt("", schema))).build())
        return
    }
    val content = contents[contentIndex]
    val partIndex = content.parts.indexOfLast { it is TextPart }
    contents[contentIndex] = Content.builder().setRole(content.role).apply {
        content.parts.forEachIndexed { index, part ->
            addPart(if (index == partIndex) TextPart(schemaPrompt((part as TextPart).textString, schema)) else part)
        }
    }.build()
}
