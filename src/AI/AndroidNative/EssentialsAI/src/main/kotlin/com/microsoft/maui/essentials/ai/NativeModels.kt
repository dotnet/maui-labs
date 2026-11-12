package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.Job

internal const val CONTENT_TEXT = 0
internal const val CONTENT_IMAGE = 1
internal const val CONTENT_FUNCTION_CALL = 2
internal const val CONTENT_FUNCTION_RESULT = 3
internal const val FINISH_UNSPECIFIED = Int.MIN_VALUE

class NativeContentPart(
    val kind: Int,
    val text: String?,
    val image: ByteArray?,
)

class NativeChatMessage(
    val role: String,
    val parts: Array<NativeContentPart>,
)

class NativeChatOptions(
    val temperature: Float?,
    val topK: Int?,
    val seed: Int?,
    val maxOutputTokens: Int?,
    val enableThinking: Boolean,
    val modelId: String?,
) {
    var toolsJson: String? = null
    var responseSchemaJson: String? = null
}

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
) {
    var functionCalls: Array<NativeFunctionCall> = emptyArray()
}

class NativeFunctionCall(
    val id: String,
    val name: String,
    val argumentsJson: String,
)

class NativeChatError(
    val message: String,
    val code: Int,
    val retryDelayMilliseconds: Long?,
)

class NativeModelStatus(
    val status: Int,
    val thinkingAvailable: Boolean,
    val modelName: String?,
) {
    var toolCallingAvailable: Boolean = false
    var structuredOutputAvailable: Boolean = false
}

interface NativeCheckStatusCallback {
    fun onComplete(status: NativeModelStatus)
    fun onError(error: NativeChatError)
}

interface NativeDownloadCallback {
    fun onStarted(bytesToDownload: Long)
    fun onProgress(bytesDownloaded: Long)
    fun onComplete()
    fun onError(error: NativeChatError)
}

interface NativeChatCallback {
    fun onText(text: String, modelName: String)
    fun onThought(thought: String, modelName: String)
    fun onComplete(response: NativeChatResponse)
    fun onError(error: NativeChatError)
}

class NativeCancellation internal constructor(private val job: Job?) {
    fun cancel() = job?.cancel()
}
