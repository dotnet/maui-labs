plugins {
    id("com.android.library") version "8.12.0"
    id("org.jetbrains.kotlin.android") version "2.3.0"
}

android {
    namespace = "com.microsoft.maui.essentials.ai"
    compileSdk = 36

    defaultConfig {
        minSdk = 26
        consumerProguardFiles("consumer-rules.pro")
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
    jvmToolchain(17)
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
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.10.2")
}

// Shared Kotlin, AndroidX and Google dependencies are supplied by NuGet.
val exportMlKitRuntime = tasks.register<Sync>("exportMlKitRuntime") {
    from(mlKitRuntime)
    into(layout.buildDirectory.dir("outputs/mlkit-runtime"))
}

tasks.matching { it.name == "assembleRelease" }.configureEach {
    dependsOn("testReleaseUnitTest", exportMlKitRuntime)
}
