package com.microsoft.maui.essentials.ai

import org.junit.Assert.*
import org.junit.Test

class NativeStreamAccumulatorTest {
    private class Callback : NativeChatCallback {
        val text = mutableListOf<String>()
        val thoughts = mutableListOf<String>()
        val models = mutableListOf<String>()
        override fun onText(text: String, modelName: String) { this.text.add(text); models.add(modelName) }
        override fun onThought(thought: String, modelName: String) { thoughts.add(thought); models.add(modelName) }
        override fun onComplete(response: NativeChatResponse) = error("Not accumulator's responsibility.")
        override fun onError(error: NativeChatError) = error("Not accumulator's responsibility.")
    }

    @Test
    fun preservesRepeatedAppendOnlyDeltasAndDoesNotRepeatFinalCandidate() {
        val callback = Callback()
        val accumulator = NativeStreamAccumulator(callback, "native-model")
        accumulator.onText("ha")
        accumulator.onThought("first")
        accumulator.onText("ha")
        accumulator.onThought("second")
        accumulator.finish(NativeChatResponse("haha", emptyArray(), 0, "native-model", 1))
        assertEquals(listOf("ha", "ha"), callback.text)
        assertEquals(listOf("first", "second"), callback.thoughts)
        assertEquals(List(4) { "native-model" }, callback.models)
    }

    @Test
    fun emitsOnlyTheMissingSuffixOrEntireCandidateWhenNoDeltasArrived() {
        for (prefix in listOf("", "hello")) {
            val callback = Callback()
            val accumulator = NativeStreamAccumulator(callback, "native-model")
            if (prefix.isNotEmpty()) accumulator.onText(prefix)
            accumulator.finish(NativeChatResponse("hello world", emptyArray(), 0, "native-model", 1))
            assertEquals("hello world", callback.text.joinToString(""))
            assertEquals(if (prefix.isEmpty()) listOf("hello world") else listOf("hello", " world"), callback.text)
        }
    }

    @Test
    fun divergentOrShorterFinalCandidateFailsInsteadOfDuplicatingOrRewritingOutput() {
        for (final in listOf("different", "hel")) {
            val callback = Callback()
            val accumulator = NativeStreamAccumulator(callback, "native-model")
            accumulator.onText("hello")
            assertTrue(runCatching {
                accumulator.finish(NativeChatResponse(final, emptyArray(), 0, "native-model", 1))
            }.exceptionOrNull() is IllegalStateException)
            assertEquals(listOf("hello"), callback.text)
        }
    }
}
