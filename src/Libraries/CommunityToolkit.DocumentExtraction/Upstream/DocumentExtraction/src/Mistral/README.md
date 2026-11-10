# CommunityToolkit.DocumentExtraction.Mistral

Extract **page text, tables and images with Mistral OCR**, either directly from
Mistral or through Microsoft Foundry. One `MistralDocumentExtractionClient`
returns a provider-neutral `ExtractionDocument` graph for both hosts, with
detached native JSON for service-specific fields.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.Mistral --prerelease
```

For the Entra ID example only, also install:

```shell
dotnet add package Azure.Identity
```

Experimental APIs emit `CTDE0001`; opt in explicitly as described in the
[component guide](../../README.md#experimental-api-diagnostics).

Targets **.NET Standard 2.0 and .NET 10**. See the [runtime support and HTTP status migration policy](../../README.md#framework-support).
Runtime dependencies include `Azure.Core`
**1.53.0**, `Microsoft.Extensions.DocumentExtraction.Abstractions`,
`Microsoft.Extensions.Documents.Abstractions` and
`Microsoft.Extensions.AI.Abstractions`. `Azure.Identity` is an additional
application dependency, not a provider dependency. For `MEDE0001` or `EXTEXP0019`
compiler diagnostics, see
[experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Configure endpoint, model and authentication

Use `MistralDocumentExtractionClientOptions` to specify the **complete OCR
operation URI** in `OcrEndpoint` and the endpoint's model/deployment selector in
`DefaultModel`. The client sends requests to that exact URI, preserving its path
and query; it does not append a route, infer a host, or discover a fallback.

| Host | Complete operation URI | Authentication and model setup |
|---|---|---|
| Direct Mistral | `https://api.mistral.ai/v1/ocr` | Mistral API key, sent as `Authorization: Bearer`. `mistral-ocr-latest` is the documented native model alias; an explicit model can also be selected. |
| Microsoft Foundry | Copy the OCR operation URI for your deployed model. Endpoint families can use `/providers/mistral/azure/ocr` or `/v1/ocr`; do not append both. | An authorized `TokenCredential`, or a bearer API key **if that endpoint supports key authentication**. Use the model/deployment selector accepted by the resource. |

For Entra ID, configure a development sign-in or deployed identity and grant the
resource's required data-plane access. `TokenScope` defaults to
`https://cognitiveservices.azure.com/.default`; set it explicitly if your
endpoint requires another audience. The credential manages token caching and
refresh; the client consults it on each HTTP attempt. API-key authentication
does not acquire an Entra token.

The `Uri, TokenCredential` constructor is a convenience for a Foundry **base
endpoint**: it appends `/providers/mistral/azure/ocr`, uses the default cognitive
services scope and defaults to `mistral-ocr-4-0`. Use the options-based
constructors below for a complete operation URI or a different scope.
Chat/Responses/OpenAI-compatible endpoints are not native OCR operations.
Use HTTPS outside local transport testing.

## Examples

### Direct Mistral: extract a PDF

Complete console example. Set `MISTRAL_API_KEY` and provide `invoice.pdf`.
Optionally set `MISTRAL_OCR_MODEL` to a supported native model. This uploads the
file and can incur inference charges.

```csharp
using System;
using System.IO;
using System.Threading;
using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

string key = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
    ?? throw new InvalidOperationException("Set MISTRAL_API_KEY.");
string model = Environment.GetEnvironmentVariable("MISTRAL_OCR_MODEL")
    ?? "mistral-ocr-latest";
var settings = new MistralDocumentExtractionClientOptions
{
    OcrEndpoint = new Uri("https://api.mistral.ai/v1/ocr"),
    DefaultModel = model,
};
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var extractor = new MistralDocumentExtractionClient(settings, key);
using var input = File.OpenRead("invoice.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "invoice.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var element in document.EnumerateContent())
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
}
```

This traversal includes table-local `TextContent`. Tables supplied separately by
the service retain their literal Markdown or HTML; they are not synthesized into
an addressed cell grid. Inline Markdown tables remain in page text. Sections do
not duplicate aggregate page text.

### Table/image placement and byte fidelity

Exact native table markers (`[tbl-id](tbl-id)`) and image markers
(`![img-0.jpeg](img-0.jpeg)`) are replaced in page-text order only when ownership
is unambiguous. A table link does not match the suffix inside image syntax.
Matching is ordinal, case-sensitive text matching, not a Markdown parser.
All surrounding text, including whitespace-only spans/pages, is retained
verbatim; empty spans do not create text elements.

Missing image IDs, absent/repeated markers, duplicate nonempty IDs across native
table/image records, and overlapping candidates do not choose a first winner.
Ambiguous markers remain literal text. Every unplaced native element is appended
after page text/placed elements, in native table-array order followed by native
image-array order. Tables retain the existing unique literal-content fallback
when no native table marker exists (including tables without IDs); repeated
table markers do not enable fallback. Duplicate IDs and overlaps also disqualify
literal fallback. Rejected/repeated candidates still participate in overlap
checks so another record cannot arbitrarily consume their text.

Image `image_base64` data URIs preserve the declared MIME and exact decoded bytes,
without sniffing or transcoding. Valid non-base64 data URIs are also supported
using `DataContent` decoding; malformed data URIs/base64 fail during extraction,
without silently falling back. Untyped raw base64 remains supported and is
labeled `image/png` **only as a historical compatibility assumption**, not proof
of its actual format (raw JPEG bytes, for example, are not converted to PNG).
Missing/non-string image bytes produce an image with null `Content`.
Detached native JSON, page references and reported image polygons are retained
regardless of placement.

### Microsoft Foundry: use a deployed model with Entra ID

Independent console example. Set `AZURE_MISTRAL_OCR_URI` to the **complete**
operation URI and `AZURE_MISTRAL_OCR_MODEL` to the deployed model selector.
`DefaultAzureCredential` requires the additional `Azure.Identity` package and
the access setup described above.

```csharp
using System;
using System.IO;
using System.Threading;
using Azure.Identity;
using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

string endpoint = Environment.GetEnvironmentVariable("AZURE_MISTRAL_OCR_URI")
    ?? throw new InvalidOperationException("Set AZURE_MISTRAL_OCR_URI.");
string model = Environment.GetEnvironmentVariable("AZURE_MISTRAL_OCR_MODEL")
    ?? throw new InvalidOperationException("Set AZURE_MISTRAL_OCR_MODEL.");
var settings = new MistralDocumentExtractionClientOptions
{
    OcrEndpoint = new Uri(endpoint),
    DefaultModel = model,
    TokenScope = Environment.GetEnvironmentVariable("AZURE_MISTRAL_OCR_SCOPE")
        ?? "https://cognitiveservices.azure.com/.default",
};
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var extractor = new MistralDocumentExtractionClient(
    settings, new DefaultAzureCredential());
using var input = File.OpenRead("invoice.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "invoice.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var element in document.EnumerateContent())
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
}
```

For a Foundry operation supporting bearer API keys, use the same settings with
`new MistralDocumentExtractionClient(settings, key)` instead. Keep keys in your
application's secret configuration, not source code.

### Process an image and request extracted image bytes

Continuation of the **direct Mistral** example. Supply `scan.png`; the
configured model and endpoint must support the requested image-output options.

```csharp
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;

var options = new DocumentExtractionOptions { ExtractorName = model };
options.AdditionalProperties["includeImages"] = true;
options.AdditionalProperties["imageLimit"] = 4;
options.AdditionalProperties["imageMinSize"] = 100;
using var imageInput = File.OpenRead("scan.png");
ExtractionDocument illustratedDocument = await extractor.ExtractAsync(
    imageInput, "scan.png", "image/png", options, timeout.Token);

foreach (var image in illustratedDocument.EnumerateContent().OfType<ExtractionDocumentImageElement>())
{
    if (image.Content is DataContent bytes)
    {
        Console.WriteLine($"{bytes.MediaType}: {bytes.Data.Length} image bytes");
    }
}

if (illustratedDocument.RawRepresentation is JsonElement native &&
    native.TryGetProperty("model", out var reportedModel))
{
    Console.WriteLine($"Reported model: {reportedModel.GetString()}");
}
```

`image/*` input uses `{"type":"image_url","image_url":"data:..."}`. Other input
media types use `{"type":"document_url","document_url":"data:..."}`. Supply the
actual media type: PDF uses `application/pdf`; image examples include
`image/png`, `image/jpeg` and `image/avif`. The endpoint validates which formats
it supports; not every host/model supports every native Mistral format.
Identifiers label results, not remote URLs to fetch. This stream API sends
base64 data URLs; it does not upload to a file service or fetch input URLs.

The supported additional properties are `includeImages` (bool), `imageLimit`
and `imageMinSize` (non-negative ints). `ExtractorName` overrides the constructor's
request model. Limits govern service image output, not input buffering.
`table_format`, header/footer extraction, blocks, confidence scores and
annotations are not exposed as request options by this adapter; unsupported
properties fail explicitly. Optional response fields depend on model and host.
The requested model is not substituted for a service-reported identity.

## Limits, ownership and errors

- The entire input, base64 request and response are buffered in memory. Check
  your deployment's current file/page limits before submitting. Direct Mistral
  and Foundry limits and model availability are not interchangeable. This client
  does not split oversized documents or switch hosts/models automatically.
- HTTP 503 is retried up to five total attempts. Other HTTP failures propagate
  as `HttpRequestException`, including unsupported service options/formats.
  Malformed/missing required response structure fails rather than returning an
  empty success. For 401/403/404, check the exact operation URI, auth mechanism,
  token audience, identity access and deployed model.
- Source page numbers are one-based. Table/image geometry is preserved where
  reported; page Markdown is not measured paragraph geometry. Ambiguously placed
  tables/images remain separate elements. Native JSON and extracted text can contain
  sensitive data.
- Input streams, credentials and a supplied `httpClient` are borrowed. A client
  created internally is disposed with the extractor. Returned documents require
  no disposal; their JSON and image bytes remain usable after the request.
- Cancellation covers input copying, token acquisition, HTTP requests, response
  reading, retry delays and mapping. It cannot undo remote processing already
  accepted. Both hosts receive the document bytes and can incur charges.

## Documentation and feedback

- [Mistral OCR setup, input unions and model-dependent features](https://docs.mistral.ai/studio/document-processing/basic_ocr)
- [Native OCR API reference](https://docs.mistral.ai/api/endpoint/ocr)
- [Mistral models sold directly by Azure](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure#mistral-models-sold-by-azure)
- [Azure Identity credential configuration](https://learn.microsoft.com/dotnet/azure/sdk/authentication/)
- [Provider comparison and shared model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
