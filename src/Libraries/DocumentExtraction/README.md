# Document extraction proposal snapshot

This is non-shipping prototype scaffolding for [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588).
The pinned commit is `d44c549301d08119f86b73f3fcf58c8b65a2981e`.

`Upstream/` preserves every file from both `Microsoft.Extensions.DocumentExtraction.Abstractions` and
`Microsoft.Extensions.DocumentExtraction`, plus their new `Microsoft.Extensions.Documents.Abstractions` prerequisite,
including original project files, namespaces, accessibility, API baselines, documentation, and license headers.
The old `OcrDocumentReader` has been removed upstream and is no longer copied or compiled.
Do not edit these copies to make the Apple provider work.

The local projects beside this folder file-link those sources. Their only purpose is adapting repository build infrastructure
and supplying the upstream internal guard/diagnostic helpers. They are not package replacements and cannot be packed or shipped.
The prototype Essentials.AI library is also non-packable while its public surface depends on these unpublished assemblies.

Verify the exact copied file set and contents:

```sh
python3 src/Libraries/DocumentExtraction/verify-upstream.py
dotnet build src/Libraries/Microsoft.Extensions.DocumentExtraction/Microsoft.Extensions.DocumentExtraction.csproj
```

The complete current proposal surface is available: page observations, shared semantic document finalization, artifact ownership,
delegating clients, builders, service registration, and lifecycle-only logging. The old extraction-owned tree, typed geometry, options,
usage, metadata, configuration middleware, and OpenTelemetry were removed upstream; they are not retained as local compatibility types.
Provider limitations do not remove or redefine proposal features. When official packages become available, replace the local project
references with package references; visibility changes remain a later decision.
