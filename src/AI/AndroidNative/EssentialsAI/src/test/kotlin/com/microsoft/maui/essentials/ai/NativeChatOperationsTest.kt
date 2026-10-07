package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@OptIn(ExperimentalCoroutinesApi::class)
class NativeChatOperationsTest {
    private class Model(val id: Int = 0, var closed: Boolean = false)

    private class Callback(private val onTerminal: () -> Unit = {}) : NativeChatCallback {
        val text = mutableListOf<String>()
        val thoughts = mutableListOf<String>()
        val responses = mutableListOf<NativeChatResponse>()
        val errors = mutableListOf<NativeChatError>()
        override fun onText(text: String) { this.text.add(text) }
        override fun onThought(thought: String) { thoughts.add(thought) }
        override fun onComplete(response: NativeChatResponse) {
            onTerminal()
            responses.add(response)
        }
        override fun onError(error: NativeChatError) {
            onTerminal()
            errors.add(error)
        }
    }

    private val request = NativeChatRequest(
        arrayOf(NativeChatMessage(0, arrayOf(NativeContentPart(CONTENT_TEXT, "hello", null)))),
        NativeChatOptions(null, null, null, null, null, false),
    )
    private val response = NativeChatResponse("answer", emptyArray(), 0, "test", 1)

    private fun TestScope.operations(
        create: () -> Model = { Model() },
        close: (Model) -> Unit = { it.closed = true },
        infer: suspend (Model, NativeChatRequest, Boolean, NativeChatCallback) -> NativeChatResponse =
            { _, _, _, _ -> response },
    ) = NativeChatOperations(
        create, close, infer,
        mapError = { NativeChatError(it.message ?: "failed", if (it is CancellationException) 7 else 0, null) },
        dispatcher = StandardTestDispatcher(testScheduler),
    )

    @Test
    fun modelCreationFailureTerminatesExactlyOnce() = runTest {
        var closes = 0
        val operations = operations(
            create = { throw IllegalStateException("creation failed") },
            close = { closes++ },
        )
        val callback = Callback()
        operations.generate(request, false, callback)
        runCurrent()

        assertEquals("creation failed", callback.errors.single().message)
        assertTrue(callback.responses.isEmpty())
        assertEquals(0, closes)
        operations.close()
        assertEquals(1, callback.errors.size)
    }

    @Test
    fun linkageFailureBeforeModelCreationIsNotAnEmptySuccess() = runTest {
        val operations = operations(create = { throw NoSuchMethodError("missing native API") })
        val callback = Callback()
        operations.generate(request, false, callback)
        runCurrent()

        assertEquals("missing native API", callback.errors.single().message)
        assertTrue(callback.responses.isEmpty())
        operations.close()
    }

    @Test
    fun successfulResponseFollowsModelCleanup() = runTest {
        val model = Model()
        val operations = operations(create = { model })
        val callback = Callback { assertTrue(model.closed) }
        operations.generate(request, false, callback)
        runCurrent()

        assertSame(response, callback.responses.single())
        assertTrue(callback.errors.isEmpty())
        operations.close()
    }

    @Test
    fun inferenceFailureFollowsModelCleanup() = runTest {
        val model = Model()
        val operations = operations(create = { model }, infer = { _, _, _, _ -> error("inference failed") })
        val callback = Callback { assertTrue(model.closed) }
        operations.generate(request, false, callback)
        runCurrent()

        assertEquals("inference failed", callback.errors.single().message)
        assertTrue(callback.responses.isEmpty())
        operations.close()
    }

    @Test
    fun closeFailureIsReportedInsteadOfSuccess() = runTest {
        val operations = operations(close = { error("close failed") })
        val callback = Callback()
        operations.generate(request, false, callback)
        runCurrent()

        assertEquals("close failed", callback.errors.single().message)
        assertTrue(callback.responses.isEmpty())
        operations.close()
    }

    @Test
    fun cancellationBeforeLaunchDoesNotCreateAModelAndTerminates() = runTest {
        var creates = 0
        val operations = operations(create = { creates++; Model() })
        val callback = Callback()
        operations.generate(request, false, callback).cancel()
        runCurrent()

        assertEquals(0, creates)
        assertEquals(7, callback.errors.single().code)
        assertTrue(callback.responses.isEmpty())
        operations.close()
        assertEquals(1, callback.errors.size)
    }

    @Test
    fun cancellationDuringCreationClosesModelWithoutStartingInference() = runTest {
        val model = Model()
        lateinit var cancellation: NativeCancellation
        var inferences = 0
        val operations = operations(create = { cancellation.cancel(); model },
            infer = { _, _, _, _ -> inferences++; response })
        val callback = Callback { assertTrue(model.closed) }
        cancellation = operations.generate(request, false, callback)
        runCurrent()

        assertEquals(0, inferences)
        assertEquals(7, callback.errors.single().code)
        assertTrue(callback.responses.isEmpty())
        operations.close()
    }

    @Test
    fun cancellationWinsWhenInferenceReturnsWithoutObservingIt() = runTest {
        val model = Model()
        lateinit var cancellation: NativeCancellation
        val operations = operations(create = { model }, infer = { _, _, _, _ ->
            cancellation.cancel()
            response
        })
        val callback = Callback { assertTrue(model.closed) }
        cancellation = operations.generate(request, false, callback)
        runCurrent()

        assertEquals(7, callback.errors.single().code)
        assertTrue(callback.responses.isEmpty())
        operations.close()
    }

    @Test
    fun earlyStreamTerminationClosesModelBeforeTerminalCallback() = runTest {
        val model = Model()
        val operations = operations(create = { model }, infer = { _, _, streaming, progress ->
            assertTrue(streaming)
            progress.onText("first")
            progress.onThought("thinking")
            awaitCancellation()
        })
        val callback = Callback { assertTrue(model.closed) }
        val cancellation = operations.generate(request, true, callback)
        runCurrent()
        assertEquals(listOf("first"), callback.text)
        assertFalse(model.closed)

        cancellation.cancel()
        runCurrent()
        assertEquals(7, callback.errors.single().code)
        assertTrue(callback.responses.isEmpty())
        operations.close()
    }

    @Test
    fun concurrentRequestsOwnIndependentModelsAndCancellation() = runTest {
        val models = mutableListOf<Model>()
        val release = CompletableDeferred<Unit>()
        val operations = operations(create = { Model(models.size).also { models.add(it) } },
            infer = { _, _, _, _ -> release.await(); response })
        val first = Callback { assertTrue(models[0].closed) }
        val second = Callback { assertTrue(models[1].closed) }
        val cancellation = operations.generate(request, false, first)
        operations.generate(request, false, second)
        runCurrent()

        assertEquals(2, models.size)
        assertNotSame(models[0], models[1])
        cancellation.cancel()
        runCurrent()
        assertEquals(7, first.errors.single().code)
        assertFalse(models[1].closed)
        assertTrue(second.errors.isEmpty())
        release.complete(Unit)
        runCurrent()
        assertSame(response, second.responses.single())
        operations.close()
    }

    @Test
    fun closingClientWaitsForAllModelCleanupAndRejectsFurtherRequests() = runTest {
        val models = mutableListOf<Model>()
        val operations = operations(create = { Model().also { models.add(it) } },
            infer = { _, _, _, _ -> awaitCancellation() })
        val first = Callback()
        val second = Callback()
        operations.generate(request, true, first)
        operations.generate(request, false, second)
        runCurrent()

        operations.close()
        assertTrue(models.all { it.closed })
        assertEquals(7, first.errors.single().code)
        assertEquals(7, second.errors.single().code)
        val rejected = Callback()
        operations.generate(request, false, rejected)
        assertEquals(7, rejected.errors.single().code)
        assertEquals(2, models.size)
        operations.close()
    }

    @Test
    fun closingClientBeforeLaunchTerminatesPendingRequests() = runTest {
        var creates = 0
        val operations = operations(create = { creates++; Model() })
        val callback = Callback()
        operations.generate(request, true, callback)

        operations.close()
        assertEquals(0, creates)
        assertEquals(7, callback.errors.single().code)
    }

    @Test
    fun progressAfterTerminationIsIgnored() = runTest {
        lateinit var progress: NativeChatCallback
        val operations = operations(infer = { _, _, _, callback -> progress = callback; response })
        val callback = Callback()
        operations.generate(request, true, callback)
        runCurrent()

        progress.onText("late")
        progress.onThought("late")
        assertTrue(callback.text.isEmpty())
        assertTrue(callback.thoughts.isEmpty())
        assertEquals(1, callback.responses.size)
        operations.close()
    }

    @Test
    fun terminalNotificationWaitsForAnInFlightProgressCallback() = runBlocking {
        val entered = CountDownLatch(1)
        val release = CountDownLatch(1)
        val modelClosed = CompletableDeferred<Unit>()
        val terminal = CompletableDeferred<Unit>()
        val operations = NativeChatOperations(
            createModel = { Model() },
            closeModel = { it.closed = true; modelClosed.complete(Unit) },
            infer = { _, _, _, progress ->
                progress.onText("first")
                assertTrue(entered.await(5, TimeUnit.SECONDS))
                response
            },
            mapError = { NativeChatError(it.message ?: "failed", 0, null) },
            dispatcher = Dispatchers.Default,
        )
        val callback = object : NativeChatCallback {
            override fun onText(text: String) {
                entered.countDown()
                assertTrue(release.await(5, TimeUnit.SECONDS))
            }
            override fun onThought(thought: String) = Unit
            override fun onComplete(response: NativeChatResponse) { terminal.complete(Unit) }
            override fun onError(error: NativeChatError) {
                terminal.completeExceptionally(AssertionError(error.message))
            }
        }
        try {
            operations.generate(request, true, callback)
            withTimeout(5_000) { modelClosed.await() }
            assertFalse(terminal.isCompleted)
            release.countDown()
            withTimeout(5_000) { terminal.await() }
        } finally {
            release.countDown()
            operations.close()
        }
    }
}
