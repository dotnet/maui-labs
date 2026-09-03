# Azure Content Understanding image classification for .NET MAUI

> [!WARNING]
> This package is experimental and may change without notice.

Use an Azure Content Understanding custom classifier analyzer through the
provider-neutral image-classification APIs in `Microsoft.Maui.Essentials.AI`.

## Install

```sh
dotnet add package Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding --prerelease
dotnet add package Azure.Identity
```

The package brings the core `Microsoft.Maui.Essentials.AI` contract and
`Azure.AI.ContentUnderstanding` SDK as transitive dependencies.

## Configure the analyzer

Create an Azure Content Understanding custom classifier analyzer whose
`ContentCategories` define the possible labels. Configure it with
`EnableSegment=false` so one content-level category is returned for the whole
image.

The provider accepts JPEG, PNG, BMP, HEIF, and HEIC image media types.

## Classify an image

```csharp
using Azure.Identity;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding;

using IImageClassificationClient classifier =
    new AzureContentUnderstandingImageClassificationClient(
        new Uri("https://example.cognitiveservices.azure.com/"),
        new DefaultAzureCredential(),
        new AzureContentUnderstandingImageClassificationOptions
        {
            AnalyzerId = "my-image-classifier",
        });

await using Stream image = File.OpenRead("photo.jpg");
ImageClassificationResult result = await classifier.ClassifyImageAsync(
    image,
    "image/jpeg",
    new ImageClassificationOptions
    {
        MaximumPredictions = 1,
        MaximumInputBytes = 10 * 1024 * 1024,
    });

Console.WriteLine(result.Predictions[0].Label);
```

Azure Content Understanding whole-image classifier categories do not include a
confidence value. Predictions therefore have `Confidence = null`, and setting
`MinimumConfidence` throws `NotSupportedException`.

The adapter buffers no more than `MaximumInputBytes` from the caller-owned
stream before invoking Azure. It does not dispose or rewind the stream.
Disposing the adapter does not dispose the supplied credential.

## Platform support

| Platform | Supported |
|---|---|
| Android | Yes |
| iOS | Yes |
| Mac Catalyst | Yes |
| macOS | Yes |
| Windows | Yes |

The managed provider targets .NET 10 and is designed for trimming and native
AOT. Runtime use requires an Azure Content Understanding resource, a configured
classifier analyzer, network access, and a credential authorized to invoke it.
