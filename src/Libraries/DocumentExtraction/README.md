# Merged document extraction snapshot

This is non-shipping prototype scaffolding for [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588).
The proposal merged on November 5, 2026. The pinned **main** commit is
`f8ee27151b21e33793164b39de5bcebfdced81a4` (November 10).

`Upstream/` preserves every file from both `Microsoft.Extensions.DocumentExtraction.Abstractions` and
`Microsoft.Extensions.DocumentExtraction`, plus the corrected `Microsoft.Extensions.Documents.Abstractions` prerequisite, including
original project files, namespaces, accessibility, API baselines, documentation, and license headers. All 21 artifacts are immutable.
The obsolete upstream `OcrDocumentReader` is no longer part of the proposal and is not compiled.
Do not edit these copies to make the Apple provider work.

The local projects beside this folder file-link those sources. Their only purpose is adapting repository build infrastructure
and supplying the upstream internal guard/diagnostic helpers. They are not package replacements and cannot be packed or shipped.
The prototype Essentials.AI library is also non-packable while its public surface depends on these unpublished assemblies.

Verify the exact copied file set and contents:

```sh
python3 src/Libraries/DocumentExtraction/verify-upstream.py
dotnet build src/Libraries/Microsoft.Extensions.DocumentExtraction/Microsoft.Extensions.DocumentExtraction.csproj
```

The final abstract `DocumentExtractionClient` returns one non-disposable `ExtractionDocument`.
Local payloads use `AIContent`; source frames and exact ordered polygons, table spans/roles/empty anchors, mixed-content cell sections,
and per-call `ExtractorName`/provider options remain. Borrowed native objects are available through document/payload raw properties
and explicit source-reference inspection. No response-content artifacts, capture opt-out, generalized annotations/confidence,
page streaming/finalizer or structural Markdown renderer remain.

On November 10 the three packages were not present on NuGet.org, any repository-approved feed, or upstream's daily feeds.
The user explicitly chose this main-tip source bridge until publication. Do not invent a package version or change feeds.
Once packages exist, replace the three local ProjectReferences with centrally versioned PackageReferences and remove these copies/adapters.
Builders, DI and lifecycle-only logging remain; no local replacement APIs compensate for removed upstream features.

Local project adapters reproduce upstream's distinct assembly-level diagnostics: `EXTEXP0019` for Documents and `MEDE0001` for Extraction.
They supply only the shared argument guards needed by the original code; source is not patched to accommodate providers.
See [the correction report](../../../docs/ai/document-extraction-corrected-api.md) for actual provider fidelity and remaining gaps.
