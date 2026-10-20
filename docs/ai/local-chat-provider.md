# Local chat provider contract

This is the canonical provider-neutral contract for local/on-device chat providers
in `Microsoft.Maui.Essentials.AI`, using `Microsoft.Extensions.AI` (MEAI).
It is a design and conformance target, not a claim that every current provider
implements every feature. The package README remains the source for shipping
platform support. Platform implementations and their guides document native
constraints separately.

## Guiding principles

- Implement standard `IChatClient` behavior when the native API can represent it.
- Preserve supported content; omit only unrecognized optional content.
- Clearly reject requested behavior that cannot be honored, or expose it only on
  OS/model versions that support it.
- Never silently switch to a cloud provider or claim an unsupported feature was applied.
- Keep provider policy, native ranges, and error formats out of universal tests.
  OpenAI is a compatibility control where applicable, not a specification for local SDKs.

## Availability and capabilities

MEAI does not define a readiness or capability-discovery interface. Concrete
providers may expose native helpers; this contract adds no shared provider,
preparation, or DI interface.

`ChatModelStatus` is a sealed immutable snapshot with reference identity:

- `Availability` maps native state to `Available`, `NotReady`, `NotSupported`,
  `Disabled`, `RequirementsNotMet`, or `Unknown`.
- `UnavailableReason` preserves a provider-native reason when known. It is
  always null for `Available`; a supplied reason must not be blank.
- `Capabilities` is defensively copied and read-only. Null means unknown or not
  advertised; an empty collection means known none.
- Capabilities describe effective MEAI-facing support: text, streaming, image
  input, tool calling, structured output, token usage, and reasoning.

Compare snapshot properties explicitly. Availability is not a readiness
guarantee, and capabilities do not guarantee current availability. Refresh
status rather than indefinitely caching it; generation remains authoritative.
Generation must check availability and fail clearly rather than initiating
model setup or download. Where supported, preparation must be an explicit,
awaited provider operation separate from generation.

## Model identity and conversation state

- `ChatClientMetadata` reports a truthful provider and configured/default model.
- Responses and updates use the native executed model identifier when available,
  otherwise a documented stable identifier or null. Do not guess runtime identity.
- Fixed-model providers may ignore `ChatOptions.ModelId`.
- Use caller-supplied history when the native provider has no durable state.
- Reject unsupported non-null conversation IDs and continuation tokens, and
  explicit background requests; preserve null/default foreground behavior.
- Never silently truncate history; report context-window failures clearly.

## Message mapping

### Instructions and roles

- Process `ChatOptions.Instructions` before System messages.
- Preserve instruction order and repeated instructions. If the native API has
  one instruction slot, combine options first, then System messages in encounter
  order, with blank lines between parts; remove hoisted messages from history.
- If native generation requires User/Assistant prompt content, reject
  instructions-only input before generation; never synthesize a placeholder.
- Omit blank options instructions and blank hoisted System messages. Preserve
  blank inline System messages unless the native API rejects them.
- Preserve explicitly supplied blank User and Assistant turns and their order.
- Ignore unknown roles; never reinterpret them as User.
- Native restrictions on the final role belong in provider bindings, not a
  universal trailing-System assertion.

### Content and capabilities

- Omit unknown/custom `AIContent` items individually. Map recognized standard
  content in encounter order. If nothing remains, apply that role's blank-turn policy.
- Recognized-but-unsupported content is not unknown content. Reject it clearly
  with `NotSupportedException` when its role, media type, representation, or
  OS/model capability is unsupported; never silently drop it.
- Reject malformed or empty payloads for supported media with `ArgumentException`.
- Preserve historical tool calls/results, names, call IDs, arguments, results,
  and encounter order even when new native tool registration is unavailable.
- Gate conditional features on runtime OS/model capabilities, with no cloud fallback.

## Request features

### Generation options

Map options with native equivalents and ignore optional controls without one.
Preserve native defaults when unset. Validate known native ranges and unsafe
numeric conversions before forwarding mapped values. Sampling ranges and
conflict precedence are provider policies, not universal contracts.

### Tools

- None omits definitions. Auto registration may explicitly degrade to no tools
  when native registration is unavailable because invocation is optional.
- Match `AutoChatToolMode` and `NoneChatToolMode` by MEAI type semantics, not
  reference identity with the `ChatToolMode.Auto` and `ChatToolMode.None` singletons.
- Required and named Required must be honored or rejected, never downgraded to Auto.
- Preserve parameter/output types and required versus optional properties.
  Reject unrepresentable model-facing schemas rather than substituting a generic schema.
- Providers may execute natively or return calls for middleware execution.
  Each call executes exactly once with request cancellation and services.
- Additional `UseFunctionInvocation` middleware must not re-execute calls that
  a native provider already completed.
- Tool callbacks must retain request Activity context across awaits. Caller
  cancellation must remain cancellation, not an ordinary tool error/result.

### Structured output

- Honor a supplied schema natively or reject it; never silently downgrade to a prompt.
- Map constraints with native equivalents; reject unsupported shapes/constraints
  instead of dropping them.
- Local providers following the explicit-schema policy reject schema-free
  `ChatResponseFormat.Json` and typed prompt fallback before generation.
- Do not emulate schema guarantees by strengthening prompts, stripping fences,
  repairing output, or parsing ordinary text into JSON.
- A provider without structured output rejects schema requests explicitly.
  Cloud controls may support schema-free JSON; legacy bindings retain their
  existing behavior until explicitly migrated.

## Responses, streaming, and telemetry

- Map native metadata when available. Leave unavailable IDs, timestamps, finish
  reasons, raw objects, and provider fields null rather than inventing values.
- Stream append-only deltas, not cumulative snapshots. Preserve text, tool, and
  structured-output ordering; suppress empty updates without meaningful metadata.
- Buffer until stable or fail if a producer revises emitted content. At completion,
  emit only a missing suffix; fail explicitly on divergence.
- Every token count is optional. Map native counts to standard fields, preserve
  other summable counts in `AdditionalCounts`, and never manufacture estimates or zeroes.
- Emit streaming usage once at completion. Input-only usage does not imply output
  or total counts.
- Use the optional shared MEAI OpenTelemetry wrapper for operation, first-update,
  inter-update, model, and available-token telemetry. An update need not be a token.
- Do not add invented provider timing fields. Keep prompts, responses, tool
  arguments, and results out of telemetry unless sensitive-data capture is enabled.

## Cancellation, errors, and ownership

- Do not start generation for pre-cancelled requests. Forward in-flight
  cancellation and early stream disposal to native generation.
- Keep callbacks and request resources alive until native work terminates.
- Validate requests before generation when possible; retain native failure details
  in clear managed exceptions.
- Fault streaming operations rather than completing partial failures successfully.
  Null/malformed native success results are errors, not empty successes.
- Dispose retained clients/models only when the provider created and owns them.
  Never dispose caller-supplied models/services. Release per-request resources
  deterministically after native termination.

## Conformance bindings

Shared generic device-test bases contain backend-neutral contracts. Bind
capability hooks explicitly when adopting a contract, including rejection paths.
Conservative virtual defaults preserve current providers; an unbound legacy hook
is not evidence of support. Keep `RequiresModel` traits and existing assertions.
Required-tool, unsupported structured-output, and explicit-schema rejection
assertions live in separate model-free methods and call the bound provider
directly. Positive methods retain `RequiresModel` and return for rejection-only
bindings. Sampling tests opt into `ReasoningEffort.None` through binding hooks
only where the provider/model accepts it; leaving that option unset preserves
compatibility with non-reasoning cloud models.
Host-side controls can verify the shared harness, but cannot establish live native
or Azure/OpenAI compatibility.
