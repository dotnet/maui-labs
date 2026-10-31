# Image generation provider implementation guide

This is the shared contract and implementation map for `IImageGenerator`
providers used by Essentials.AI and the Chat Playground. It follows the
[chat provider guide](local-chat-provider.md)'s four-part pattern: **Common/API
rule**, **Provider patterns**, **Essentials.AI rule**, and **Implementation**.
Image generation is separate from chat image input, tool calls, and embeddings.

OpenAI observations refer to the pinned
[`Microsoft.Extensions.AI.OpenAI` 10.4.1 adapter](https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIImageGenerator.cs).
The Playground uses that adapter against a configured Azure OpenAI deployment.
Adapter mappings do not guarantee that every OpenAI/Azure model supports an
operation or option. OpenAI is a compatibility control, not a specification for
local SDKs.

Keep native dependencies, packaging, readiness policy, and option ranges in the
[Windows image guide](windows-ai-image-generation.md). Keep app controls and
cloud configuration in the [Playground README](../../samples/AIExtensions.Sample.ChatPlayground/README.md).
The rules below distinguish shared requirements from current implementation
limits; they do not claim new runtime coverage.

## Provider identity and setup

**Common/API rule**

`IImageGenerator` exposes `GenerateAsync`, `GetService`, and disposal. Metadata
identifies the provider, endpoint, and default model when known; it is not a
readiness or capability-discovery API.

**Provider patterns**

- **OpenAI:** metadata uses `openai`, the configured endpoint, and the SDK
  `ImageClient.Model`. `GetService` also exposes that SDK client. Credentials,
  deployment availability, and service policy are checked by the service.
- **Windows:** metadata uses `windows` and `windows-image-generator`, a stable
  provider identifier rather than an installed-weights version. First use may
  prepare/download the optional native model.

**Essentials.AI rule**

Expose truthful metadata and the generator through `GetService` without starting
inference. Unsupported service keys/types return `null`. Document preparation
and data-transfer policy before use; never silently switch providers. Do not
apply chat's explicit preparation contract to images without an intentional API
change.

**Implementation**

Windows setup is in `WindowsAIImageModelFactory`. The sample registers Windows
on supported OS versions and Azure when `ImageDeploymentName` is configured;
its descriptor is sample UI metadata, not a portable capability API.

## Requests and source images

**Common/API rule**

`ImageGenerationRequest` carries a prompt and optional `OriginalImages`.
The presence of original images does not define mask semantics or guarantee
editing support.

**Provider patterns**

- **OpenAI:** no originals selects generation; originals select editing. The
  pinned adapter consumes only the first original, as encoded `DataContent`.
  This is an adapter limitation, not multi-image support.
- **Windows:** no originals selects text-to-image; one encoded `DataContent`
  selects prompt-guided editing. Multiple originals are rejected, not treated
  as an implicit mask.

**Essentials.AI rule**

Require a valid prompt, preserve the requested operation, and reject unsupported
source representations/counts rather than silently dropping originals. Keep
generation and editing capability bindings separate. The OpenAI adapter's
first-image behavior is not a rule to copy.

**Implementation**

Windows rejects null requests, blank prompts, and multiple originals; source
decoding rejects non-`DataContent` inputs. The shared sample service trims a
nonblank prompt and sends zero or one original. Raw provider tests do not claim
that this sample normalization is provider behavior.

## Generation options

**Common/API rule**

Options can request count, size, media type, and response representation.
Provider-native options are extension points, not universally supported knobs.

**Provider patterns**

- **OpenAI:** maps count, size, PNG/JPEG/WebP, and data/URI preferences to SDK
  options. A typed `RawRepresentationFactory` can supply generation or edit
  options; existing native values take precedence. Actual support is
  deployment-specific.
- **Windows:** supports count and PNG/JPEG/BMP/TIFF encoding. Requested sizes
  and URI output are rejected. Native creativity, steps, and seed are mapped
  from recognized, correctly typed `AdditionalProperties`.

**Essentials.AI rule**

Leave unset options at provider defaults. Validate known unsupported requests
before expensive work where possible. Do not claim an ignored extension was
honored or force cloud size/encoding choices into local contracts.

**Implementation**

Windows defaults to one PNG, rejects nonpositive counts and unsupported output
formats before model setup, and offsets an explicit seed per output. It does
not consume `RawRepresentationFactory` or arbitrary additional properties.
Shared positive tests leave size, encoding, and response format unset.

## Responses, usage, and failures

**Common/API rule**

`ImageGenerationResponse.Contents` represents results using `AIContent`;
usage and raw provider data are optional. A URI is not encoded image bytes.

**Provider patterns**

- **OpenAI:** maps returned bytes to `DataContent` or returned URLs to
  `UriContent`, retains the SDK collection as `RawRepresentation`, and maps
  usage when supplied. Missing bytes and URI is an error.
- **Windows:** returns encoded `DataContent` with the selected image media type;
  it does not report token usage or a raw response. Native failure status and
  extended errors are surfaced through exceptions.

**Essentials.AI rule**

Return usable image content, preserve available provider diagnostics, and never
invent usage. Invalid input, setup failure, safety rejection, and inference
failure must not become successful empty or partial results. A URI response
does not prove successful image retrieval or decoding.

**Implementation**

Shared live assertions require nonempty image bytes or an absolute HTTP(S)
image URI. The sample service copies bytes into its display model, retains
HTTP(S) URIs, ignores non-image auxiliary content, and rejects malformed
data/URI entries or a response with no images. These are sample-consumer rules,
not extra guarantees imposed on all raw adapters.

## Cancellation, responsiveness, and ownership

**Common/API rule**

`GenerateAsync` accepts a cancellation token and the generator is disposable.
Cancellation does not guarantee that a remote or native operation stops.

**Provider patterns**

- **OpenAI:** forwards cancellation to async SDK requests. Adapter disposal is
  a no-op; it does not take ownership of the supplied `ImageClient`.
- **Windows:** inference is synchronous and worker-dispatched, including warm
  calls. Cancellation during inference waits for completion; a returned image
  is put in disposal scope before cancellation is checked.

**Essentials.AI rule**

Honor pre-cancellation before expensive work. Never release resources still in
use by native work merely to stop waiting sooner. Dispose owned resources,
not caller-supplied clients/models, and document when cancellation is observed.
An async method name or `ConfigureAwait(false)` alone does not ensure UI
responsiveness. Callers must await outstanding requests before disposing the
generator.

**Implementation**

Windows owns its lazy-created generator but not a supplied generator. Decoded
sources survive native completion and are disposed on success or failure.
Cancellation of the setup wait does not cancel the cached setup task. The
sample borrows its generator and forwards the token; the test factory creates
a per-test adapter over the shared Azure SDK client.

## Conformance testing

- Put provider-neutral assertions in `ImageGeneratorTestsBase`; bind capable
  providers/deployments to `ImageGeneratorEditTestsBase` separately. Unsupported
  editing is not a passing no-op or a runtime skip.
- Keep native format/size/URI rejection, marshalling, readiness, and threading
  checks provider-specific, with the concrete reason stated on each test.
  Model-free checks must not initiate inference.
- Test raw adapters separately from the actual source-linked
  `ImageGenerationService`, models, and input resource. Portable fakes test
  sample normalization, forwarding, ownership, and errors, not compatibility
  with a live provider.
- Establish the real Azure baseline with `EnableOpenAIImageTests=true`;
  `EnableOpenAIImageEditTests=true` includes generation and editing and requires
  an edit-capable `AI:ImageDeploymentName`. Neither requires enabling chat.
- Preserve `RequiresModel=true` on positive native/cloud cases. Use bounded
  requests and runner timeouts suitable for image inference. Before live runs,
  obtain approval for device use, model downloads, data transfer, and charges.
- Verify warm UI responsiveness with an actually queued callback while inference
  is pending, then await inference before disposal. Do not equate cancellation
  delivery with interruption of a synchronous native call.
- Final validation runs the complete configured device assembly and portable
  suites, reporting discovered/executed/passed/failed/skipped counts. Focused
  filters, compilation, and prior-head results are not a complete live baseline.
  Keep run-specific evidence and limitations in the PR, not this guide.
