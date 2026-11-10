# Toolkit cloud extraction source bridge

The playground reuses `CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence` and
`CommunityToolkit.DocumentExtraction.Mistral`. It does not maintain a second provider mapper or wire protocol.
Only these two providers are included; PdfPig, Content Understanding and Vision Language Model are out of scope.

The Toolkit packages are not available on NuGet.org or `dotnet-libraries` yet. This temporary bridge preserves the provider implementations,
options, extensions, shared helpers, original project configuration and license **verbatim** from
[CommunityToolkit/AI](https://github.com/CommunityToolkit/AI/tree/e8bcbf47ffdcfcd89f9624d60797322b4e73417e/DocumentExtraction),
commit `e8bcbf47ffdcfcd89f9624d60797322b4e73417e`. `upstream-manifest.json` records every file's upstream path, blob and SHA256.
Do not patch the copied implementations. Report provider limitations upstream or make explicit application-level choices instead.

Two sibling non-shipping/non-packable net10 build adapters file-link the exact sources. Unlike the removed Microsoft source bridge,
they reference the real signed `Microsoft.Extensions.DocumentExtraction.Abstractions` daily package.
The Toolkit assemblies retain their original namespaces and `CTDE0001` experimental diagnostic.
App policy, credentials, endpoint selection, size limits and deadlines stay in the playground rather than in a fork of provider code.

```sh
python3 src/Libraries/CommunityToolkit.DocumentExtraction/verify-upstream.py
dotnet build src/Libraries/CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence/CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence.csproj
dotnet build src/Libraries/CommunityToolkit.DocumentExtraction.Mistral/CommunityToolkit.DocumentExtraction.Mistral.csproj
```

Once Toolkit packages are published, replace those two ProjectReferences with centrally versioned PackageReferences, then remove
the copied sources, verification manifest and adapters. Do not copy the Microsoft contract again or create a local NuGet identity
pretending to be a published Toolkit package.
