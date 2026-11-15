-keep class com.microsoft.maui.essentials.ai.GeminiNanoNativeClient { *; }
-keep class com.microsoft.maui.essentials.ai.NativeChatRequest { *; }
-keep class com.microsoft.maui.essentials.ai.NativeChatMessage { *; }
-keep class com.microsoft.maui.essentials.ai.NativeChatOptions { *; }
-keep class com.microsoft.maui.essentials.ai.NativeContentPart { *; }
-keep class com.microsoft.maui.essentials.ai.NativeChatResponse { *; }
-keep class com.microsoft.maui.essentials.ai.NativeChatError { *; }
-keep class com.microsoft.maui.essentials.ai.NativeCancellation { *; }
-keep interface com.microsoft.maui.essentials.ai.NativeChatCallback { *; }
# Structured output sets ML Kit's internal AICore response-schema field by type.
-keepclassmembers class * extends com.google.android.gms.internal.mlkit_genai_prompt.zzim {
    com.google.android.gms.internal.mlkit_genai_prompt.zzip *;
}
