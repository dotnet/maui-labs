package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.concurrent.atomic.AtomicBoolean

internal class NativeChatOperations<Model>(
    private val createModel: () -> Model,
    private val closeModel: (Model) -> Unit,
    private val infer: suspend (Model, NativeChatRequest, Boolean, NativeChatCallback) -> NativeChatResponse,
    private val mapError: (Throwable) -> NativeChatError,
    dispatcher: CoroutineDispatcher = Dispatchers.Default,
) {
    private val rootJob = SupervisorJob()
    private val scope = CoroutineScope(rootJob + dispatcher)
    private val closed = AtomicBoolean()

    fun generate(
        request: NativeChatRequest,
        streaming: Boolean,
        callback: NativeChatCallback,
    ): NativeCancellation {
        val terminated = AtomicBoolean()

        fun fail(cause: Throwable) {
            if (terminated.compareAndSet(false, true)) callback.onError(mapError(cause))
        }

        if (closed.get()) {
            fail(kotlinx.coroutines.CancellationException("Gemini Nano client is closed."))
            return NativeCancellation(null)
        }

        val job = scope.launch {
            val updates = Channel<Pair<Boolean, String>>(Channel.UNLIMITED)
            val progressTask = async {
                for ((thought, text) in updates) {
                    if (thought) callback.onThought(text) else callback.onText(text)
                }
            }
            val progress = object : NativeChatCallback {
                override fun onText(text: String) {
                    if (isActive) updates.trySend(false to text)
                }

                override fun onThought(thought: String) {
                    if (isActive) updates.trySend(true to thought)
                }

                override fun onComplete(response: NativeChatResponse) =
                    error("Inference must return its response after model cleanup.")

                override fun onError(error: NativeChatError) =
                    error("Inference must throw its failure after model cleanup.")
            }

            val response = try {
                val model = createModel()
                try {
                    ensureActive()
                    infer(model, request, streaming, progress)
                } finally {
                    closeModel(model)
                }.also {
                    updates.close()
                    progressTask.await()
                    ensureActive()
                }
            } catch (cause: Throwable) {
                updates.cancel()
                // Terminal notification releases the managed JNI callback. Wait
                // for any admitted progress callback before permitting disposal.
                withContext(NonCancellable) { progressTask.cancelAndJoin() }
                fail(cause)
                return@launch
            }

            if (terminated.compareAndSet(false, true)) callback.onComplete(response)
        }

        // A cancelled scope may prevent the coroutine body from ever running.
        job.invokeOnCompletion { cause ->
            if (cause != null) fail(cause)
        }
        if (closed.get()) job.cancel()
        return NativeCancellation(job)
    }

    suspend fun close() {
        closed.set(true)
        rootJob.cancelAndJoin()
    }
}
