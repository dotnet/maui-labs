buildscript {
    if (providers.gradleProperty("enableGeminiStructuredOutputProbe").orNull == "true") {
        repositories {
            google()
            mavenCentral()
            gradlePluginPortal()
        }
        dependencies {
            classpath("com.google.devtools.ksp:com.google.devtools.ksp.gradle.plugin:2.3.6")
        }
    }
}

plugins {
    id("com.android.library") version "8.12.0"
    id("org.jetbrains.kotlin.android") version "2.4.0"
}

val enableGeminiStructuredOutputProbe =
    providers.gradleProperty("enableGeminiStructuredOutputProbe").orNull == "true"

if (enableGeminiStructuredOutputProbe) {
    apply(plugin = "com.google.devtools.ksp")
    // Never replace the default AAR/runtime exports consumed by AndroidGradleProject.
    layout.buildDirectory.set(layout.projectDirectory.dir("build/structured-output-probe"))
}

android {
    namespace = "com.microsoft.maui.essentials.ai"
    compileSdk = 36

    defaultConfig {
        minSdk = 26
        consumerProguardFiles("consumer-rules.pro")
        if (enableGeminiStructuredOutputProbe) {
            consumerProguardFiles("src/structuredOutputProbe/consumer-rules.pro")
            testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        }
    }

    if (enableGeminiStructuredOutputProbe) {
        sourceSets {
            getByName("main").java.srcDir("src/structuredOutputProbe/kotlin")
            getByName("test").java.srcDir("src/structuredOutputProbeTest/kotlin")
            getByName("androidTest").java.srcDir("src/structuredOutputProbeAndroidTest/kotlin")
            getByName("androidTest").manifest.srcFile("src/structuredOutputProbeAndroidTest/AndroidManifest.xml")
        }
        packaging.resources.merges.add("META-INF/services/com.google.mlkit.genai.schema.guided.GenerableProvider")
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

val mlKitRuntime by configurations.creating {
    isCanBeConsumed = false
    isTransitive = false
}

// Keep handwritten SPI resources separate so KSP's resource copy cannot overwrite them.
val runtimeSchemaServices = if (enableGeminiStructuredOutputProbe) {
    tasks.register<Jar>("runtimeSchemaServices") {
        from("src/structuredOutputProbe/resources")
        archiveFileName.set("runtime-schema-services.jar")
        destinationDirectory.set(layout.buildDirectory.dir("runtime-schema-services"))
    }
} else null

dependencies {
    compileOnly("com.google.mlkit:genai-prompt:1.0.0-beta5")
    compileOnly("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.11.0")
    mlKitRuntime("com.google.mlkit:genai-prompt:1.0.0-beta5")
    mlKitRuntime("com.google.mlkit:genai-common:1.0.0-beta5")
    mlKitRuntime("com.google.mlkit:genai-schema:1.0.0-alpha2")
    testImplementation("junit:junit:4.13.2")
    testImplementation("com.google.mlkit:genai-prompt:1.0.0-beta5")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.11.0")
    testImplementation("org.json:json:20250517")
    if (enableGeminiStructuredOutputProbe) {
        runtimeOnly(files(checkNotNull(runtimeSchemaServices)))
        compileOnly("com.google.mlkit:genai-schema:1.0.0-alpha2")
        add("ksp", "com.google.mlkit:genai-schema-compiler:1.0.0-alpha2")
        androidTestImplementation("androidx.test.ext:junit:1.3.0")
        androidTestImplementation("androidx.test:runner:1.7.0")
        androidTestImplementation("com.google.mlkit:genai-prompt:1.0.0-beta5")
        androidTestImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.11.0")
    }
}

// Shared Kotlin, AndroidX and Google dependencies are supplied by NuGet.
val exportMlKitRuntime = tasks.register<Sync>("exportMlKitRuntime") {
    from(mlKitRuntime)
    into(layout.buildDirectory.dir("outputs/mlkit-runtime"))
}

tasks.matching { it.name == "assembleRelease" }.configureEach {
    dependsOn("testReleaseUnitTest", exportMlKitRuntime)
}
