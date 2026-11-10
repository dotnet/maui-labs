# CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence

Extract **document text, layout, tables and figures with Azure Document
Intelligence** and return them as a provider-neutral `ExtractionDocument` graph.
Use it for OCR and layout analysis when you need shared text/table/figure
handling plus service-reported source pages and regions.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence --prerelease
dotnet add package Azure.Identity
```

`Azure.Identity` supplies `DefaultAzureCredential` for the examples; it is an
additional application dependency. For `MEDE0001` or `EXTEXP0019` compiler
diagnostics, see [experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Requirements and setup

Experimental APIs emit `CTDE0001`; opt in explicitly as described in the
[component guide](../../README.md#experimental-api-diagnostics).

Targets **.NET Standard 2.0 and .NET 10**. See the [runtime support policy](../../README.md#framework-support).
These runtime packages are brought in transitively:

- `Azure.AI.DocumentIntelligence` **1.0.0** (its default service API is
  `2024-11-30`).
- `Microsoft.Extensions.DocumentExtraction.Abstractions`,
  `Microsoft.Extensions.Documents.Abstractions` and
  `Microsoft.Extensions.AI.Abstractions` for extraction options, document
  structure and content payloads.

You need an Azure Document Intelligence resource, endpoint and access to a model
supporting your input. The adapter defaults to `prebuilt-layout`; custom model IDs
must already exist. For the Entra ID example below, use an identity-enabled
single-service resource with a custom-subdomain endpoint and appropriate access
roles (for example **Cognitive Services User** on the resource), as described in the
[SDK authentication guide](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.DocumentIntelligence_1.0.0/sdk/documentintelligence/Azure.AI.DocumentIntelligence/README.md#authenticate-the-client).

**`Azure.Identity` is an example-only extra dependency**, not part of this
provider. Configure `DefaultAzureCredential` using your development sign-in or
deployed identity and set `AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT`. Endpoint
configuration alone does not grant service access.

## Examples

### Extract text and layout

Complete console example. Provide `invoice.pdf` and the endpoint environment
variable above. This submits the file to Azure and can incur service charges.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.DocumentExtraction;

string endpoint = Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT")
    ?? throw new InvalidOperationException("Set AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT.");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var sdkClient = new DocumentIntelligenceClient(new Uri(endpoint), new DefaultAzureCredential());
using DocumentExtractionClient extractor = sdkClient.AsDocumentExtractionClient();
using var input = File.OpenRead("invoice.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "invoice.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var text in document.EnumerateContent().OfType<ExtractionDocumentTextElement>())
{
    Console.WriteLine(((TextContent)text.Content!).Text);
    foreach (var source in text.Sources)
    {
        Console.WriteLine($"Page {source.Page.PageNumber}: {source.Polygon.Count} vertices");
    }
}
```

Choose a supported media type for your actual input; changing a MIME string
does not convert the file. The result retains reported page units, ordered
polygons and multi-page source associations.
The traversal above lists text outside table cells; see
[reading table cell content](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#read-table-cell-content)
to include cell text and section-valued mixed content.

### Restrict pages and request optional analysis features

Continuation of the first example. The resource/model must support the requested
features; some can affect service pricing.

```csharp
using Microsoft.Extensions.DocumentExtraction;

var options = new DocumentExtractionOptions { ExtractorName = "prebuilt-layout" };
options.AdditionalProperties["pages"] = "1-2";
options.AdditionalProperties["locale"] = "en-US";
options.AdditionalProperties["features"] = new[] { "languages" };
using var selectedInput = File.OpenRead("invoice.pdf");
var selectedDocument = await extractor.ExtractAsync(
    selectedInput, "invoice.pdf", "application/pdf", options, timeout.Token);
```

`ExtractorName` selects the request model. Supported additional properties are
`pages` and `locale` (strings), and `features` (`IEnumerable<string>`).
Recognized feature names are `ocrHighResolution`, `languages`, `barcodes`,
`formulas`, `keyValuePairs`, `fontStyling` and `queryFields`, case-insensitively.
Recognition of a feature name does not guarantee service support or projection
of its result: use native inspection for fields not represented by this adapter.
No separate query-fields configuration is exposed here. Other explicit options
fail rather than being silently ignored.

### Retrieve figures and inspect the native result

Alternative extraction, continuing with `endpoint` and `timeout` from above.
`IncludeImages` enables additional figure downloads; it is a client-options setting,
not an additional-properties option.

```csharp
using Azure.AI.DocumentIntelligence;

using DocumentExtractionClient imageExtractor = sdkClient.AsDocumentExtractionClient(
    new()
    {
        DefaultModel = "prebuilt-layout",
        IncludeImages = true,
    });
using var imageInput = File.OpenRead("invoice.pdf");
var illustratedDocument = await imageExtractor.ExtractAsync(
    imageInput, "invoice.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var figure in illustratedDocument.EnumerateContent().OfType<ExtractionDocumentImageElement>())
{
    Console.WriteLine(figure.AlternativeText);
    if (figure.Content is DataContent image)
    {
        Console.WriteLine($"{image.MediaType}: {image.Data.Length} detached bytes");
    }
}

if (illustratedDocument.RawRepresentation is AnalyzeResult native)
{
    Console.WriteLine($"Reported model: {native.ModelId}");
}
```

Captions and geometry are mapped even when byte retrieval is disabled. Image
data requires a model that reports downloadable figures. `AnalyzeResult.ModelId`
contains the model identity returned by the service.

## Working with results

Text, sections and tables follow reported document structure; table cells retain
roles, spans and any explicitly referenced nested content. `EnumerateContent()`
walks section elements but does not enter table cells; inspect `table.Cells`
and each anchor's `Content` separately. Figure captions are `AlternativeText`,
distinct from recognized image `Text`.

Native sections spanning pages are projected as page-local continuations with
their original native source references. Within a page, native child pointer
order and atomic tables/cells take precedence over a global span sort; page-root
blocks use their first known content offset, and unknown offsets remain stable
after known blocks. Unowned headers, footers and paragraphs are never adopted
into native sections just because their offsets lie between section children.
Continuations are adapter structure, not newly reported service sections.

Figure `Text` contains only uniquely referenced plain paragraphs with measured
containment in a reported convex figure region and no section/cell/other-figure,
caption or footnote ownership. Descriptive/caption text is not treated as OCR.
Ambiguous, unlocated or shared paragraphs stay in their original structure;
`Text=null` does not mean the service recognized no text. Moved paragraph source
references remain on the image alongside its native figure references. Inspect
the native result for complete relationships and paragraph identities.

The constructor also accepts an existing
`Azure.AI.DocumentIntelligence.DocumentIntelligenceClient`, with optional
`DocumentIntelligenceExtractionOptions`. Options are copied at
construction: `DefaultModel` defaults to `prebuilt-layout`, and `IncludeImages`
to `false`; null or omitted options select those defaults. Native SDK transport
and retry settings belong in the separate `DocumentIntelligenceClientOptions`
`clientOptions` parameter or the configured borrowed SDK client.
`AsDocumentExtractionClient` is a convenience conversion to the common
`DocumentExtractionClient` abstraction using that same constructor. It preserves
the supplied native client's credentials, transport and retry configuration;
the adapter constructors remain supported.
Retrieve the SDK client through unkeyed
`GetService(typeof(DocumentIntelligenceClient))` when you need SDK operations.
Inspect `AnalyzeResult` for service-specific fields beyond the shared layout model.

## Limitations and troubleshooting

- Input is uploaded to Azure and buffered in application memory. Check service
  privacy, pricing, format and file-size limits. This is not a local PDF parser.
- The caller owns input streams and supplied SDK clients; disposing the provider
  leaves them open. Returned documents require no disposal, and downloaded image
  bytes remain usable after the request finishes.
- Cancellation covers input copying, analysis and figure downloads, but cannot
  undo remote work already accepted.
- For `RequestFailedException`, check endpoint, Entra access, model and feature
  availability. Figure download failures, unsupported options and invalid result
  relationships also propagate to the caller.
- Not every custom-model field is projected into the graph. Use the native result
  for those fields, and avoid logging sensitive results.

## Documentation and feedback

- [SDK setup and examples](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.DocumentIntelligence_1.0.0/sdk/documentintelligence/Azure.AI.DocumentIntelligence/README.md)
- [Document Intelligence layout model](https://learn.microsoft.com/azure/ai-services/document-intelligence/prebuilt/layout)
- [Shared model and provider comparison](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
