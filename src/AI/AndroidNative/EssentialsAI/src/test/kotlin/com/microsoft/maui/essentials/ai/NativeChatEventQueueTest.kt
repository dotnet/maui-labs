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

class NativeChatEventQueueTest {
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

    @Test
    fun textAndThoughtEventsDrainInOrderWithTheirModelNames() = runBlocking {
        val callback = Callback()
        val queue = NativeChatEventQueue(this, callback)

        queue.callback.onText("first", "model-a")
        queue.callback.onThought("thinking", "model-a")
        queue.callback.onText("second", "model-b")
        queue.finish()

        assertEquals(
            listOf(
                Triple(false, "first", "model-a"),
                Triple(true, "thinking", "model-a"),
                Triple(false, "second", "model-b"),
            ),
            callback.events,
        )
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
        val scope = CoroutineScope(Dispatchers.Default)
        val queue = NativeChatEventQueue(scope, callback)
        try {
            queue.callback.onText("first", "model")
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            val finish = async(Dispatchers.Default) { queue.finish() }
            assertFalse(finish.isCompleted)
            release.countDown()
            withTimeout(5_000) { finish.await() }
        } finally {
            release.countDown()
        }
    }

    @Test
    fun cancelWaitsForAnInFlightCallback() = runBlocking {
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
        val queue = NativeChatEventQueue(CoroutineScope(Dispatchers.Default), callback)
        try {
            queue.callback.onText("first", "model")
            assertTrue(entered.await(5, TimeUnit.SECONDS))
            val cancel = async(Dispatchers.Default) { queue.cancel() }
            assertFalse(cancel.isCompleted)
            release.countDown()
            withTimeout(5_000) { cancel.await() }
        } finally {
            release.countDown()
        }
    }

    @Test
    fun eventsAfterFinishAreIgnored() = runBlocking {
        val callback = Callback()
        val queue = NativeChatEventQueue(this, callback)
        queue.callback.onText("before", "model")
        queue.finish()

        queue.callback.onText("late", "model")
        queue.callback.onThought("late", "model")

        assertEquals(listOf(Triple(false, "before", "model")), callback.events)
    }
}
