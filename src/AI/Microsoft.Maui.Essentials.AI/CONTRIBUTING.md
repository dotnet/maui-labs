## Experimental Core AI validation

The unpublished OS27 Core AI experiment uses the existing Xcode project and
the shared internal Foundation Models chat implementation. It is enabled only
with `EnableCoreAI=true` and `UseXcode27Preview=true`; normal builds retain the
System native framework and OS26 deployment minimum. Model resources belong
to validation apps, never the library or NuGet.

See [the experiment guide](../../../docs/ai/core-ai-experiment.md) for pinned
dependencies/model preparation, `CoreAIModelDirectory` app staging, native
resource/lifetime boundaries, supported options and device-test commands.

## Generating binding files

To generate the API definitions files:

```
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj -f net10.0-ios26.0

sharpie bind \
  --output=src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS \
  --namespace=Microsoft.Maui.Essentials.AI \
  --sdk=iphoneos26.1 \
  --scope=. \
  artifacts/obj/Microsoft.Maui.Essentials.AI/Debug/net10.0-ios26.0/xcode/{hash}/archives/EssentialsAIiOS.xcarchive/Products/Library/Frameworks/EssentialsAI.framework/Headers/EssentialsAI-Swift.h
```