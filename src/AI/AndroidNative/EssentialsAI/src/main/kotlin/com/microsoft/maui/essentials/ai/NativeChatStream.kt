package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.StreamingCallback
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.withContext

// ML Kit reports append-only callback deltas and returns the complete candidate.
internal class NativeChatStream(
    scope: CoroutineScope,
    private val target: NativeChatCallback,
    private val modelName: String,
) : StreamingCallback {
    private data class Event(val thought: Boolean, val text: String)

    private val text = StringBuilder()
    private val thoughts = StringBuilder()
    private val events = Channel<Event>(Channel.UNLIMITED)
    private val pump = scope.async {
        for (event in events) {
            if (event.thought) {
                target.onThought(event.text, modelName)
            } else {
                target.onText(event.text, modelName)
            }
        }
    }

    @Synchronized
    override fun onNewText(additionalText: String) {
        text.append(additionalText)
        events.trySend(Event(false, additionalText))
    }

    @Synchronized
    override fun onNewThought(additionalThought: String) {
        thoughts.append(additionalThought)
        events.trySend(Event(true, additionalThought))
    }

    suspend fun finish(response: NativeChatResponse) {
        synchronized(this) {
            emitMissingSuffix(text, response.text, thought = false)
            emitMissingSuffix(thoughts, response.thoughts.joinToString(""), thought = true)
            events.close()
        }
        pump.await()
    }

    suspend fun cancel() {
        events.cancel()
        withContext(NonCancellable) { pump.cancelAndJoin() }
    }

    private fun emitMissingSuffix(streamed: StringBuilder, final: String, thought: Boolean) {
        check(final.startsWith(streamed.toString())) {
            "Gemini Nano final ${if (thought) "thought" else "candidate"} diverged from streamed content."
        }
        if (final.length > streamed.length) {
            val suffix = final.substring(streamed.length)
            streamed.append(suffix)
            events.trySend(Event(thought, suffix))
        }
    }
}
