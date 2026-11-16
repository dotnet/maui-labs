@file:JvmName("NativeStructuredOutput")

package com.microsoft.maui.essentials.ai

import com.google.android.gms.internal.mlkit_genai_prompt.zzaaa
import com.google.android.gms.internal.mlkit_genai_prompt.zzaaf
import com.google.android.gms.internal.mlkit_genai_prompt.zzaap
import com.google.android.gms.internal.mlkit_genai_prompt.zzaaq
import com.google.android.gms.internal.mlkit_genai_prompt.zzacp
import com.google.android.gms.internal.mlkit_genai_prompt.zzacq
import com.google.android.gms.internal.mlkit_genai_prompt.zzaxs
import com.google.android.gms.internal.mlkit_genai_prompt.zzdv
import com.google.android.gms.internal.mlkit_genai_prompt.zzec
import com.google.android.gms.internal.mlkit_genai_prompt.zzed
import com.google.android.gms.internal.mlkit_genai_prompt.zzen
import com.google.android.gms.internal.mlkit_genai_prompt.zzip
import com.google.android.gms.internal.mlkit_genai_prompt.zziq
import com.google.android.gms.internal.mlkit_genai_prompt.zziw
import com.google.android.gms.internal.mlkit_genai_prompt.zziz
import com.google.mlkit.genai.common.StreamingCallback
import com.google.mlkit.genai.prompt.Candidate
import com.google.mlkit.genai.common.FunctionCall
import com.google.mlkit.genai.prompt.GenerateContentRequest
import com.google.mlkit.genai.prompt.GenerateContentResponse
import com.google.mlkit.genai.prompt.GenerationConfig

// ML Kit beta5 has no public runtime-schema API. Its typed API derives an internal
// response schema from a Kotlin class, places it in the AICore request (where AICore
// applies constrained decoding), and appends a schema prompt to the last text part.
// This file does the same from a JSON schema. Internal names are pinned to the
// bundled genai-prompt 1.0.0-beta5 runtime and are re-verified on every SDK update.

fun validateSchema(json: String) {
    NativeResponseSchema.parse(json)
}

/** ML Kit's own schema prompt suffix (the typed API's includeSchemaInPrompt default). */
internal fun schemaPrompt(text: String, schema: zzip): String = zzaap.zze(text, schema)

internal suspend fun countStructuredTokens(processor: NativeSchemaProcessor, request: GenerateContentRequest): Int =
    zzaap.zza.zza(processor.zzJ(zzacq(request, null, false)) as zzen).totalTokens

internal suspend fun generateStructuredContent(
    processor: NativeSchemaProcessor,
    request: GenerateContentRequest,
    callback: StreamingCallback?,
): GenerateContentResponse =
    processor.zzN(zzacq(request, null, false), callback?.let(::ThoughtHeaderFilter)) as GenerateContentResponse

/**
 * The structured-output feature streams thoughts with AICore's raw "thought\n" channel header,
 * which the final candidate omits; drop it so streamed thoughts match the final candidate.
 */
internal class ThoughtHeaderFilter(private val inner: StreamingCallback) : StreamingCallback {
    private val pending = StringBuilder()
    private var started = false

    override fun onNewText(additionalText: String) = inner.onNewText(additionalText)

    override fun onNewFunctionCall(functionCall: FunctionCall) = inner.onNewFunctionCall(functionCall)

    @Synchronized
    override fun onNewThought(additionalThought: String) {
        if (started) return inner.onNewThought(additionalThought)
        pending.append(additionalThought)
        if (pending.length < HEADER.length && HEADER.startsWith(pending)) return
        started = true
        val thought = if (pending.startsWith(HEADER)) pending.substring(HEADER.length) else pending.toString()
        if (thought.isNotEmpty()) inner.onNewThought(thought)
    }

    private companion object {
        const val HEADER = "thought\n"
    }
}

/** Mirrors the SDK's typed-output processor, adding the schema to each AICore request it builds. */
internal class NativeSchemaProcessor(config: GenerationConfig, private val schema: zzip) :
    zzaxs(config.workerExecutor, zzaaq.zzb(config, zzaaa.zzb), -1, zzaaf(config, zzacp.zza)) {

    override fun zzd(client: zzdv, feature: zzed): zzec = zziw.zzz(zziz.zzd(client).zzc(feature).zzd()) as zzec

    override fun zze(request: Any, callback: StreamingCallback?): Any =
        applyResponseSchema(zzaap.zzg(request as zzacq, callback, null), schema)

    override fun zzf(result: Any): Any = zzaap.zzf(result as zziq)
}

// The typed path sets this AICore request field only when it has a class; set it from the JSON schema.
internal fun applyResponseSchema(native: Any, schema: zzip): Any {
    val fields = generateSequence<Class<*>>(native.javaClass) { it.superclass }
        .flatMap { it.declaredFields.asSequence() }
        .filter { it.type == zzip::class.java && !java.lang.reflect.Modifier.isStatic(it.modifiers) }
        .toList()
    check(fields.size == 1) {
        "Gemini Nano structured output does not support this ML Kit runtime " +
            "(expected one native response-schema field, found ${fields.size})."
    }
    fields.single().apply { isAccessible = true }.set(native, schema)
    return native
}

internal fun validateStructuredResponse(schema: NativeResponseSchema, response: NativeChatResponse): String {
    try {
        return schema.validate(response.text)
    } catch (exception: IllegalArgumentException) {
        val truncated = if (response.finishReason == Candidate.FinishReason.MAX_TOKENS) {
            " Generation stopped at the output token limit; increase MaxOutputTokens."
        } else ""
        throw IllegalStateException(
            "Gemini Nano structured output did not match the response schema: ${exception.message}$truncated", exception)
    }
}
