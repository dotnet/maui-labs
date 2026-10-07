package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.Job

internal const val CONTENT_TEXT = 0
internal const val CONTENT_IMAGE = 1
internal const val FINISH_OTHER = -100

class NativeContentPart(
    val kind: Int,
    val text: String?,
    val image: ByteArray?,
)

class NativeChatMessage(
    val role: Int,
    val parts: Array<NativeContentPart>,
)

class NativeChatOptions(
    val systemInstruction: String?,
    val temperature: Float?,
    val topK: Int?,
    val seed: Int?,
    val maxOutputTokens: Int?,
    val enableThinking: Boolean,
)

class NativeChatRequest(
    val messages: Array<NativeChatMessage>,
    val options: NativeChatOptions,
)

class NativeChatResponse(
    val text: String,
    val thoughts: Array<String>,
    val finishReason: Int,
    val modelName: String,
    val inputTokens: Int,
)

class NativeChatError(
    val message: String,
    val code: Int,
    val retryDelayMilliseconds: Long?,
)

interface NativeChatCallback {
    fun onText(text: String)
    fun onThought(thought: String)
    fun onComplete(response: NativeChatResponse)
    fun onError(error: NativeChatError)
}

class NativeCancellation internal constructor(private val job: Job?) {
    fun cancel() = job?.cancel()
}
