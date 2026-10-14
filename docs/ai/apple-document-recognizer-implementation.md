# Apple RecognizeDocuments implementation experiment

**Status:** Non-shipping prototype in #450.
**Contract:** [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588) at
`1fc95b5b77b48e085b49cf25fd1b3e4797825fa9`.
**Native API:** Swift Vision `RecognizeDocumentsRequest`, iOS/Mac Catalyst/macOS 26+.

## Pipeline

```text
caller-owned image/PDF stream
  -> internal AppleVisionRecognizeDocumentsProcessor / PDFKit
  -> RecognizeDocumentsRequestNative / bounded native snapshot
  -> actual DocumentExtractionPage observations, source frames, annotations and source output
  -> original upstream finalizer
  -> disposable shared Documents.Document
      -> explicit rich playground inspection
      -> local AppleVisionRecognizeDocumentsReader projection
```

The corrected shared model replaces the extraction-owned semantic tree without discarding layout fidelity.
One canonical Apple mapping is used; the reader does not interpret raw snapshots independently.
All three upstream projects are copied exactly and file-linked through separate local adapters. There is no private substitute contract.

## Fidelity and extensibility

Use typed regions/coordinate frames, actual confidence, independent cell annotations and spans, and explicit structured extensions.
Do not abuse arbitrary metadata or source-reference strings as native payload transport.
Unknown native capabilities remain provider-specific or absent, not inferred normalized fields.
Apple does not return figure crops/captions, standardized cell roles or a model deployment ID.

Native snapshots and genuine original cloud response content belong only in the explicit source-output API with truthful availability.
The Apple JSON bridge snapshot is not an original response body or a live Swift object. Capture opt-out must remove raw data throughout
all source pages and annotations while preserving portable content/layout. Operation output is never stored as mutable client last-response.

## Native safety

The Swift bridge uses Apple terminology, preserves cancellation and orientation, and invokes completion directly rather than redispatching
onto an operation queue blocked by managed page processing. PDFKit rendering remains internal with finite bounds and bounded scale.
Apple's synthesized Codable can recurse through framework-produced list/table content; ancestor re-entry detection and traversal limits
remain necessary. A bounded snapshot is a deliberate safety boundary, not a promise that every future native field is automatically captured.

## Application

Documents retains the same services/view-model/page-area-composer-settings composition as Chat, Embeddings and Images.
The playground exercises completed-page enumeration, core finalization, the concrete ingestion reader, restored settings and explicit
source-output inspection. It must render layout/span/annotation data rather than merely compile with unused restored types.
Automatic normalized rendering/logging must not access raw objects or read original-body streams.

Azure Document Intelligence and Foundry Mistral remain explicit sample-only providers using existing configured resources/transports.
No client fallback, extra cloud resource, credentials in source or distribution of secret-embedded Debug binaries is introduced.

See [corrected adoption details](document-extraction-corrected-api.md) and [feature gaps](apple-document-extraction-gaps.md).
The old investigations in [historical feedback](apple-document-extraction-feedback.md) are retained as history, not current mapping rules.
