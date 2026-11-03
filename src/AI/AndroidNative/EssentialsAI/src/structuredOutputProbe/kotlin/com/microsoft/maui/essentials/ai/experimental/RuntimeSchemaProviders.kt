package com.microsoft.maui.essentials.ai.experimental

import com.google.mlkit.genai.schema.guided.GenerableDetail
import com.google.mlkit.genai.schema.guided.GenerableProvider
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlin.reflect.KClass

class RuntimeObject0 : LinkedHashMap<String, Any?>()
class RuntimeObject1 : LinkedHashMap<String, Any?>()
class RuntimeObject2 : LinkedHashMap<String, Any?>()
class RuntimeObject3 : LinkedHashMap<String, Any?>()

abstract class RuntimeObjectProvider(private val index: Int) : GenerableProvider {
    override val targetClass: KClass<*> get() = RuntimeSchemaProviders.classes[index]
    override fun getGenerableDetail(): GenerableDetail<*> = RuntimeSchemaProviders.detail(index)
}

class RuntimeObjectProvider0 : RuntimeObjectProvider(0)
class RuntimeObjectProvider1 : RuntimeObjectProvider(1)
class RuntimeObjectProvider2 : RuntimeObjectProvider(2)
class RuntimeObjectProvider3 : RuntimeObjectProvider(3)

internal object RuntimeSchemaProviders {
    val classes = listOf(RuntimeObject0::class, RuntimeObject1::class,
        RuntimeObject2::class, RuntimeObject3::class)
    private val mutex = Mutex()
    @Volatile private var active: RuntimeSchema? = null

    fun detail(index: Int): GenerableDetail<*> =
        checkNotNull(active) { "No runtime structured-output request is active." }.detail(index)

    // The SDK caches provider instances and can query them on different threads
    // before and after suspension. Keep one immutable schema active until parsing ends.
    suspend fun <T> withSchema(schema: RuntimeSchema, action: suspend () -> T): T = mutex.withLock {
        check(active == null) { "A runtime structured-output schema is already active." }
        active = schema
        try {
            action()
        } finally {
            active = null
        }
    }
}
