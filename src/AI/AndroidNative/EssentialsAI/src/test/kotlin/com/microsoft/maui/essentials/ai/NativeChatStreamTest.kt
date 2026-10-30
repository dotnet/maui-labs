package com.microsoft.maui.essentials.ai

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class NativeChatStreamTest {
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
