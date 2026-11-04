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

    private class ContentBuffer(val thought: Boolean) {
        val received = StringBuilder()
        var emittedLength = 0
    }

    private val text = ContentBuffer(thought = false)
    private val thoughts = ContentBuffer(thought = true)
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
        append(text, additionalText)
    }

    @Synchronized
    override fun onNewThought(additionalThought: String) {
        append(thoughts, additionalThought)
    }

    suspend fun finish(response: NativeChatResponse) {
        synchronized(this) {
            finishContent(text, response.text)
            finishContent(thoughts, response.thoughts.joinToString(""))
            events.close()
        }
        pump.await()
    }

    suspend fun cancel() {
        events.cancel()
        withContext(NonCancellable) { pump.cancelAndJoin() }
    }

    private fun append(buffer: ContentBuffer, additional: String) {
        val previousLength = buffer.received.length
        buffer.received.append(additional)
        val lastContent = additional.indexOfLast { !it.isWhitespace() }
        if (lastContent >= 0) {
            val end = previousLength + lastContent + 1
            events.trySend(Event(buffer.thought, buffer.received.substring(buffer.emittedLength, end)))
            buffer.emittedLength = end
        }
    }

    private fun finishContent(buffer: ContentBuffer, final: String) {
        val received = buffer.received.toString()
        // ML Kit can omit or change trailing whitespace in the final candidate.
        check(final.startsWith(received) || final.trimEnd() == received.trimEnd()) {
            "Gemini Nano final ${if (buffer.thought) "thought" else "candidate"} diverged from streamed content."
        }
        if (final.length > buffer.emittedLength) {
            events.trySend(Event(buffer.thought, final.substring(buffer.emittedLength)))
            buffer.emittedLength = final.length
        }
    }
}
