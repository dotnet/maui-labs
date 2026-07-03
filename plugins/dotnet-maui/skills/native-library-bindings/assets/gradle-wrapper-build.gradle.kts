// app/build.gradle.kts (or <module>/build.gradle.kts) for the Kotlin/Java
// wrapper module that will be built into an AAR and bound from a .NET Android
// binding project. Adapt namespace, SDK versions, and dependencies to the
// vendor SDK being wrapped.
//
// Companion settings.gradle.kts for the wrapper project (create alongside
// this file, not part of the module build script):
//
// pluginManagement {
//     repositories {
//         google()
//         mavenCentral()
//         gradlePluginPortal()
//     }
// }
//
// dependencyResolutionManagement {
//     repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
//     repositories {
//         google()
//         mavenCentral()
//         // Add vendor-specific repositories here, e.g.:
//         // maven { url = uri("https://jitpack.io") }
//     }
// }
//
// rootProject.name = "ContosoSdkWrapper"
// include(":app")

plugins {
    id("com.android.library") version "8.7.3"
    id("org.jetbrains.kotlin.android") version "2.0.21"
}

android {
    namespace = "com.contoso.binding"
    compileSdk = 35

    defaultConfig {
        minSdk = 23

        // Keep rules for consumers of this AAR when it is packaged into an app;
        // ship one even if empty, so obfuscation/shrinking of the consuming app
        // doesn't strip wrapper entry points.
        consumerProguardFiles("consumer-rules.pro")
    }

    buildTypes {
        release {
            // Do not shrink/obfuscate the wrapper itself — let the consuming
            // app's own release build type decide whether to minify.
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

// Dependency documentation table (keep in sync with the vendor SDK's own
// release notes so upstream updates are easy to diff):
//
// | Dependency                          | Purpose                          | Notes                          |
// |--------------------------------------|-----------------------------------|---------------------------------|
// | com.contoso:sdk:1.2.3                 | Vendor SDK being wrapped          | Pin the exact version; re-check
// |                                        |                                    | transitive deps on every bump. |
// | androidx.annotation:annotation        | Nullability annotations           | Only if wrapper API uses them. |
dependencies {
    implementation("com.contoso:sdk:1.2.3")
}
