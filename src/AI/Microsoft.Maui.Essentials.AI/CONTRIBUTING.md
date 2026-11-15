## Generating Files

To generate the API definitions files:

```
dotnet build src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj -f net11.0-ios

sharpie bind \
  --output=src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS \
  --namespace=Microsoft.Maui.Essentials.AI \
  --sdk=iphoneos27.2 \
  --scope=. \
  artifacts/obj/Microsoft.Maui.Essentials.AI/Debug/net11.0-ios/xcode/{hash}/archives/EssentialsAIiOS.xcarchive/Products/Library/Frameworks/EssentialsAI.framework/Headers/EssentialsAI-Swift.h
```