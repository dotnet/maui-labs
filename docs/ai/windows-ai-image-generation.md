# Windows AI image generation

`WindowsAIImageGenerator` implements `Microsoft.Extensions.AI.IImageGenerator`
for experimental text-to-image generation and prompt-guided edits with one
source image. It uses a separate optional model from Windows chat.

## Requirements and dependencies

Use a supported Windows 11 24H2+ Copilot+ PC and an MSIX-packaged app declaring
`systemAIModels`. See the [Windows platform guide](windows-ai-language-models.md#packaging)
for packaging and runtime configuration.

Image generation requires the experimental Windows App SDK:

| Package | Library | Windows samples and device tests |
| --- | --- | --- |
| `Microsoft.WindowsAppSDK` | Not referenced | `2.5.4-experimental` |
| `Microsoft.WindowsAppSDK.AI` | `2.5.4-experimental` | `2.5.4-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.Foundation` | `2.3.13-experimental` (transitive) | `2.3.13-experimental` (transitive) |
| `Microsoft.WindowsAppSDK.InteractiveExperiences` | `2.1.10-experimental` (transitive) | `2.1.10-experimental` (transitive) |

The library overrides only its Windows AI package. Apps must select a matching
umbrella SDK rather than MAUI's implicit Windows App SDK graph:

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-windows'))">
  <PackageReference Include="Microsoft.WindowsAppSDK" VersionOverride="2.5.4-experimental" />
</ItemGroup>
```

There is no global SDK pin. Experimental sample settings are not a production
access grant. Build and package Windows targets on Windows.

## Usage and options

```csharp
using var generator = new WindowsAIImageGenerator();
var response = await generator.GenerateAsync(
    new ImageGenerationRequest("A blue sailboat on a lake."));
```

Acknowledge `MEAI001` when using the experimental M.E.AI image API. Obtain
consent before first use: image generation checks readiness and may trigger a
multi-gigabyte model download. Unsupported hardware, missing capabilities,
and setup failures are errors; the generator does not switch providers.
This setup policy is separate from chat's explicit `EnsureReadyAsync` lifecycle.

Results are encoded PNG (default), JPEG, BMP, or TIFF bytes. `Count` controls
output count; `AdditionalProperties` accepts `Creativity` (`double`),
`MaxInferenceSteps` (`int`), and `Seed` (`int`). The seed increments for each
output. Source images must be encoded bytes in `DataContent`. Masks, multiple
source images, requested sizes, and hosted URI output are rejected.
Unsupported output formats fail before model setup.

Native `Generate*` calls are synchronous and cannot be interrupted mid-call.
The [Chat Playground](../../samples/AIExtensions.Sample.ChatPlayground/README.md)
exposes the Windows image provider alongside Azure.
