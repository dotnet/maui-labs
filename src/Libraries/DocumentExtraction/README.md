# Official document extraction dependencies

The Microsoft source bridge has been removed. Documents, Extraction and middleware are consumed as signed official packages,
version `11.0.0-preview.1.26560.3`, published on NuGet.org.
Their package repository commit is `c60d72e7973a90cd4e4300d9153e97b93315e4b7`.
Central versions live in `Directory.Packages.props`; the matching MEAI abstractions and middleware dependencies are scoped to
document-consuming projects rather than changing the whole repository's dependency graph.

The contracts are still experimental (`EXTEXP0019`, `MEDE0001`). `Microsoft.Maui.Essentials.AI` stays non-shipping/non-packable
pending provider acceptance; consuming an official package does not make the prototype a supported release.

Sample cloud providers now come from [CommunityToolkit/AI](https://github.com/CommunityToolkit/AI/tree/main/DocumentExtraction),
not local Azure/Mistral extraction implementations. Both providers use published `1.0.0-preview` packages.
All copied Toolkit sources, verification manifests and build adapters are removed.
The Apple Vision provider remains our implementation of the official abstract client.

NuGet.org is temporarily configured with explicit user approval because the existing dnceng feeds do not yet expose these versions.
The obsolete `dotnet-libraries` daily source is removed. Mirror the package list in the adoption report before removing NuGet.org.

See [adoption history and current evidence](../../../docs/ai/document-extraction-corrected-api.md) and
[Apple capability gaps](../../../docs/ai/apple-document-extraction-gaps.md).
