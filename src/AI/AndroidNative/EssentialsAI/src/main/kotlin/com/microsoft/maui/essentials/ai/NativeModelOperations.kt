package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.DownloadStatus
import com.google.mlkit.genai.common.FeatureStatus
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.launch
import java.util.concurrent.atomic.AtomicBoolean

internal fun featureStatusReason(status: Int): String? = when (status) {
    FeatureStatus.AVAILABLE -> null
    FeatureStatus.DOWNLOADABLE -> "DOWNLOADABLE ($status)"
    FeatureStatus.DOWNLOADING -> "DOWNLOADING ($status)"
    FeatureStatus.UNAVAILABLE -> "UNAVAILABLE ($status): Gemini Nano is unsupported or AICore configuration is not ready."
    else -> "Unknown ML Kit FeatureStatus ($status)"
}

internal fun requireAvailable(status: Int) {
    check(status == FeatureStatus.AVAILABLE) {
        "Gemini Nano is not ready: ${featureStatusReason(status)}. Call PrepareModelAsync explicitly before retrying."
    }
}

internal class NativeModelOperations<Model>(
    private val createModel: () -> Model,
    private val closeModel: (Model) -> Unit,
    private val checkStatus: suspend (Model) -> Int,
    private val thinkingAvailable: suspend (Model) -> Boolean,
    private val download: (Model) -> Flow<DownloadStatus>,
    private val mapError: (Throwable) -> NativeChatError,
    dispatcher: CoroutineDispatcher = Dispatchers.Default,
) {
    private val rootJob = SupervisorJob()
    private val scope = CoroutineScope(rootJob + dispatcher)
    private val closed = AtomicBoolean()

    fun execute(prepare: Boolean, callback: NativeModelCallback): NativeCancellation {
        val terminated = AtomicBoolean()
        fun fail(cause: Throwable) {
            if (terminated.compareAndSet(false, true)) callback.onError(mapError(cause))
        }
        if (closed.get()) {
            fail(kotlinx.coroutines.CancellationException("Gemini Nano client is closed."))
            return NativeCancellation(null)
        }
        val job = scope.launch {
            val snapshot = try {
                val model = createModel()
                try {
                    ensureActive()
                    var status = checkStatus(model)
                    if (prepare) {
                        // Joining AICore's existing download must not initiate another one.
                        while (status == FeatureStatus.DOWNLOADING) {
                            delay(250)
                            status = checkStatus(model)
                        }
                        if (status == FeatureStatus.DOWNLOADABLE) {
                            val terminal = checkNotNull(download(model).firstOrNull {
                                it is DownloadStatus.DownloadCompleted || it is DownloadStatus.DownloadFailed
                            }) { "Gemini Nano download Flow ended without a terminal status." }
                            if (terminal is DownloadStatus.DownloadFailed) throw terminal.e
                            status = checkStatus(model)
                            while (status == FeatureStatus.DOWNLOADING) {
                                delay(250)
                                status = checkStatus(model)
                            }
                        }
                        requireAvailable(status)
                    }
                    NativeModelStatus(status, featureStatusReason(status),
                        !prepare && status == FeatureStatus.AVAILABLE && thinkingAvailable(model))
                } finally {
                    closeModel(model)
                }.also { ensureActive() }
            } catch (cause: Throwable) {
                fail(cause)
                return@launch
            }
            if (terminated.compareAndSet(false, true)) callback.onComplete(snapshot)
        }
        job.invokeOnCompletion { cause -> if (cause != null) fail(cause) }
        if (closed.get()) job.cancel()
        return NativeCancellation(job)
    }

    suspend fun close() {
        closed.set(true)
        rootJob.cancelAndJoin()
    }
}
