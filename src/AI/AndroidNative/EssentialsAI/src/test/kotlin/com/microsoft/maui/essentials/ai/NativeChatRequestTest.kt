package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.prompt.TextPart
import org.junit.Assert.*
import org.junit.Test

class NativeChatRequestTest {
    private fun message(role: String, vararg text: String) = NativeChatMessage(
        role, text.map { NativeContentPart(CONTENT_TEXT, it, null) }.toTypedArray(),
    )

    private fun request(
        vararg messages: NativeChatMessage,
        options: NativeChatOptions = NativeChatOptions(null, null, null, null, false),
    ) = NativeChatRequest(arrayOf(*messages), options    )

    @Test
    fun userContentsAndPartsArePassedToNativeWithoutRoleTokens() {
        val result = request(
            message("user", "hello", "world"),
            message("user", "second content"),
        ).toNative()

        assertEquals(listOf(listOf("hello", "world"), listOf("second content")),
            result.contents.map { content -> content.parts.map { (it as TextPart).textString } })
        assertNull(result.systemInstruction)
        assertFalse(result.enableThinking)
    }

    @Test
    fun userAssistantConversationIsIncludedInOrderAsOrdinaryPromptText() {
        val result = request(
            message("system", "Be concise."),
            message("user", "hello", " world"),
            message("assistant", "Hello!"),
            message("user", "What did I say?"),
        ).toNative()

        val prompt = result.contents.flatMap { it.parts }
            .joinToString("") { (it as TextPart).textString }
        assertEquals("User:\nhello world\n\nAssistant:\nHello!\n\nUser:\nWhat did I say?\n\n", prompt)
        assertEquals("Be concise.", result.systemInstruction!!.textString)
        assertFalse(prompt.contains("<|"))
    }

    @Test
    fun caseVariantStandardRolesRetainInstructionsAndToolHistory() {
        val result = request(
            message("SYSTEM", "Be concise."),
            message("ASSISTANT", "Earlier answer."),
            message("TOOL", """{"type":"function_result","callId":"call-1","result":"ok"}"""),
            message("USER", "Continue."),
        ).toNative()

        assertEquals("Be concise.", result.systemInstruction!!.textString)
        val prompt = result.contents.flatMap { it.parts }
            .joinToString("") { (it as TextPart).textString }
        assertTrue(prompt.contains("Assistant:\nEarlier answer."))
        assertTrue(prompt.contains("Tool:\n"))
        assertTrue(prompt.contains("\"callId\":\"call-1\""))
        assertTrue(prompt.endsWith("User:\nContinue.\n\n"))
    }

    @Test
    fun prependedOptionsInstructionsAndSystemPartsKeepTheirOrder() {
        val result = request(
            message("system", "options instructions"),
            message("system", "first system", "second part"),
            message("user", "hello"),
            message("system", "last system"),
        ).toNative()

        assertEquals("options instructions\n\nfirst systemsecond part\n\nlast system",
            result.systemInstruction!!.textString)
        assertEquals("hello", (result.contents.single().parts.single() as TextPart).textString)
    }

    @Test
    fun thinkingMapsToTheNativeFlag() {
        val request = request(message("user", "hello"),
            options = NativeChatOptions(null, null, null, null, true))

        assertTrue(request.toNative().enableThinking)
    }

    @Test
    fun toolHistoryIsTextualAndUnknownRolesAreIgnored() {
        val result = request(message("user", "hello"),
            message("assistant", """{"type":"function_call","callId":"1","name":"sample","arguments":{}}"""),
            message("tool", """{"type":"function_result","callId":"1","result":"ok"}"""),
            message("custom", "ignored"), message("developer", "ignored"),
            message("user", "next")).toNative()
        val prompt = result.contents.flatMap { it.parts }.joinToString("") { (it as TextPart).textString }
        assertEquals("User:\nhello\n\nAssistant:\n" +
            """{"type":"function_call","callId":"1","name":"sample","arguments":{}}""" +
            "\n\nTool:\n" + """{"type":"function_result","callId":"1","result":"ok"}""" +
            "\n\nUser:\nnext\n\n", prompt)
    }

    @Test
    fun blankSystemChunksAreOmittedButBlankConversationTurnsRemain() {
        val result = request(message("system", " \n", "\t"), message("system"),
            message("user"), message("assistant", ""), message("user", "next"))
            .toNative()
        assertNull(result.systemInstruction)
        assertEquals(3, result.contents.size)
        assertEquals("User:\n\n\nAssistant:\n\n\nUser:\nnext\n\n",
            result.contents.flatMap { it.parts }.joinToString("") { (it as TextPart).textString })
    }

    @Test
    fun systemImagesMissingImageDataAndUnknownPartsAreRejected() {
        for (message in listOf(
            NativeChatMessage("system", arrayOf(NativeContentPart(CONTENT_IMAGE, null, byteArrayOf(1)))),
            NativeChatMessage("user", arrayOf(NativeContentPart(CONTENT_IMAGE, null, null))),
            NativeChatMessage("user", arrayOf(NativeContentPart(123, "unknown", null))),
        )) {
            assertNotNull(runCatching {
                request(message, message("user", "hello")).toNative()
            }.exceptionOrNull())
        }
    }

    @Test
    fun conversationContentIsRequired() {
        for (request in listOf(request(), request(message("system", "instructions")))) {
            assertTrue(runCatching { request.toNative() }
                .exceptionOrNull() is IllegalArgumentException)
        }
    }

    @Test
    fun supportedGenerationOptionsMapDirectlyToTheNativeRequest() {
        val result = request(message("user", "hello"),
            options = NativeChatOptions(0.3f, 12, 42, 128, false))
            .toNative()

        assertEquals(0.3f, result.temperature, 0f)
        assertEquals(12, result.topK)
        assertEquals(42, result.seed)
        assertEquals(128, result.maxOutputTokens)
    }

    @Test
    fun nativeBuilderOwnsGenerationValueValidation() {
        val error = runCatching {
            request(message("user", "hello"),
                options = NativeChatOptions(2f, null, null, null, false))
                .toNative()
        }.exceptionOrNull()

        assertTrue(error is IllegalArgumentException)
        assertEquals("temperature must be between 0 and 1", error!!.message)
    }
}
