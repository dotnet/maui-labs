# Document extraction contract proposal

This directory temporarily compiles the proposed
`Microsoft.Extensions.DocumentExtraction` abstractions from
[dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588) so MAUI Labs
experiments can reference the exact contract while it is under upstream review.

The project targets only `net10.0`, is non-shipping, and is not packable. The imported
source remains byte-for-byte unchanged under
`Upstream/dotnet-extensions/src/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions`.
MAUI Labs compatibility shims stay in `Compatibility`.

Run the provenance check from the repository root:

```powershell
pwsh -File src/AI/DocumentExtraction.ContractProposal/Verify-Upstream.ps1
```

Use `-Offline` to verify the local files against the recorded hashes without downloading
the pinned upstream revision.
