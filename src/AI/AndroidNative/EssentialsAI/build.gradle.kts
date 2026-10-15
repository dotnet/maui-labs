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
    id("org.jetbrains.kotlin.android") version "2.3.0"
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
        }
    }

    if (enableGeminiStructuredOutputProbe) {
        sourceSets {
            getByName("main").java.srcDir("src/structuredOutputProbe/kotlin")
            getByName("test").java.srcDir("src/structuredOutputProbeTest/kotlin")
        }
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

dependencies {
    compileOnly("com.google.mlkit:genai-prompt:1.0.0-beta4")
    compileOnly("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.10.2")
    mlKitRuntime("com.google.mlkit:genai-prompt:1.0.0-beta4")
    mlKitRuntime("com.google.mlkit:genai-common:1.0.0-beta4")
    mlKitRuntime("com.google.mlkit:genai-schema:1.0.0-alpha1")
    testImplementation("junit:junit:4.13.2")
    testImplementation("com.google.mlkit:genai-prompt:1.0.0-beta4")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.10.2")
    if (enableGeminiStructuredOutputProbe) {
        compileOnly("com.google.mlkit:genai-schema:1.0.0-alpha1")
        add("ksp", "com.google.mlkit:genai-schema-compiler:1.0.0-alpha1")
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
