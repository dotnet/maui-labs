# The SDK discovers KSP-generated providers and reads the annotated Kotlin shape.
-keepattributes RuntimeVisibleAnnotations,RuntimeInvisibleAnnotations,Signature,InnerClasses,EnclosingMethod
-keep class com.microsoft.maui.essentials.ai.experimental.SentimentProbe { *; }
-keep class * implements com.google.mlkit.genai.schema.guided.GenerableProvider { *; }
