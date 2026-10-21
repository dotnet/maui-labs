package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.withContext
import java.util.concurrent.atomic.AtomicBoolean

internal class NativeChatEventQueue(
    scope: CoroutineScope,
    private val target: NativeChatCallback,
) {
    private data class Event(val thought: Boolean, val text: String, val modelName: String)

    private val accepting = AtomicBoolean(true)
    private val events = Channel<Event>(Channel.UNLIMITED)
    private val drain = scope.async {
        for (event in events) {
            if (event.thought) {
                target.onThought(event.text, event.modelName)
            } else {
                target.onText(event.text, event.modelName)
            }
        }
    }

    val callback = object : NativeChatCallback {
        override fun onText(text: String, modelName: String) {
            if (accepting.get()) events.trySend(Event(false, text, modelName))
        }

        override fun onThought(thought: String, modelName: String) {
            if (accepting.get()) events.trySend(Event(true, thought, modelName))
        }

        override fun onComplete(response: NativeChatResponse) =
            error("Inference returns its terminal response after model cleanup.")

        override fun onError(error: NativeChatError) =
            error("Inference throws its terminal failure after model cleanup.")
    }

    suspend fun finish() {
        accepting.set(false)
        events.close()
        drain.await()
    }

    suspend fun cancel() {
        accepting.set(false)
        events.cancel()
        withContext(NonCancellable) { drain.cancelAndJoin() }
    }
}
