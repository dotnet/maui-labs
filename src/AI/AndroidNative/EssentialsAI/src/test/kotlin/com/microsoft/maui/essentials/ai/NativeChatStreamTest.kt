package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.yield
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class NativeChatStreamTest {
    @Test
    fun toolStreamsSuppressRawControlTokensAndUseOnlyFinalDisplayText() = runBlocking {
        val callback = Callback()
        val stream = NativeChatStream(this, callback, "native-model", deferToolText = true)
        stream.onNewText("<ctrl42>call:lookup{key:<ctrl46>orchard<ctrl46>}<ctrl43>")
        yield()
        assertTrue(callback.events.isEmpty())
        stream.finish(response(""))
        assertTrue(callback.events.isEmpty())

        val answer = NativeChatStream(this, callback, "native-model", deferToolText = true)
        answer.onNewText("secret")
        yield()
        assertTrue(callback.events.isEmpty())
        answer.finish(response("secret"))
        assertEquals(listOf(Triple(false, "secret", "native-model")), callback.events)
    }

    private class Callback : NativeChatCallback {
        val events = mutableListOf<Triple<Boolean, String, String>>()

        override fun onText(text: String, modelName: String) {
            events.add(Triple(false, text, modelName))
        }

        override fun onThought(thought: String, modelName: String) {
            events.add(Triple(true, thought, modelName))
        }

        override fun onComplete(response: NativeChatResponse) = error("Unexpected terminal callback.")
        override fun onError(error: NativeChatError) = error("Unexpected terminal callback.")
    }

    private fun response(text: String, thoughts: Array<String> = emptyArray()) =
        NativeChatResponse(text, thoughts, 0, "native-model", 1)

    @Test
    fun deltasDrainInOrderWithoutRepeatingTheFinalCandidate() = runBlocking {
        val callback = Callback()
        val stream = NativeChatStream(this, callback, "native-model")

        stream.onNewText("ha")
        stream.onNewThought("first")
        stream.onNewText("ha")
        stream.onNewThought("second")
        stream.finish(response("haha", arrayOf("first", "second")))

        assertEquals(
            listOf(
                Triple(false, "ha", "native-model"),
                Triple(true, "first", "native-model"),
                Triple(false, "ha", "native-model"),
                Triple(true, "second", "native-model"),
            ),
            callback.events,
        )
    }

    @Test
    fun finishEmitsMissingTextAndThoughtSuffixes() = runBlocking {
        val callback = Callback()
        val stream = NativeChatStream(this, callback, "native-model")
        stream.onNewText("hello")
        stream.onNewThought("think")

        stream.finish(response("hello world", arrayOf("thinking")))

        assertEquals(
            listOf(
                Triple(false, "hello", "native-model"),
                Triple(true, "think", "native-model"),
                Triple(false, " world", "native-model"),
                Triple(true, "ing", "native-model"),
            ),
            callback.events,
        )
    }

    @Test
    fun trailingWhitespaceWaitsForMoreContentWithoutMixingTextAndThoughts() = runBlocking {
        val callback = Callback()
        val stream = NativeChatStream(this, callback, "native-model")
        stream.onNewText(" \n")
        stream.onNewThought("\t")
        stream.onNewText("")
        yield()
        assertTrue(callback.events.isEmpty())

        stream.onNewText("hello ")
        stream.onNewThought("think\n")
        yield()
        assertEquals(
            listOf(
                Triple(false, " \nhello", "native-model"),
                Triple(true, "\tthink", "native-model"),
            ),
            callback.events,
        )

        stream.onNewText("\n")
        stream.onNewThought("\t")
        yield()
        assertEquals(2, callback.events.size)
        stream.onNewText("world! \n")
        stream.onNewThought("more\t")
        stream.finish(response(" \nhello \nworld!", arrayOf("\tthink\n\tmore")))

        assertEquals(
            listOf(
                Triple(false, " \nhello", "native-model"),
                Triple(true, "\tthink", "native-model"),
                Triple(false, " \nworld!", "native-model"),
                Triple(true, "\n\tmore", "native-model"),
            ),
            callback.events,
        )
    }

    @Test
    fun finishUsesFinalWhitespaceWithoutEmittingDiscardedWhitespace() = runBlocking {
        for ((streamed, final) in listOf(
            "hello\n\n\n\n" to "hello",
            "hello\n" to "hello ",
            "hello\n" to "hello \n\n",
            "hello\n" to "hello\n",
            "hello  " to "hello  world  ",
        )) {
            val callback = Callback()
            val stream = NativeChatStream(this, callback, "native-model")
            stream.onNewText(streamed)
            stream.onNewThought(streamed)

            stream.finish(response(final, arrayOf(final)))

            assertEquals(Triple(false, "hello", "native-model"), callback.events[0])
            assertEquals(Triple(true, "hello", "native-model"), callback.events[1])
            assertEquals(final, callback.events.filter { !it.first }.joinToString("") { it.second })
            assertEquals(final, callback.events.filter { it.first }.joinToString("") { it.second })
            assertEquals(if (final == "hello") 2 else 4, callback.events.size)
        }
    }

    @Test
    fun whitespaceOnlyStreamIsDiscardedWhenAbsentFromFinalResponse() = runBlocking {
        val callback = Callback()
        val stream = NativeChatStream(this, callback, "native-model")
        stream.onNewText(" \t\n")
        stream.onNewThought("\n\n")

        stream.finish(response(""))

        assertTrue(callback.events.isEmpty())
    }

    @Test
    fun finalCandidateMissingNonWhitespaceStillFails() = runBlocking {
        val callback = Callback()
        val stream = NativeChatStream(this, callback, "native-model")
        stream.onNewText("hello world")

        assertTrue(runCatching {
            stream.finish(response("hello"))
        }.exceptionOrNull() is IllegalStateException)
        stream.cancel()
    }

    @Test
    fun divergentFinalTextOrThoughtFails() = runBlocking {
        for ((finalText, finalThought) in listOf("different" to "thinking", "hello" to "different")) {
            val callback = Callback()
            val stream = NativeChatStream(this, callback, "native-model")
            stream.onNewText("hello")
            stream.onNewThought("thinking")

            assertTrue(runCatching {
                stream.finish(response(finalText, arrayOf(finalThought)))
            }.exceptionOrNull() is IllegalStateException)
            stream.cancel()
        }
    }

    @Test
    fun finishWaitsForAnInFlightCallback() = runBlocking {
        val entered = CountDownLatch(1)
        val release = CountDownLatch(1)
        val callback = object : NativeChatCallback {
            override fun onText(text: String, modelName: String) {
                entered.countDown()
                assertTrue(release.await(5, TimeUnit.SECONDS))
            }
            override fun onThought(thought: String, modelName: String) = Unit
            override fun onComplete(response: NativeChatResponse) = Unit
            override fun onError(error: NativeChatError) = Unit
        }
        val stream = NativeChatStream(CoroutineScope(Dispatchers.Default), callback, "model")
        try {
            stream.onNewText("first")
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            val finish = async(Dispatchers.Default) { stream.finish(response("first")) }
            assertFalse(finish.isCompleted)
            release.countDown()
            withTimeout(5_000) { finish.await() }
        } finally {
            release.countDown()
        }
    }

    @Test
    fun cancelWaitsForAnInFlightCallbackAndIgnoresLateEvents() = runBlocking {
        val entered = CountDownLatch(1)
        val release = CountDownLatch(1)
        val callback = object : NativeChatCallback {
            val text = mutableListOf<String>()
            override fun onText(text: String, modelName: String) {
                this.text.add(text)
                entered.countDown()
                assertTrue(release.await(5, TimeUnit.SECONDS))
            }
            override fun onThought(thought: String, modelName: String) = Unit
            override fun onComplete(response: NativeChatResponse) = Unit
            override fun onError(error: NativeChatError) = Unit
        }
        val stream = NativeChatStream(CoroutineScope(Dispatchers.Default), callback, "model")
        try {
            stream.onNewText("first")
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            val cancel = async(Dispatchers.Default) { stream.cancel() }
            assertFalse(cancel.isCompleted)
            release.countDown()
            withTimeout(5_000) { cancel.await() }
            stream.onNewText("late")
            assertEquals(listOf("first"), callback.text)
        } finally {
            release.countDown()
        }
    }
}
