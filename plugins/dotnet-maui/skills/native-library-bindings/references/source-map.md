# Source Map

Use these sources to refresh or verify the skill guidance.

## Microsoft and Community Toolkit docs

- Native Library Interop getting started:
  `https://learn.microsoft.com/dotnet/communitytoolkit/maui/native-library-interop/get-started`
  - Used for NLI project layout, Xcode/Android wrapper projects, Swift/Java wrapper patterns, Objective Sharpie flow, and platform-conditional app references.
- CommunityToolkit/Maui.NativeLibraryInterop:
  `https://github.com/CommunityToolkit/Maui.NativeLibraryInterop`
  - Used for template and sample binding structure.
- .NET for Android binding a Java library:
  `https://learn.microsoft.com/dotnet/android/binding-libs/binding-java-libs/binding-java-library`
  - Used for local AAR/JAR binding projects, `AndroidLibrary`, `Bind=false`, and Java dependency verification.
- .NET for Android binding from Maven:
  `https://learn.microsoft.com/dotnet/android/binding-libs/binding-java-libs/binding-java-maven-library`
  - Used for `AndroidMavenLibrary` and Maven artifact binding.
- AndroidMavenLibrary build action:
  `https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/android-maven-library`
  - Used for repository values, `Bind`, `Pack`, POM download behavior, and dependency verification behavior.
- Java dependency verification:
  `https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/java-dependency-verification`
  - Used for POM/JavaArtifact verification concepts.
- Resolving Java dependencies:
  `https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/resolving-java-dependencies`
  - Used for `PackageReference`, `ProjectReference`, `AndroidLibrary`, `AndroidMavenLibrary`, and `AndroidIgnoredJavaDependency` decision order.
- Java bindings metadata:
  `https://learn.microsoft.com/dotnet/android/binding-libs/customizing-bindings/java-bindings-metadata`
  - Used for `api.xml`, XPath transforms, and metadata attributes.
- SDK-style iOS/Apple binding project structure:
  `https://learn.microsoft.com/dotnet/maui/migration/ios-binding-projects`
  - Used for SDK-style binding project items and `NativeReference` (structure only; ignore any Xamarin-migration framing — target .NET MAUI net10.0+).
- Objective-C binding docs:
  `https://learn.microsoft.com/previous-versions/xamarin/cross-platform/macios/binding/`
  - Used for `ApiDefinition.cs`, `StructsAndEnums.cs`, ObjC binding attributes, and Objective Sharpie context.
- Objective Sharpie docs:
  `https://learn.microsoft.com/previous-versions/xamarin/cross-platform/macios/binding/objective-sharpie/`
  - Used for Sharpie as a first-pass header parser.
- NuGet MSBuild pack docs:
  `https://learn.microsoft.com/nuget/create-packages/creating-a-package-msbuild`
  - Used for SDK-style package metadata and `dotnet pack`.
- Native NuGet package docs:
  `https://learn.microsoft.com/nuget/guides/native-packages`
  - Used for `build` props/targets import behavior.

## Related projects

- Swift direct binding generator:
  `https://github.com/justinwojo/swift-dotnet-bindings`
  - Used as an optional advanced path for Swift-only `.xcframework` inputs.

## Local repo prior art

The predecessor implementation guides and eval suites for
`android-slim-bindings` and `ios-slim-bindings` were migrated into this skill's
references, assets, and `tests/dotnet-maui/native-library-bindings/eval.yaml`.
The old skill directories now contain only tiny redirect shims for backwards
compatibility; do not use them as source material for binding guidance.
