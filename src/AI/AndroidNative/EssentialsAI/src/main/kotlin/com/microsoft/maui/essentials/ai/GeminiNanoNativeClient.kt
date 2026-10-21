package com.microsoft.maui.essentials.ai

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
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.util.concurrent.atomic.AtomicBoolean

class GeminiNanoNativeClient {
    private val chatJob = SupervisorJob()
    private val chatScope = CoroutineScope(chatJob + Dispatchers.Default)
    private val chatClosed = AtomicBoolean()

    private val modelOperations = NativeModelOperations(
        createModel = { Generation.getClient() },
        closeModel = { it.close() },
        checkStatus = { it.checkStatus() },
        thinkingAvailable = { it.isThinkingModeAvailable() },
        download = { it.download() },
        mapError = ::toNativeError,
    )

    fun checkStatus(callback: NativeModelCallback): NativeCancellation =
        modelOperations.execute(false, callback)

    fun prepareModel(callback: NativeModelCallback): NativeCancellation =
        modelOperations.execute(true, callback)

    fun generate(
        request: NativeChatRequest,
        streaming: Boolean,
        callback: NativeChatCallback,
    ): NativeCancellation {
        val terminated = AtomicBoolean()

        fun fail(cause: Throwable) {
            if (terminated.compareAndSet(false, true)) callback.onError(toNativeError(cause))
        }

        if (chatClosed.get()) {
            fail(CancellationException("Gemini Nano client is closed."))
            return NativeCancellation(null)
        }

        val job = chatScope.launch {
            val events = NativeChatEventQueue(this, callback)
            val response = try {
                val model = Generation.getClient()
                val generatedResponse = try {
                    ensureActive()
                    requireAvailable(model.checkStatus())
                    val nativeRequest = request.toNative(
                        model::isSystemPromptAvailable,
                        model::isThinkingModeAvailable,
                    )
                    val modelName = model.getBaseModelName()
                    val tokenCount = model.countTokens(nativeRequest).totalTokens
                    val accumulator = NativeStreamAccumulator(events.callback, modelName)
                    val generated = if (streaming) {
                        model.generateContent(nativeRequest, object : StreamingCallback {
                            override fun onNewText(additionalText: String) = accumulator.onText(additionalText)
                            override fun onNewThought(additionalThought: String) = accumulator.onThought(additionalThought)
                        })
                    } else {
                        model.generateContent(nativeRequest)
                    }

                    generated.toNativeResponse(modelName, tokenCount).also {
                        if (streaming) accumulator.finish(it)
                    }
                } finally {
                    model.close()
                }
                events.finish()
                ensureActive()
                generatedResponse
            } catch (cause: Throwable) {
                events.cancel()
                fail(cause)
                return@launch
            }

            if (terminated.compareAndSet(false, true)) callback.onComplete(response)
        }

        job.invokeOnCompletion { cause ->
            if (cause != null) fail(cause)
        }
        if (chatClosed.get()) job.cancel()
        return NativeCancellation(job)
    }

    fun close() {
        runBlocking {
            chatClosed.set(true)
            chatJob.cancelAndJoin()
            modelOperations.close()
        }
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
            candidate.finishReason ?: FINISH_UNSPECIFIED,
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
