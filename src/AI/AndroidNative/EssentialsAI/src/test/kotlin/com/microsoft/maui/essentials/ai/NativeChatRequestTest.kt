package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.prompt.TextPart
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class NativeChatRequestTest {
    private fun message(role: String, vararg text: String) = NativeChatMessage(
        role, text.map { NativeContentPart(CONTENT_TEXT, it, null) }.toTypedArray(),
    )

    private fun request(
        vararg messages: NativeChatMessage,
        options: NativeChatOptions = NativeChatOptions(null, null, null, null, false),
    ) = NativeChatRequest(arrayOf(*messages), options)

    @Test
    fun userContentsAndPartsArePassedToNativeWithoutRoleTokens() = runBlocking {
        val result = request(
            message("user", "hello", "world"),
            message("user", "second content"),
        ).toNative({ error("System capability should not be queried.") },
            { error("Thinking capability should not be queried.") })

        assertEquals(listOf(listOf("hello", "world"), listOf("second content")),
            result.contents.map { content -> content.parts.map { (it as TextPart).textString } })
        assertNull(result.systemInstruction)
        assertFalse(result.enableThinking)
    }

    @Test
    fun userAssistantConversationIsIncludedInOrderAsOrdinaryPromptText() = runBlocking {
        val result = request(
            message("system", "Be concise."),
            message("user", "hello", " world"),
            message("assistant", "Hello!"),
            message("user", "What did I say?"),
        ).toNative({ true }, { false })

        val prompt = result.contents.flatMap { it.parts }
            .joinToString("") { (it as TextPart).textString }
        assertEquals("User:\nhello world\n\nAssistant:\nHello!\n\nUser:\nWhat did I say?\n\n", prompt)
        assertEquals("Be concise.", result.systemInstruction!!.textString)
        assertFalse(prompt.contains("<|"))
    }

    @Test
    fun prependedOptionsInstructionsAndSystemPartsKeepTheirOrder() = runBlocking {
        val result = request(
            message("system", "options instructions"),
            message("system", "first system", "second part"),
            message("user", "hello"),
            message("system", "last system"),
        ).toNative({ true }, { false })

        assertEquals("options instructions\n\nfirst system\n\nsecond part\n\nlast system",
            result.systemInstruction!!.textString)
        assertEquals("hello", (result.contents.single().parts.single() as TextPart).textString)
    }

    @Test
    fun unavailableNativeSystemInstructionsFailWithoutPromptFallback() = runBlocking {
        val error = runCatching {
            request(message("system", "instructions"), message("user", "hello"))
                .toNative({ false }, { true })
        }.exceptionOrNull()

        assertTrue(error is IllegalStateException)
        assertTrue(error!!.message!!.contains("native system instructions"))
    }

    @Test
    fun thinkingMapsToTheNativeFlagAndFailsWhenUnavailable() = runBlocking {
        val request = request(message("user", "hello"),
            options = NativeChatOptions(null, null, null, null, true))

        assertTrue(request.toNative({ false }, { true }).enableThinking)
        val error = runCatching { request.toNative({ true }, { false }) }.exceptionOrNull()
        assertTrue(error is IllegalStateException)
        assertTrue(error!!.message!!.contains("thinking mode"))
    }

    @Test
    fun toolAndCustomRolesAreRejectedEvenInEmptyHistoryEntries() = runBlocking {
        for (role in listOf("tool", "custom")) {
            for (parts in listOf(emptyArray(), arrayOf(NativeContentPart(CONTENT_TEXT, "history", null)))) {
                val error = runCatching {
                    request(message("user", "hello"), NativeChatMessage(role, parts), message("user", "next"))
                        .toNative({ true }, { true })
                }.exceptionOrNull()
                assertTrue(error is IllegalStateException)
                assertTrue(error!!.message!!.contains("chat role '$role'"))
            }
        }
    }

    @Test
    fun systemImagesMissingImageDataAndUnknownPartsAreRejected() = runBlocking {
        for (message in listOf(
            NativeChatMessage("system", arrayOf(NativeContentPart(CONTENT_IMAGE, null, byteArrayOf(1)))),
            NativeChatMessage("user", arrayOf(NativeContentPart(CONTENT_IMAGE, null, null))),
            NativeChatMessage("user", arrayOf(NativeContentPart(123, "unknown", null))),
        )) {
            assertNotNull(runCatching {
                request(message, message("user", "hello")).toNative({ true }, { true })
            }.exceptionOrNull())
        }
    }

    @Test
    fun conversationContentIsRequired() = runBlocking {
        for (request in listOf(request(), request(message("system", "instructions")))) {
            assertTrue(runCatching { request.toNative({ true }, { true }) }
                .exceptionOrNull() is IllegalArgumentException)
        }
    }

    @Test
    fun supportedGenerationOptionsMapDirectlyToTheNativeRequest() = runBlocking {
        val result = request(message("user", "hello"),
            options = NativeChatOptions(0.3f, 12, 42, 128, false))
            .toNative({ false }, { false })

        assertEquals(0.3f, result.temperature, 0f)
        assertEquals(12, result.topK)
        assertEquals(42, result.seed)
        assertEquals(128, result.maxOutputTokens)
    }

    @Test
    fun nativeBuilderOwnsGenerationValueValidation() = runBlocking {
        val error = runCatching {
            request(message("user", "hello"),
                options = NativeChatOptions(2f, null, null, null, false))
                .toNative({ false }, { false })
        }.exceptionOrNull()

        assertTrue(error is IllegalArgumentException)
        assertEquals("temperature must be between 0 and 1", error!!.message)
    }
}
