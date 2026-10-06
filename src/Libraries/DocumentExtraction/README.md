# Document extraction proposal snapshot

This is non-shipping prototype scaffolding for [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588).
The pinned commit is `a215825ae2c96723e922e068c226ff77122c7c94`.

`Upstream/` preserves every file from both `Microsoft.Extensions.DocumentExtraction.Abstractions` and
`Microsoft.Extensions.DocumentExtraction`, including their original project files, namespace, accessibility, API baselines,
documentation, and license headers. The proposal's `Microsoft.Extensions.DataIngestion/OcrDocumentReader.cs` is also unchanged.
Do not edit these copies to make the Apple provider work.

The local projects beside this folder file-link those sources. Their only purpose is adapting repository build infrastructure
and supplying the upstream internal guard/diagnostic helpers. They are not package replacements and cannot be packed or shipped.
The prototype Essentials.AI library is also non-packable while its public surface depends on these unpublished assemblies.

Verify the exact copied file set and contents:

```sh
python3 src/Libraries/DocumentExtraction/verify-upstream.py
dotnet build src/Libraries/Microsoft.Extensions.DocumentExtraction/Microsoft.Extensions.DocumentExtraction.csproj
```

The complete proposal surface is available: the client contract, result models, delegating clients, builders, service registration,
option configuration, logging, and OpenTelemetry. Provider limitations do not remove or redefine proposal features. When the official
packages become available, replace the local project references with package references; visibility changes are a later decision.
