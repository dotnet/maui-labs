# Document extraction adoption

## Published packages only - 2026-11-11

Microsoft Documents/Extraction packages now use NuGet.org version `11.0.0-preview.1.26560.3` (repository commit
`c60d72e7973a90cd4e4300d9153e97b93315e4b7`). The Mistral Toolkit provider uses the published `1.0.0-preview`
packages (repository commit `74935f4e20490889ae051b4bb8f4bc32959ab3bd`).
All remaining copied source, local provider projects/adapters, manifests and source-verification scripts are removed.
The package migration preserves provider mappings, caller ownership and the Apple implementation.

### One cloud comparison and shared file input

The sample deliberately keeps only Apple Vision and one cloud comparison: Foundry Mistral. The Azure Document Intelligence
registration, NuGet dependencies, separate endpoint/key settings and provider-specific tests are removed; this is a user-directed
scope reduction, not a missing backend fallback. Mistral uses the same `AI:Endpoint`/`AI:ApiKey` as the other Azure features and
adds only `AI:DocumentDeploymentName`. Generic exchange/ingestion table, role, source, blank-cell and mixed-content tests remain
provider-neutral instead of depending on Azure SDK model factories.

Root-level `FileInputService` replaces the image/document input services. Callers pass `PickOptions` using reusable
`ImageFiles`/`DocumentFiles` presets and pass packaged sample names explicitly. A shared `InputFile` replaces the two duplicate
byte records and provides caller-stream loading with MIME resolution, cancellation and borrowed stream ownership.
The image-only filters stay distinct from OCR document filters; format support is not widened to whatever a different feature accepts.
The standalone chat-recording and embedding text import paths still stream into their existing parsers and retain their own limits.

### Direct Toolkit client

The playground constructs `MistralDocumentExtractionClient` directly with the Foundry endpoint, key and deployment name.
The custom cloud policy and bounded-response handler are removed: no extra upload/media validation, response buffering/cap,
operation deadline or exception rewriting wraps the provider. HTTP transport lifetime and defaults belong to the Toolkit.
Cancellation still flows from the existing UI commands, and the shared reader still projects the selected client's output.
Each developer uses their own Foundry deployment; this playground does not need a separate infrastructure policy layer.
After removing these layers, all 471 portable integration tests pass and the complete Catalyst playground builds without warnings/errors.
Tests retain provider mapping, reader projection, transport-error propagation, cancellation and ownership coverage; tests of removed
playground-specific limits and exception rewriting are removed rather than reintroducing those policies through fixtures.

### Playground simplification

The shared-input base PR removes the arbitrary 20 MB file limit and bounded stream helper in favor of standard stream copying.
Native inspection serializes the complete native results as structured JSON on demand, without a semaphore, cached output,
128 KB cap, custom truncating stream or JSON-inside-a-string envelope. Inspection errors surface to the UI with their details
instead of being sanitized and cached. The normalized view remains separate and retains exact geometry, table spans and hierarchy.
Options pass directly to the selected client; settings use their UI-owned provider lists without duplicate-ID or membership policing.
Cancellation messages are simple status text. Reader/extraction modes, API options and provider/native fidelity remain intact.
After integrating the updated shared-input base and newer on-device chat providers, the portable AI suite passes 401 tests and
the full playground suite passes 223 tests. The complete Catalyst playground builds with zero warnings/errors and secret embedding
disabled. The earlier counts below describe their historical snapshots rather than the current merged test inventory.

### Historical registration cleanup (before narrowing to Mistral; policy layer since removed)

Provider construction and options now live directly in `AddDocumentFeature`: Azure uses the package's
`AsDocumentExtractionClient()` extension and Foundry constructs the package's `MistralDocumentExtractionClient`.
The four provider-specific extraction/reader wrapper classes are gone. One non-owning `ExtractedDocumentReader` adapts either
selected client to DataIngestion; the shared projection is still needed because the NuGets return exchange documents, not ingestion records.

The Mistral response bound is an ordinary `DelegatingHandler` in the DI-owned HTTP pipeline. It no longer creates a second
HttpClient, clones requests or recopies request bodies just to forward them to another client. The package borrows that single
HTTP client, whose lifetime is managed by DI.

The remaining shared policy is application behavior, not a local provider: upload limit, operation-wide deadline (including
Toolkit retry delays) and safe error reporting. Keeping it avoids quietly dropping limits during simplification. The reader
projection preserves the still-supported reader mode; no new provider factory, compatibility layer or package subclass is introduced.
After cleanup, all 480 portable integration tests and 158 surrounding playground tests pass. Complete Catalyst/Android builds
remain clean. The isolated full playground successfully processed the bundled synthetic image through both Azure and Mistral,
each in extraction and reader mode, exercising the actual registration extension and single HTTP pipeline.

The published Documents package requires stable MEAI abstractions `11.0.0`; net10 middleware requires DI/logging abstractions
`10.0.15`. These pins remain scoped to document consumers. The obsolete daily feed is removed.
The user explicitly approved adding NuGet.org while mirroring catches up: configured dnceng feeds did not expose these versions.
There is no silent command-line restore workaround or reliance on the previous local build assemblies.

### Package mirror list

The migration introduces or updates these exact packages, including the newly required transitive contract/middleware dependencies:

- Microsoft.Extensions.Documents.Abstractions/11.0.0-preview.1.26560.3
- Microsoft.Extensions.DocumentExtraction.Abstractions/11.0.0-preview.1.26560.3
- Microsoft.Extensions.DocumentExtraction/11.0.0-preview.1.26560.3
- CommunityToolkit.DocumentExtraction.Mistral/1.0.0-preview
- Microsoft.Extensions.AI.Abstractions/11.0.0
- Microsoft.Extensions.DependencyInjection.Abstractions/10.0.15
- Microsoft.Extensions.Logging.Abstractions/10.0.15

`Azure.Core/1.53.0` remains the Mistral dependency. `Azure.AI.DocumentIntelligence` is no longer needed by this sample or its tests.

### Publication validation

After narrowing the provider scope and unifying file input, the portable integration suite passes 476 tests;
Chat/Embeddings/Images/settings/file-input passes 169 tests against actual published assemblies.
The isolated full playground exposes only Apple/Mistral extraction choices. It successfully processes the bundled image through
Mistral extraction and reader modes, and the bundled two-page PDF through the Apple reader using the shared file service.
Chat attachments and image-edit previews also load their packaged sample through that same service without issuing model requests.
The complete Catalyst and Android playgrounds build without warnings/errors. The restored Xcode 27.2 installation is usable;
the installed packs support 26.0 and 27.2 rather than the repository's explicit 27.0 preview target, so Catalyst validation uses the
normal `net10.0-maccatalyst` target with command-line `ValidateXcodeVersion=false`, without changing repository TFMs or workloads.
Actual Release Catalyst execution against the published contracts passes all 11 Apple document client/reader cases.
Earlier session-only smoke runs exercised both configured resources before narrowing to Mistral; those are historical comparisons,
not a requirement to retain both providers. Native results and caller stream ownership remain explicit; credentials/content are not logged.
The existing test-only AngleSharp 1.4.0 moderate advisory remains unrelated and unchanged.

## Official daily packages and source-backed Toolkit reuse - 2026-11-10 (superseded)

The Microsoft source bridge is removed: all three document packages are available from the public **`dotnet-libraries` feed**.
Earlier package checks omitted that feed; their conclusions about non-publication were incomplete.
The pinned `11.0.0-preview.1.26560.1` packages identify repository commit `f8ee27151b21e33793164b39de5bcebfdced81a4`,
the same final contract already evaluated here. The matching MEAI abstractions and middleware dependencies are centrally pinned
only for document-consuming projects.

Azure/Mistral extraction now reuses the Toolkit providers rather than our own transport/mapping implementations.
Toolkit packages are not published on NuGet.org or `dotnet-libraries`, so those two providers temporarily file-link exact upstream
sources at `CommunityToolkit/AI` commit `e8bcbf47ffdcfcd89f9624d60797322b4e73417e`.
Their source/provenance/license and non-shipping adapters were recorded in the source bridge, removed after package publication.
No unrelated provider, cloud fallback or new resource is added. Our Apple Vision provider remains in Essentials.AI.

### Provider integration and deliberate differences

The sample's cloud extraction classes are now thin application-policy decorators over the real Toolkit clients, not replacement
provider implementations. The Azure client borrows the configured SDK transport; the Mistral client borrows the configured HTTP
transport, uses the exact existing Foundry operation URI and Toolkit bearer-key authentication, and requests image bytes.
Both paths retain the 20 MB upload bound; Azure retains its 100-second deadline, Mistral its five-minute deadline and 32 MB
response bound. Mistral 503 retries come from the Toolkit, not an additional retry layer. Redirects remain disabled.
Caller streams and supplied SDK/HTTP clients remain borrowed. The same provider instances feed extraction and the thin local
DataIngestion projection, eliminating the old independent reader HTTP/HTML/JSON interpretation.

The Toolkit mappings are not patched to imitate the old sample:

- Mistral tables retain their literal Markdown/HTML with a 0-by-0 addressed grid rather than parsing HTML into guessed cells.
  Blocks, headers/footers, confidence and auxiliary caption/OCR details remain in detached native JSON when not projected upstream.
- Mistral untyped images follow the Toolkit's PNG assumption, including empty bytes; the sample does not invent a separate MIME policy.
- Azure spans-only fallback content remains native when the Toolkit does not map it to semantic elements.
- Native heading roles do not become fabricated heading levels. Original aggregate content and native-only details are not copied
  into application metadata to conceal differences. Provider grid validation is upstream-owned; old sample HTML parsing and its
  parser-specific dimension limits are not reproduced.

Sample-only `AngleSharp` is removed with the old Mistral HTML parser. The existing test dependency remains for local Apple
nested-table rendering assertions.

### Package/Toolkit adoption evidence

- Both temporary Toolkit assemblies compile against signed official daily packages; all 15 copied provider/configuration/license
  artifacts match the pinned Toolkit commit. No Microsoft source implementation or shim remains.
- Portable integration suite: 479 passed; Chat/Embeddings/Images/settings suite: 158 passed.
- Actual Apple client/reader execution against signed daily assemblies: 11 clean Release Catalyst cases passed.
  iOS and macOS library builds and the benchmark consumer also succeeded.
- The complete Android playground builds with the Toolkit providers and real packages.
- A session-only console smoke test compiled the exact sample policy/decorator sources against the real providers and used the
  existing configured resources with the bundled synthetic image. Azure returned one page, five content elements and native
  `AnalyzeResult`; Mistral returned one page and detached native `JsonElement`. Both retained caller input ownership.
  This verifies the actual Foundry route/bearer-key combination, not merely mocked transport.
- Final Catalyst playground/UI rerun was blocked by a shared-machine Xcode replacement: Xcode 27.0 disappeared after the successful
  native tests; the new 27.2 bundle lacked its Info.plist/frameworks and could not run `xcodebuild`. The global selection pointed at
  CommandLineTools. No global selection, installer or another session's app was changed. Earlier native results remain valid for
  the unchanged Apple provider; they are not claimed as a final Toolkit-equipped Catalyst app run.

The following sections preserve historical source-bridge evidence, not the current dependency mechanism.

## Main-tip source bridge adoption - 2026-11-10 (superseded)

[dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588) merged on November 5.
The current source bridge pins main at `f8ee27151b21e33793164b39de5bcebfdced81a4`; all 21 artifacts from the three upstream
projects remain byte-for-byte copies with original public namespaces. NuGet.org, all approved repository feeds and upstream's daily
feeds returned no versions for these packages. The user explicitly chose a main-tip source bridge to get ahead of publication.
Local adapters remain non-shipping/non-packable and no unpublished version is fabricated.

The final contract is not the October correction below:

| Surface | Merged contract |
|---|---|
| Client | Abstract `DocumentExtractionClient.ExtractAsync` returns one completed `ExtractionDocument` |
| Payload | Local `AIContent`: text, bytes, URI; no aggregate descendant copies |
| Geometry | Exact ordered double vertices on source references, explicit source-page frames |
| Tables | Spans, roles, empty anchors and mixed-content sections inside cells |
| Native inspection | Borrowed document/payload raw objects and source-reference `GetRawRepresentation()` |
| Ownership | Documents are non-disposable data; clients and providers own their own temporary resources; caller inputs stay borrowed |
| Options | `ExtractorName` and provider-defined `AdditionalProperties`; no `CaptureRawOutput` |
| Removed | Page streaming/finalizer/result envelope, generalized annotations/confidence/extension values, raw-body artifacts/statuses, structural Markdown rendering |

These are intentional boundaries, not requests for a compatibility layer. The adapters use the actual supported intersection.
Native details remain accessible explicitly, rather than being moved into document metadata or silently serialized into semantic text.
The sample's normalized view reports payload types, frames, polygons, spans, roles and hierarchy. Explicit bounded native inspection
is a separate action. Raw bodies are not promised or reconstructed. Dropping a view result releases application references, not a
fictional upstream result owner. The October nested-table HTML projection issue is superseded because upstream no longer renders structure.

Apple's local ingestion renderer now renders nested/mixed table cells structurally in HTML, including merged cells, rather than escaping
the child's complete rendered table. Leaf text is HTML-encoded and ordinary flat tables retain their existing pipe-table projection.
Duplicate native page numbers fail before a completed result is published; native enumeration cleanup and caller stream ownership remain intact.

Mistral page Markdown that aggregates already projected margins/tables/images stays in native inspection rather than being copied into an
ancestor's local payload. Standalone otherwise-unrepresented body Markdown can remain ordinary text. No attempt is made to parse a
provider's aggregate Markdown into a guessed structure or to infer a table grid from auxiliary HTML.

### Current evidence

- Exact source verification: 21 artifacts at the pinned main tip.
- Portable Essentials.AI suite: 487 passed; playground Chat/Embeddings/Images/settings suite: 158 passed.
- Actual clean Release Catalyst 27 reader/client execution: 11 passed, including images, PDF page order/frames,
  native details, merged cells, cancellation and borrowed-input behavior.
- Complete Catalyst playground and Android playground builds succeeded; native macOS 27 and iOS library builds succeeded.
  The installed iOS references support 26.0/27.1, not the repo's explicit `ios27.0` preview target, so iOS validation used the normal
  `net10.0-ios` target with `ValidateXcodeVersion=false` against installed references. No workload, feed or target-framework configuration was changed.
- The isolated full playground processed the bundled Apple two-page PDF as both extraction and ingestion. Normalized extraction retained
  two BottomLeft normalized source frames and actual polygons; explicit bounded native inspection completed without errors or truncation.
  Configured Azure and Mistral each processed the bundled image using their completed-document adapters; Azure retained actual source polygons.
  Explicit Mistral native inspection also completed without errors or truncation, and was not labeled an original response body.
- Existing Azure SDK restoration still needs the previously approved temporary NuGet.org additional source when absent from approved feeds.
  No repository feed configuration was changed. That restore reports the existing AngleSharp 1.4.0 moderate advisory
  [GHSA-pgww-w46g-26qg](https://github.com/advisories/GHSA-pgww-w46g-26qg); this unrelated dependency is unchanged.

See [current capability gaps](apple-document-extraction-gaps.md) and [implementation](apple-document-recognizer-implementation.md).

## Historical correction adoption - 2026-10-14

The remainder records the earlier experiment, not the current contract or current validation.

Upstream: [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588), pinned to
`1fc95b5b77b48e085b49cf25fd1b3e4797825fa9`.
Shared Documents correction: [dotnet/extensions#7835](https://github.com/dotnet/extensions/pull/7835),
`37dad1a0837de86317a716c81805560a1cea18dc`, imported upstream in `d830ad69fe7145eb086e9662bf50bea4f58286d2`.

## Why try again

The `d44c549…` reduced-contract experiment was reverted because geometry, spans, raw-provider access and several other surfaces were lost.
The correction explicitly restores layout fidelity across page-to-document finalization, rather than relying on provider metadata.
This experiment imports the complete corrected source, not a local substitute or a compatibility fork.

## Corrected surfaces to exercise

| Capability | Supported upstream representation |
|---|---|
| Source frame | `DocumentSourcePage`: originating page identity, optional dimensions/unit/origin, source operation |
| Geometry | `DocumentRegion`: exact producer-ordered polygon vertices in that frame; double precision |
| Confidence | `DocumentConfidence`: reported value/kind and optional scale, without normalization or inferred calibration |
| Layout/detail associations | `DocumentAnnotation`: explicit regions, confidence, roles, structured supplied values and raw inspection |
| Merged/empty cells | Spans, optional native role and annotations independent of nullable cell content; covered and empty addresses differ |
| Images | Bytes/source/description survive without requiring invented captions or nonempty Markdown |
| Structured extensions | `DocumentExtractionExtension.Value` reaches shared `DocumentExtensionElement.Value` |
| Source output | One operation-scoped `DocumentSourceOutput`, with explicit native/body availability and ownership |
| Options | Per-call `ModelId`, adapter settings, and `CaptureRawOutput` (default true); fresh requests, no shared mutation |
| Finalization | Original deterministic builder creates disposable `DocumentExtractionResult.Document` and owns registered streams |

These are real API mechanisms, not permission to invent provider facts. Native roles/confidence/model IDs remain absent when not supplied.
An annotation's opaque structured value is not a standardized barcode/list/entity schema. The provider must label its supplied information.

## Raw-output rules

Native results are borrowed; the model never implicitly disposes the SDK's object. Available original response bytes must be detached
from the HTTP response and become a document-owned artifact exactly once, even when pages share an operation.
Serializing a mapped result is never described as the original response body.

Apple Vision is local and has no HTTP response body. Our bounded bridge snapshot is a detached representation of the recognized output,
not the actual Swift `DocumentObservation` instance or an original wire response. The operation's original-content status is unavailable.
Azure's SDK result is likewise not an original response body unless that body was actually captured independently.
Mistral's detached received body can be retained as original content if ownership and failure/cancellation cleanup are handled.

`CaptureRawOutput=false` must remove every raw carrier, including nested annotations/regions/source-frame associations. It does not
erase genuine portable semantic content or typed geometry/confidence. Raw data must not enter automatic Markdown, logs, embeddings,
normalized inspection, or ingestion records. Inspection is a deliberate action before the owning result/document is disposed.

## Provider constraints

Use only actual intersections between native output and the corrected contract. No image crops, inferred confidence/header levels,
guessed table roles, estimated token usage, client fallbacks, or custom semantic metadata dictionaries are added.
Per-call provider settings must be explicitly supported or rejected; missing values use documented defaults without mutating them.
Concurrent extractions must have independent operation output and request settings.

The native Apple request stays `RecognizeDocumentsRequest`, with bounded list/table recursion, orientation-aware images,
internal PDFKit rendering, direct callback completion and cancellation. Geometry must preserve original vertices rather than
reconstructing rectangles or silently reordering points.

## Operational APIs still absent upstream

The correction does not restore usage/progress envelopes, old client-metadata conveniences, URI/AI-content overloads,
configuration/OpenTelemetry middleware, or `OcrDocumentReader`. Pages still enumerate, but that is not a promise that a cloud
operation extracts incrementally over the network. Any completed-page count in the playground is local presentation, not a claimed
provider total or billing statistic.

The concrete Apple reader remains a clearly local `IngestionDocumentReader` projection over the canonical finalized document.
The artifact lifetime does not transfer silently to DataIngestion values; returned text/image values must be independent where necessary.
First-class normalized lists/items/nesting and barcode/entity/language detection schemas remain separate upstream review work.

## Provenance and release

All 30 project artifacts, including original projects, API baselines and documentation, are copied byte-for-byte and hash-verified.
Original namespaces/accessibility remain intact. Local build adapters reproduce shared guards and assembly experimental markers only.
The copied projects and prototype Essentials.AI library remain non-shipping and non-packable pending API/provider review.

## Remaining upstream projection issue

The canonical hierarchy can retain a nested table as a merged cell's content, but the current merged-table HTML projection
HTML-encodes the child's complete `ToMarkdown()` output. Nested table markup is consequently rendered as literal Markdown or
escaped HTML rather than structural nested table HTML. This is a presentation loss, not loss of the typed table in the document.
`UpstreamNestedTableMarkdownTests` records the distinction without changing the verbatim upstream implementation.

## Local evidence

The final corrected implementation passes 485 portable Essentials.AI tests and 158 playground feature/settings tests.
Eleven real Mac Catalyst 27 document reader/client tests pass in both Debug and Release, including images, multi-page PDFs, merged tables,
native geometry, explicit raw opt-out, input ownership, cancellation and invalid input.
The full playground builds for Catalyst 27 and Android; the library also builds for iOS 27 and macOS 27.

The isolated Catalyst playground processed the bundled two-page PDF through page streaming, whole-document finalization
and the ingestion reader. Both extraction paths retained two normalized BottomLeft source frames. Disabling raw capture
kept geometry and semantic annotations while changing both raw statuses to Disabled. Explicit inspection with capture
enabled returned two bounded bridge snapshots, with NativeStatus Retained and ContentStatus Unavailable, without inspection errors.
The same playground processed the bundled image using both configured cloud adapters. Azure returned one page with native SDK
output retained and original-body capture unavailable. Mistral returned one page with native parsed JSON and detached original
body both retained, and reported model identity `mistral-ocr-4-0`. Explicit Mistral inspection read both retained representations
without errors or truncation. Both canonical results retained actual source polygons.

The native list fixture exposed repeated recursive projections of an item's own content. Markdown now reconciles those
using matching item text/marker and overlapping source geometry, not global text deduplication. Distinct same-text nested
items remain distinct; all original structured observations remain available in semantic annotations and captured snapshots.

Incremental Release device builds twice retained stale native AOT outputs and aborted before running tests. A targeted
device-project Clean allowed execution and exposed the list regression above; final Debug and clean Release native runs each
pass all eleven cases. Existing MAUI Compatibility IL2037 linker warnings remain unrelated to this prototype.
