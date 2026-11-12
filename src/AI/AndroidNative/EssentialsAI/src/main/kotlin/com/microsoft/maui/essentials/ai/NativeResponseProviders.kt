package com.microsoft.maui.essentials.ai

import com.google.mlkit.genai.schema.guided.GenerableDetail
import com.google.mlkit.genai.schema.guided.GenerableProvider
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlin.reflect.KClass

class NativeResponseObject0 : LinkedHashMap<String, Any?>()
class NativeResponseObject1 : LinkedHashMap<String, Any?>()
class NativeResponseObject2 : LinkedHashMap<String, Any?>()
class NativeResponseObject3 : LinkedHashMap<String, Any?>()
class NativeResponseObject4 : LinkedHashMap<String, Any?>()
class NativeResponseObject5 : LinkedHashMap<String, Any?>()
class NativeResponseObject6 : LinkedHashMap<String, Any?>()
class NativeResponseObject7 : LinkedHashMap<String, Any?>()
class NativeResponseObject8 : LinkedHashMap<String, Any?>()
class NativeResponseObject9 : LinkedHashMap<String, Any?>()
class NativeResponseObject10 : LinkedHashMap<String, Any?>()
class NativeResponseObject11 : LinkedHashMap<String, Any?>()
class NativeResponseObject12 : LinkedHashMap<String, Any?>()
class NativeResponseObject13 : LinkedHashMap<String, Any?>()
class NativeResponseObject14 : LinkedHashMap<String, Any?>()
class NativeResponseObject15 : LinkedHashMap<String, Any?>()

abstract class NativeResponseProvider(private val index: Int) : GenerableProvider {
    override val targetClass: KClass<*> get() = NativeResponseProviders.classes[index]
    override fun getGenerableDetail(): GenerableDetail<*> = NativeResponseProviders.detail(index)
}

class NativeResponseProvider0 : NativeResponseProvider(0)
class NativeResponseProvider1 : NativeResponseProvider(1)
class NativeResponseProvider2 : NativeResponseProvider(2)
class NativeResponseProvider3 : NativeResponseProvider(3)
class NativeResponseProvider4 : NativeResponseProvider(4)
class NativeResponseProvider5 : NativeResponseProvider(5)
class NativeResponseProvider6 : NativeResponseProvider(6)
class NativeResponseProvider7 : NativeResponseProvider(7)
class NativeResponseProvider8 : NativeResponseProvider(8)
class NativeResponseProvider9 : NativeResponseProvider(9)
class NativeResponseProvider10 : NativeResponseProvider(10)
class NativeResponseProvider11 : NativeResponseProvider(11)
class NativeResponseProvider12 : NativeResponseProvider(12)
class NativeResponseProvider13 : NativeResponseProvider(13)
class NativeResponseProvider14 : NativeResponseProvider(14)
class NativeResponseProvider15 : NativeResponseProvider(15)

internal object NativeResponseProviders {
    val classes = listOf(NativeResponseObject0::class, NativeResponseObject1::class,
        NativeResponseObject2::class, NativeResponseObject3::class, NativeResponseObject4::class,
        NativeResponseObject5::class, NativeResponseObject6::class, NativeResponseObject7::class,
        NativeResponseObject8::class, NativeResponseObject9::class, NativeResponseObject10::class,
        NativeResponseObject11::class, NativeResponseObject12::class, NativeResponseObject13::class,
        NativeResponseObject14::class, NativeResponseObject15::class)
    private val mutex = Mutex()
    @Volatile private var active: NativeResponseSchema? = null

    fun detail(index: Int): GenerableDetail<*> =
        checkNotNull(active) { "No structured-output request is active." }.detail(index)

    // The SDK caches provider instances and can query them on different threads
    // before and after suspension. Keep one immutable schema active until parsing ends.
    suspend fun <T> withSchema(schema: NativeResponseSchema?, action: suspend () -> T): T {
        if (schema == null) return action()
        return mutex.withLock {
            check(active == null) { "A structured-output schema is already active." }
            active = schema
            try {
                action()
            } finally {
                active = null
            }
        }
    }
}
