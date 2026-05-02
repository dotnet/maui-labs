## Generating Files

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