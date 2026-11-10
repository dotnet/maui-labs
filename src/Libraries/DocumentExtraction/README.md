# Official document extraction dependencies

The Microsoft source bridge has been removed. Documents, Extraction and middleware are consumed as signed official packages,
version `11.0.0-preview.1.26560.1`, from the public `dotnet-libraries` feed in the repository's `NuGet.config`.
Their package repository commit is `f8ee27151b21e33793164b39de5bcebfdced81a4`, the same source previously evaluated here.
Central versions live in `Directory.Packages.props`; the matching MEAI abstractions and middleware dependencies are scoped to
document-consuming projects rather than changing the whole repository's dependency graph.

The contracts are still experimental (`EXTEXP0019`, `MEDE0001`). `Microsoft.Maui.Essentials.AI` stays non-shipping/non-packable
pending provider acceptance; consuming an official package does not make the prototype a supported release.

Sample cloud providers now come from [CommunityToolkit/AI](https://github.com/CommunityToolkit/AI/tree/main/DocumentExtraction),
not local Azure/Mistral extraction implementations. Their packages are not published yet, so the exact provider sources and temporary
non-shipping build adapters are documented [separately](../CommunityToolkit.DocumentExtraction/README.md).
The Apple Vision provider remains our implementation of the official abstract client.

See [adoption history and current evidence](../../../docs/ai/document-extraction-corrected-api.md) and
[Apple capability gaps](../../../docs/ai/apple-document-extraction-gaps.md).
