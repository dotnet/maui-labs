package com.microsoft.maui.essentials.ai

// ML Kit's callbacks are deltas; its returned candidate is the complete text.
internal class NativeStreamAccumulator(
    private val callback: NativeChatCallback,
    private val modelName: String,
) {
    private val text = StringBuilder()

    @Synchronized
    fun onText(additionalText: String) {
        text.append(additionalText)
        callback.onText(additionalText, modelName)
    }

    @Synchronized
    fun onThought(additionalThought: String) {
        callback.onThought(additionalThought, modelName)
    }

    @Synchronized
    fun finish(response: NativeChatResponse) {
        check(response.text.startsWith(text.toString())) {
            "Gemini Nano final candidate diverged from streamed text."
        }
        if (response.text.length > text.length) onText(response.text.substring(text.length))
    }
}
