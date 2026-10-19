# Document extraction proposal snapshot

This is non-shipping prototype scaffolding for [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588).
The pinned commit is `1fc95b5b77b48e085b49cf25fd1b3e4797825fa9`.

`Upstream/` preserves every file from both `Microsoft.Extensions.DocumentExtraction.Abstractions` and
`Microsoft.Extensions.DocumentExtraction`, plus the corrected `Microsoft.Extensions.Documents.Abstractions` prerequisite, including
original project files, namespaces, accessibility, API baselines, documentation, and license headers. All 30 artifacts are immutable.
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

The corrected proposal preserves typed source coordinate frames, exact ordered polygons, confidence, structured annotations, merged
table spans/roles/empty anchors, per-call settings, and explicit operation-scoped native/original-response retention with opt-out.
The canonical semantic tree is the shared Documents model, finalized through the original builder with result/artifact ownership.
Builders, DI and lifecycle-only logging remain. Old usage/progress, client-metadata, URI/AI-content convenience APIs, configuration
middleware, OpenTelemetry and upstream DataIngestion translation are not restored.

Local project adapters reproduce upstream's distinct assembly-level diagnostics: `EXTEXP0019` for Documents and `MEDE0001` for Extraction.
They supply only the shared argument guards needed by the original code; source is not patched to accommodate providers.
See [the correction report](../../../docs/ai/document-extraction-corrected-api.md) for actual provider fidelity and remaining gaps.
