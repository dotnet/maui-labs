# Android Artifact Acquisition

Acquire Android artifacts and dependency facts before writing binding metadata.

## Preferred order

1. Maven coordinates from Maven Central, Google Maven, or vendor repository.
2. Existing Gradle module that builds an AAR.
3. Direct AAR/JAR release with matching POM.
4. Source repo built locally.

Prefer Maven/Gradle because POM metadata and resolved versions are essential for dependency verification.

## Maven coordinates

Collect:

- Group ID
- Artifact ID
- Version
- Repository
- Packaging type (AAR/JAR)
- POM file

Example:

```xml
<AndroidMavenLibrary Include="com.airbnb.android:lottie" Version="6.6.2" Repository="Central" />
```

Use `Repository="Google"` for AndroidX/Google Maven artifacts, or a repository URL for vendor-hosted artifacts that do not require authentication.

## Gradle wrapper project

When a vendor provides Gradle instructions, use a small Gradle project to resolve the full graph:

```kotlin
plugins {
    id("com.android.library") version "8.7.3" apply false
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}
```

Then:

```bash
./gradlew :app:dependencies --configuration releaseRuntimeClasspath
./gradlew :app:dependencyInsight --configuration releaseRuntimeClasspath --dependency artifact-name
```

Capture the resolved graph in the binding PR/notes because it explains why specific NuGet versions were chosen.

Prerequisites for automated Gradle resolution are JDK, Android SDK with `ANDROID_HOME` or equivalent configured, network access to repositories, and `gradle` on `PATH` for generated report projects. Treat Gradle project files and wrappers as executable code: only run a vendor/native project wrapper if the project is trusted, or pass explicit Maven coordinates to the helper so it generates its own minimal project. If the helper script reports `Failed` because prerequisites are missing, stop and report the prerequisite instead of guessing dependency versions.

## Mapping Maven to NuGet

Search for packages that advertise Java artifacts:

```bash
dotnet package search "artifact=androidx.core:core" --source https://api.nuget.org/v3/index.json
dotnet package search "artifact=com.squareup.okhttp3:okhttp" --source https://api.nuget.org/v3/index.json
```

If a package contains the artifact but does not advertise it, add `JavaArtifact` metadata to the `PackageReference`:

```xml
<PackageReference Include="Some.Binding.Package" Version="1.2.3"
                  JavaArtifact="com.vendor:artifact:1.2.3" />
```

For local bindings:

```xml
<ProjectReference Include="../Vendor.Dependency/Vendor.Dependency.csproj"
                  JavaArtifact="com.vendor:dependency:1.2.3" />
```

## Direct AAR/JAR downloads

Use direct downloads only when Maven is unavailable.

For each file:

- Record source URL and version.
- Verify checksum when available.
- Download matching POM if possible.
- Inspect the archive for embedded `classes.jar`, `jni` native libs, resources, and ProGuard/R8 files.

Commands:

```bash
unzip -l vendor.aar | head -100
unzip -l vendor.aar | grep -E 'classes\\.jar|jni/.+\\.so|AndroidManifest\\.xml|consumer-rules'
```

If no POM exists, Gradle cannot fully verify dependencies. Use vendor docs and runtime testing to identify missing dependencies.

## Version conflict handling

Use Gradle's resolved versions as facts. If the tree says:

```text
org.jetbrains.kotlin:kotlin-stdlib:1.9.0 -> 2.0.21
```

choose a .NET binding/NuGet strategy that satisfies the resolved `2.0.21` artifact, not just the originally requested `1.9.0`.

## Private or authenticated repositories

Some SDKs (Mapbox is the classic example) are served from a credentialed Maven repository and require a download token to acquire the AAR at build time.

- Declare the repo (for a Gradle wrapper project, a `GradleRepository` item pointing at the vendor Maven URL). Gradle reads the credential from `~/.gradle/gradle.properties` (for example `MAPBOX_DOWNLOADS_TOKEN=...`), which is machine-global and never committed.
- For contributor setups, commit a `*.props.template` with a placeholder and `.gitignore` the real `*.props` that holds the token, so the secret never lands in source or the package.
- In CI, inject the token from a secret into the environment or a generated `gradle.properties`; do not bake it into `.targets`/`.props` that ship in the NuGet.
- Distinguish the **build-time acquisition credential** (download token used to fetch the SDK) from any **runtime API key** (for example a Mapbox access token in the app). They are different secrets with different lifecycles; never ship either in the binding package.

Do not put credentials in skill files, scripts, or committed project files. Document required access, but never commit secrets.
