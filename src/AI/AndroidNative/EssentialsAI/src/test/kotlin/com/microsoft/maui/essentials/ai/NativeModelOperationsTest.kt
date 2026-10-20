package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.common.DownloadStatus
import com.google.mlkit.genai.common.FeatureStatus
import com.google.mlkit.genai.common.GenAiException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.*
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class NativeModelOperationsTest {
    private class Model(var closed: Boolean = false)
    private class Callback(private val terminal: () -> Unit = {}) : NativeModelCallback {
        val statuses = mutableListOf<NativeModelStatus>()
        val errors = mutableListOf<NativeChatError>()
        override fun onComplete(status: NativeModelStatus) { terminal(); statuses.add(status) }
        override fun onError(error: NativeChatError) { terminal(); errors.add(error) }
    }

    private fun TestScope.operations(
        model: Model = Model(),
        status: suspend (Model) -> Int = { FeatureStatus.AVAILABLE },
        thinking: suspend (Model) -> Boolean = { true },
        download: (Model) -> Flow<DownloadStatus> = { error("Must not start a download.") },
        create: () -> Model = { model },
        mapError: (Throwable) -> NativeChatError = {
            NativeChatError(it.message.orEmpty(), if (it is CancellationException) 7 else 0, null)
        },
    ) = NativeModelOperations(create, { it.closed = true }, status, thinking, download, mapError,
        StandardTestDispatcher(testScheduler))

    @Test
    fun availableStatusAndPreparationDoNotDownloadAndCleanUpBeforeCompletion() = runTest {
        for (prepare in listOf(false, true)) {
            val model = Model()
            val operations = operations(model, thinking = {
                check(!prepare) { "Preparation must not query optional capabilities." }
                true
            })
            val callback = Callback { assertTrue(model.closed) }
            operations.execute(prepare, callback)
            runCurrent()
            val result = callback.statuses.single()
            assertEquals(FeatureStatus.AVAILABLE, result.status)
            assertEquals(!prepare, result.thinkingAvailable)
            assertNull(result.reason)
            assertTrue(callback.errors.isEmpty())
            operations.close()
        }
    }

    @Test
    fun statusReturnsNativeReasonsWithoutPreparingOrQueryingUnavailableCapabilities() = runTest {
        for (status in listOf(FeatureStatus.UNAVAILABLE, FeatureStatus.DOWNLOADABLE, FeatureStatus.DOWNLOADING, 99)) {
            val operations = operations(status = { status }, thinking = { error("Not ready.") })
            val callback = Callback()
            operations.execute(false, callback)
            runCurrent()
            assertEquals(status, callback.statuses.single().status)
            assertEquals(featureStatusReason(status), callback.statuses.single().reason)
            assertFalse(callback.statuses.single().thinkingAvailable)
            operations.close()
        }
    }

    @Test
    fun downloadableCollectsStartedProgressAndCompletedThenRechecksReadiness() = runTest {
        val model = Model()
        var status = FeatureStatus.DOWNLOADABLE
        var collected = false
        var released = false
        val operations = operations(model, { status }, download = {
            flow {
                collected = true
                try {
                    emit(DownloadStatus.DownloadStarted(100))
                    emit(DownloadStatus.DownloadProgress(50))
                    status = FeatureStatus.AVAILABLE
                    emit(DownloadStatus.DownloadCompleted)
                    error("Collection must stop at the terminal status.")
                } finally { released = true }
            }
        })
        val callback = Callback { assertTrue(released); assertTrue(model.closed) }
        operations.execute(true, callback)
        assertFalse(collected)
        runCurrent()
        assertTrue(collected)
        assertEquals(FeatureStatus.AVAILABLE, callback.statuses.single().status)
        assertTrue(callback.errors.isEmpty())
        operations.close()
    }

    @Test
    fun failedDownloadPreservesTheOriginalNativeException() = runTest {
        val failure = GenAiException("native download failure", IllegalStateException("cause"), 5,
            java.time.Duration.ofMillis(321))
        val model = Model()
        val operations = operations(model, { FeatureStatus.DOWNLOADABLE },
            download = { flowOf(DownloadStatus.DownloadStarted(1), DownloadStatus.DownloadFailed(failure)) },
            mapError = {
                assertSame(failure, it)
                NativeChatError(it.message.orEmpty(), failure.errorCode, failure.retryDelay.toMillis())
            })
        val callback = Callback { assertTrue(model.closed) }
        operations.execute(true, callback)
        runCurrent()
        assertEquals("native download failure", callback.errors.single().message)
        assertEquals(5, callback.errors.single().code)
        assertEquals(321L, callback.errors.single().retryDelayMilliseconds)
        assertTrue(callback.statuses.isEmpty())
        operations.close()
    }

    @Test
    fun cancellationStopsOwnedDownloadCollectionBeforeTerminalNotification() = runTest {
        val model = Model()
        var released = false
        val operations = operations(model, { FeatureStatus.DOWNLOADABLE }, download = {
            flow {
                try { emit(DownloadStatus.DownloadProgress(1)); awaitCancellation() }
                finally { released = true }
            }
        })
        val callback = Callback { assertTrue(released); assertTrue(model.closed) }
        val cancellation = operations.execute(true, callback)
        runCurrent()
        assertFalse(released)
        cancellation.cancel()
        runCurrent()
        assertEquals(7, callback.errors.single().code)
        assertTrue(callback.statuses.isEmpty())
        operations.close()
        assertEquals(1, callback.errors.size)
    }

    @Test
    fun downloadingPollsWithDelayWithoutStartingASecondDownload() = runTest {
        var reads = 0
        val operations = operations(status = { if (++reads < 3) FeatureStatus.DOWNLOADING else FeatureStatus.AVAILABLE })
        val callback = Callback()
        operations.execute(true, callback)
        runCurrent()
        assertEquals(1, reads)
        assertTrue(callback.statuses.isEmpty())
        advanceTimeBy(250); runCurrent()
        assertEquals(2, reads)
        advanceTimeBy(250); runCurrent()
        assertEquals(3, reads)
        assertEquals(FeatureStatus.AVAILABLE, callback.statuses.single().status)
        operations.close()
    }

    @Test
    fun downloadingRechecksUnavailableOrDownloadable() = runTest {
        for (next in listOf(FeatureStatus.UNAVAILABLE, FeatureStatus.DOWNLOADABLE)) {
            var reads = 0
            var downloads = 0
            val operations = operations(status = {
                when (++reads) {
                    1 -> FeatureStatus.DOWNLOADING
                    2 -> next
                    else -> FeatureStatus.AVAILABLE
                }
            }, download = { downloads++; flowOf(DownloadStatus.DownloadCompleted) })
            val callback = Callback()
            operations.execute(true, callback)
            runCurrent()
            advanceTimeBy(250); runCurrent()
            if (next == FeatureStatus.UNAVAILABLE) {
                assertTrue(callback.errors.single().message.contains("UNAVAILABLE"))
                assertEquals(0, downloads)
            } else {
                assertEquals(1, downloads)
                assertEquals(FeatureStatus.AVAILABLE, callback.statuses.single().status)
            }
            operations.close()
        }
    }

    @Test
    fun unavailableAndUnknownPreparationFailWithoutDownload() = runTest {
        for (status in listOf(FeatureStatus.UNAVAILABLE, 99)) {
            val operations = operations(status = { status })
            val callback = Callback()
            operations.execute(true, callback)
            runCurrent()
            assertTrue(callback.errors.single().message.contains(featureStatusReason(status)!!))
            assertTrue(callback.statuses.isEmpty())
            operations.close()
        }
    }

    @Test
    fun downloadEndingWithoutTerminalOrWithoutReadinessIsNotSuccess() = runTest {
        for (events in listOf(flowOf<DownloadStatus>(DownloadStatus.DownloadProgress(1)),
            flowOf<DownloadStatus>(DownloadStatus.DownloadCompleted))) {
            val operations = operations(status = { FeatureStatus.DOWNLOADABLE }, download = { events })
            val callback = Callback()
            operations.execute(true, callback)
            runCurrent()
            assertEquals(1, callback.errors.size)
            assertTrue(callback.statuses.isEmpty())
            operations.close()
        }
    }

    @Test
    fun completedDownloadWaitsForAICoreReadinessWithoutRedownloading() = runTest {
        var reads = 0
        var downloads = 0
        val operations = operations(status = {
            when (++reads) {
                1 -> FeatureStatus.DOWNLOADABLE
                2 -> FeatureStatus.DOWNLOADING
                else -> FeatureStatus.AVAILABLE
            }
        }, download = { downloads++; flowOf(DownloadStatus.DownloadCompleted) })
        val callback = Callback()
        operations.execute(true, callback)
        runCurrent()
        assertTrue(callback.statuses.isEmpty())
        advanceTimeBy(250); runCurrent()
        assertEquals(FeatureStatus.AVAILABLE, callback.statuses.single().status)
        assertEquals(1, downloads)
        operations.close()
    }

    @Test
    fun closingDuringOwnedDownloadWaitsForCollectionCleanupAndOneTerminal() = runTest {
        val model = Model()
        var released = false
        val operations = operations(model, { FeatureStatus.DOWNLOADABLE }, download = {
            flow {
                try { emit(DownloadStatus.DownloadStarted(10)); awaitCancellation() }
                finally { released = true }
            }
        })
        val callback = Callback { assertTrue(released); assertTrue(model.closed) }
        operations.execute(true, callback)
        runCurrent()
        operations.close()
        assertEquals(7, callback.errors.single().code)
        assertTrue(callback.statuses.isEmpty())
    }

    @Test
    fun cancellationBeforeLaunchAndClientCloseTerminateExactlyOnce() = runTest {
        var creates = 0
        val operations = operations(create = { creates++; Model() })
        val callback = Callback()
        operations.execute(true, callback).cancel()
        runCurrent()
        assertEquals(0, creates)
        assertEquals(7, callback.errors.single().code)
        operations.close()
        val rejected = Callback()
        operations.execute(false, rejected)
        assertEquals(7, rejected.errors.single().code)
        assertEquals(1, callback.errors.size)
    }

    @Test
    fun closingDuringStatusOrDownloadingPollingReleasesModel() = runTest {
        for (prepare in listOf(false, true)) {
            val model = Model()
            val operations = operations(model, status = {
                if (prepare) FeatureStatus.DOWNLOADING else awaitCancellation()
            })
            val callback = Callback { assertTrue(model.closed) }
            operations.execute(prepare, callback)
            runCurrent()
            operations.close()
            assertEquals(7, callback.errors.single().code)
            assertTrue(callback.statuses.isEmpty())
        }
    }

    @Test
    fun generationReadinessGateRejectsEveryNonAvailableValue() {
        requireAvailable(FeatureStatus.AVAILABLE)
        for (status in listOf(FeatureStatus.UNAVAILABLE, FeatureStatus.DOWNLOADABLE, FeatureStatus.DOWNLOADING, 99)) {
            val failure = runCatching { requireAvailable(status) }.exceptionOrNull()
            assertTrue(failure is IllegalStateException)
            assertTrue(failure!!.message!!.contains(featureStatusReason(status)!!))
        }
    }
}
