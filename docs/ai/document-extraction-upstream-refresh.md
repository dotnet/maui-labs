# Document extraction upstream refresh — 2026-10-14

Proposal: [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588).
Previous snapshot: `a215825ae2c96723e922e068c226ff77122c7c94`.
Current snapshot: `d44c549301d08119f86b73f3fcf58c8b65a2981e`.

## What changed upstream

This is a replacement of the prototype contract, not an additive update. The proposal now depends on
[dotnet/extensions#7835](https://github.com/dotnet/extensions/pull/7835), the shared Documents abstractions.
That prerequisite is explicitly included in the proposal branch; both contracts remain provisional and unapproved.

After merging upstream main, the proposal added shared Documents in `4888286e02bc482d11420c06a031d78abcb92a2e`,
recovered the selected Extraction design in `af8ea07dc640ef85d4a452a71afb66ed26a6f924`, and excluded an unrelated
AI API-baseline whitespace change in `d44c549301d08119f86b73f3fcf58c8b65a2981e`.

| Previous API | Current API |
|---|---|
| Extraction-owned page/block/table/image semantic tree | `Microsoft.Extensions.Documents.Document` is the canonical owned document |
| `ExtractAsync` and `ExtractPagesAsync` provider methods | One provider operation: `ExtractPagesAsync(stream, identifier, mediaType, token)` |
| Result assembled by a lightweight page reducer | `ExtractAsync` is a core extension that finalizes observations through `DocumentExtractionDocumentBuilder` |
| `DocumentExtractionPageResult` with progress and usage | `DocumentExtractionPage` with ordered observations, optional exact provider Markdown and opaque page references |
| Mutable options and model selection | No standard options or model override parameter |
| Client metadata and usage types | Removed; optional `GetService` remains |
| Typed polygons, dimensions, coordinate systems and confidence | Removed from the selected extraction contract |
| `DocumentTableCell` with spans and nested element list | Addressed cell with a single content observation; spans are not modeled |
| Raw representations and property bags | No raw-provider payload slot in extraction; opaque references are associations, not replacement payload containers |
| Configuration, logging and OpenTelemetry middleware | Builders, DI, delegation and lifecycle-only logging remain; configuration and OpenTelemetry are removed |
| Original `OcrDocumentReader` adapter to DataIngestion | Removed; no upstream DataIngestion migration/bridge is supplied |
| Loose result lifetime | Disposable result owns its finalized Document and registered artifacts |

## New guarantees to exercise

Finalization deterministically orders pages and observations. It rejects duplicate page numbers, duplicate extraction-local provider IDs,
invalid table ownership, and shared artifact ownership. Equal text from distinct observations is not deduplicated.
Ordered duplicate opaque page references and evidence associations are preserved.

Providers still do not own input streams. Enumerators must release provider resources when disposed.
Page enumeration does not promise incremental network extraction: Azure or Mistral may complete the remote operation before yielding pages.
The builder owns artifacts until successful finalization, then ownership transfers to the resulting Document.
Cancellation and partially built middleware failures have explicit cleanup paths.

Shared Documents separates **stored source Markdown** from **semantic Markdown projection**. `GetMarkdown()` returns stored content;
`ToMarkdown()` projects the semantic elements. Neither operation is permission to fabricate a provider's original Markdown.
Local element IDs/addresses are in-memory navigation helpers, not durable provider identities or grounding claims.

## Local migration constraints

All 28 files in the three proposal projects are copied verbatim and hash-checked, including original project files and baselines.
Only the local file-link build adapters supply repository-specific wiring. The prototype remains non-shipping and non-packable.
The old contract is not retained alongside the new one, and no private replacement model is introduced.

The Apple request remains `RecognizeDocumentsRequest`, shared by the extraction client and concrete ingestion reader.
Only semantically matching native information is normalized. Geometry/confidence, barcode payloads, detected entities, candidate words,
PDF render facts and native snapshots are not stuffed into opaque evidence, extension values, references, metadata or artifacts.

The concrete reader remains a **local DataIngestion projection**, not an upstream adapter. The playground labels the two actual APIs
and no longer offers the deleted upstream OCR reader or removed request options. Sample providers retain existing configuration and
transport safeguards; removed per-call model options do not become a custom options protocol.

See [Apple capability gaps](apple-document-extraction-gaps.md) for the current intersection and intentionally omitted capabilities.

## Experiment results

The prototype was rebased on `dotnet/maui-labs` main `543f8643f255535b2165dc0a66ca4fbc48b8fa1c`, retaining its Chat diagnostics,
Foundation Models image input/token support, and opt-in OpenAI device-test configuration.

- Source verification: 28 original files across all three upstream projects match the new pinned snapshot byte-for-byte.
- Portable execution: 488 Essentials.AI tests and 157 playground-feature tests passed, with no failures/skips.
- Actual Release Mac Catalyst 27 execution: 11 Apple client/reader cases passed, including text, tables, merged-grid anchors, lists as
  text, multi-page PDF, identifiers, finalized-document disposal, and caller-owned input streams.
- The first device-runner attempt timed out after 30 seconds without result packets during cold native startup; the explicit
  `-p:DeviceRunnersDataTimeout=180` rerun executed all 11 cases successfully. No product fallback or timeout suppression was added.
- Full playground builds passed on Catalyst 27 and Android; native library builds also passed on iOS 27 and macOS 27.
  Builds were clean; device linking emitted existing MAUI compatibility `IL2037` warnings.
- An isolated live playground extracted the same two-page PDF through Apple page enumeration, core result finalization, and the local
  ingestion reader. Caller identifier and page attribution were retained; Apple page Markdown and inferred header levels remained null.
- Both configured cloud providers finalized the bundled image successfully into the new shared document using the existing resources.
  No credentials were read into tool output or committed, and no new cloud resources were created.
- Independent review exposed an upstream inconsistency: empty optional page Markdown can cause the finalizer's nonempty section
  constructor to throw despite valid observations. The local Mistral adapter treats an empty optional value as absent, preserves
  nonempty provider Markdown exactly, and has regressions for both execution modes. Copied upstream source remains unchanged.

The changed contract deliberately drops prior typed geometry, confidence, span, raw-payload, usage and option demonstrations.
Those losses are visible in the gap matrix, not hidden in a new provider-key scheme. The local reader is explicitly not an upstream bridge.
