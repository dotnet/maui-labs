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

dependencies {
    compileOnly("com.google.mlkit:genai-prompt:1.0.0-beta4")
    compileOnly("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.10.2")
}
