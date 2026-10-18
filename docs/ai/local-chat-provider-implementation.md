# Local Chat Provider Implementation Specification

This is repository policy for implementing local/on-device
`Microsoft.Extensions.AI.IChatClient` providers. It defines an OpenAI-compatible
core with explicit native capability profiles, not a lowest-common-denominator
API. It does not imply that every provider supports tools, images, JSON, or usage.

**MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** have their
[RFC 2119](https://www.rfc-editor.org/rfc/rfc2119) meanings. A documented gap is
not a conformance waiver: deviations MUST identify the native constraint,
semantic loss, observable behavior, and covering tests or unresolved work.

## 1. Authority and decisions

Implementers MUST apply this decision order:

1. Follow the [MEAI contract][ichatclient] and use the repository-pinned
   `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` **10.4.1**
   adapters as the compatibility baseline. Distinguish interface requirements
   from behavior the interface merely permits. For example, MEAI permits argument
   mutation; this repository recommends nonmutation.
2. Prefer an exact native mapping when it preserves roles, content, history, and
   tool semantics. An ordered transcript SHOULD NOT be flattened just because
   another provider has only one instruction slot.
3. When exact mapping is impossible, use the documented majority among
   *comparable* adapters/native APIs and record the loss. Count independent
   implementations, not endpoints: Azure and OpenAI using the same adapter are
   one baseline, not two votes.
4. If there is no clear majority, expose an explicit capability, reject the
   request, or document and test an explicitly weaker profile. Implementations
   MUST NOT silently weaken a requested guarantee or switch providers/clouds.

The [OpenAI Chat adapter][openai-chat] prepends options instructions and preserves
message role/order. The [Responses adapter][openai-responses-options] uses
top-level options instructions while [retaining System/Developer input
items][openai-responses-content]. Neither establishes universal "hoist everything"
or "last instruction wins" behavior.

For single-slot systems, the official [Google adapter][google-instructions] and
[AWS adapter][aws-instructions] collect system instructions separately from
conversation history. Google exposes native [user/model roles][google-content];
Anthropic Messages has a [top-level system field, not a system input
role][anthropic-messages]. This supports the single-slot profile below, not a
claim that all providers have identical APIs. Current Bedrock
[Converse][bedrock-converse] and [Message][bedrock-message] documentation also
includes a system message role; observed adapter hoisting is not an eternal
native API limitation.

## 2. Capability profiles

Every provider MUST declare its profile and a capability matrix in its provider
documentation. Profiles describe mapping fidelity; none implies full conformance.

| Profile | Required declaration |
|---------|----------------------|
| Exact/native history | Ordered native roles, instruction entries, content and tool entries preserved; any unsupported roles or prompt-position restrictions explicit. |
| Single top-level instruction | Instruction messages collected by section 3; retained conversation history mapped with its own declared fidelity. |
| Text/prompt-only | Role-labelled/textual replay and its weaker isolation/boundary semantics explicit; no claim of native role or tool-execution equivalence. |

A provider MAY combine profiles by operation, such as a native instruction
channel for text and prompt-only structured generation. It MUST document that
difference; instruction text embedded in a prompt is not a native system channel.

The matrix MUST cover accepted roles and modalities; instruction/history
mapping; tool definitions, execution and replay; schema modes; option mappings;
streaming/progress; usage; ownership/concurrency; and OS, hardware, model
readiness and feature gates. Each feature MUST be classified as supported,
unsupported, advisory, or an explicitly documented weaker mapping.
`ChatClientMetadata` identifies a provider/model; it is **not** a capability
inventory unless a separate repository-defined extension supplies one.

## 3. Instructions and roles

[`ChatRole` is extensible and case-insensitive][chatrole]. Implementations MUST
compare roles accordingly. The literal `new ChatRole("developer")` is an
instruction role for normalization even though it is not a built-in MEAI role.

**Exact/native history:** providers SHOULD represent `ChatOptions.Instructions`
initially, followed by message instructions in their existing positions. They
SHOULD preserve distinct native System/Developer semantics when supported. A
provider unable to preserve Developer semantics MUST document its handling;
it MUST NOT claim hierarchy-preserving support after collapsing those roles.

**Single top-level instruction:** providers MUST use this conversion:

1. Enumerate caller messages once, using a request-local materialization when
   needed. Do not modify caller messages, contents, or options.
2. Add nonblank `ChatOptions.Instructions` as the first instruction chunk.
3. In message encounter order, collect System and literal Developer messages.
   Concatenate supported text fragments *within each message* without adding
   separators. Omit an entirely blank chunk, but preserve each retained chunk
   unchanged, including its whitespace and repeated text.
4. Remove collected instruction messages only from converted conversation
   history. Preserve the relative order of all remaining accepted messages.
5. Join chunks with exactly `\n\n`, independent of the host platform, or preserve
   them as ordered native instruction blocks when the API supports blocks.

For example, options `base` and messages `System:A`, `User:hello`,
`Developer:B`, `System:A` yield instructions `base\n\nA\n\nB\n\nA` and history
`User:hello`. Repetition MUST NOT be deduplicated.

This ordering is **not precedence**. Providers MUST NOT promise last-wins
resolution of conflicting instructions. Collapsing System and Developer loses
hierarchy and MUST be documented. If there is no instruction channel, reject
instructions or declare a prompt-only profile; never silently relabel them User.
Supported nontext instructions SHOULD retain native semantics. Text-only
instruction profiles apply the content policy below to unsupported parts;
they MUST NOT stringify binary data or fetch remote content implicitly.

## 4. Inputs, content, and history

Both APIs MUST reject a null messages enumerable with `ArgumentNullException`;
`GetService` MUST do the same for a null service type. Null options MUST use
provider defaults. Implementations MUST consume the messages enumerable at most
once and SHOULD avoid mutating caller-owned objects. The single-slot conversion
and schema transformation rules require nonmutation.

The default OpenAI-compatible content profile omits unknown roles and
unrecognized/unmappable content, retaining neighboring supported content and an
empty User turn when nothing maps. Null text MUST NOT become the literal
`"null"`. A stricter provider MUST explicitly declare and test its rejection
policy; rejection is not equivalent to the default tolerance profile. Known
instruction roles follow section 3, not unknown-role handling.

Malformed **supported** content MUST produce an explicit error, not be silently
treated as unsupported. Availability-dependent omission, such as images on an
older text-only OS, MUST be documented separately from decode/model failures
on supported systems. Remote URLs MUST NOT trigger implicit downloads.

Accepted history, roles, content order, and tool relationships MUST be preserved
within the declared profile. Implementations MUST NOT silently truncate,
summarize, reorder, or discard supported history to fit a context window.
An explicitly requested preprocessing layer MAY do so outside the provider.
Native requirements for a final User prompt MUST be validated without relabeling
Assistant or Tool messages. Empty, instruction-only, consecutive-role, and
assistant-prefill requests are explicit provider policies, not universal errors.

## 5. Tools and transcript fidelity

Providers MUST distinguish tool definitions, generated calls, native execution,
and historical replay. Supporting replay does not imply execution support.

Supported tool history MUST retain call IDs, names, arguments, results, role
boundaries, and encounter order. Name resolution for a result MUST use only
preceding matching calls; ambiguous mappings MUST fail rather than guess a name
or borrow a later call. Duplicate IDs, missing names, and parallel calls need
explicit validation/mapping policies. Generated IDs MUST remain stable across
updates, results, and subsequent history.

Native-executed calls MUST be marked `InformationalOnly` when surfaced as
`FunctionCallContent`, so `FunctionInvokingChatClient` does not execute them
again. Native execution MUST propagate request cancellation and required
invocation services/context. Tool call/result ordering MUST survive streaming
and aggregation; a Tool result is not Assistant answer text.

`ChatToolMode.None` MUST disable execution/registration even when definitions
are supplied. Required or named selection MUST be honored or explicitly
rejected, not silently treated as Auto. Unsupported requests for tool calling
MUST fail explicitly. `AllowMultipleToolCalls` MUST be documented as honored,
advisory, or unsupported. Unused definitions with None need a consistent tested
policy, not accidental invocation.

A declared textual replay fallback MAY serialize calls/results without native
tool entries, but MUST retain both names and call IDs where supplied and document
the weaker semantics. It MUST NOT invent a function name for an unmatched result
or advertise native execution. Invalid tool schemas MUST NOT silently become
unrelated schemas.

## 6. Structured output

Providers MUST distinguish plain text, schema-less JSON mode, and schema-constrained
JSON. Unsupported modes MUST be explicit. A constrained request MUST NOT silently
downgrade to ordinary text or "please return JSON" prompting.

Caller schemas MUST NOT be mutated. Providers SHOULD reuse MEAI's
`AIJsonSchemaTransformCache` for native transformations, with tests for cache
reuse and input immutability. Strictification MUST be limited to documented
native requirements. Explicit `additionalProperties`, nullability, defaults,
required fields, references, unions, and unsupported constructs need deliberate
handling or rejection, not accidental loss.

In the pinned [MEAI transform][schema-transform], `DisallowAdditionalProperties`
adds `false` only when the keyword is absent, while `RequireAllProperties`
replaces the required list. Requiring all properties does **not** make formerly
optional values nullable. Implementers MUST NOT describe this transformation as
preserving omission semantics or providing complete JSON Schema support.

## 7. Options, services, and model identity

Each provider MUST publish mapped options, valid ranges, native defaults,
ignored/advisory fields, unsupported guarantees, and conflict precedence.
Null fields SHOULD preserve native defaults rather than force an arbitrary
sampling mode. Invalid nonfinite/out-of-range values MUST be rejected before
native invocation; adapters MUST NOT silently clamp or wrap signed values into
unsigned/native ranges.

Temperature, nucleus/top-k sampling, seed, reasoning, stop conditions, and token
limits are native/model-specific. There is no universal temperature range,
reasoning setting, deterministic-seed promise, or equivalence between a validated
token count and an enforced native output limit. Required capabilities MUST be
honored, rejected, or explicitly exposed as a weaker/advisory profile.

Metadata and responses MUST identify the actual selected/executed model using
a stable identifier, not echo an unselected caller `ModelId`. A provider with
one fixed model MUST document ignored/rejected model overrides.
`GetService` SHOULD return metadata and the matching client/native service where
appropriate; unsupported types/keys MUST return null rather than unrelated
objects. Fixed local identity is not a claim about a cloud deployment's resolved
snapshot identifier.

## 8. Streaming and usage

Text updates MUST be append-only deltas within each message/content stream.
Cumulative native snapshots MUST be normalized exactly once; native deltas MUST
NOT be differenced again. Repeated snapshots MUST NOT duplicate text. If a
producer revises an emitted prefix, use authoritative completion metadata,
buffer until stable, or fail explicitly; resetting and resending text is invalid.
The [JSON chunker design](json-stream-chunker-design.md) describes one fallback,
not a requirement to replace a native delta protocol.

Successful structured text deltas MUST concatenate to one valid final JSON
document equivalent to the authoritative final output. Every intermediate prefix
need not be valid JSON; object key order need not match native snapshot order.
Tool argument fragments MUST be assembled before exposing an invokable
`FunctionCallContent`, with parse errors surfaced. Role and message IDs, when
available, MUST preserve boundaries and order: [MEAI aggregation][stream-boundaries]
uses role/ID changes to create messages.

Metadata-only updates are valid; implementations MUST NOT assume every update
contains answer text. Local providers SHOULD label every update with the stable
model ID. A single completed update is valid when native progress is unavailable;
providers MUST NOT fabricate progress or split completed text to simulate it.
Final native status/errors MUST be checked even after progress; final text MUST
NOT be appended a second time.

[MEAI adds each `UsageContent` to aggregate usage][stream-usage]. Native cumulative
totals MUST therefore be emitted once per execution, or converted into genuine
additive deltas. Unavailable counts MUST remain null/absent, not fabricated zero
or unlabeled estimates. Supported usage fields MUST reflect the native execution.
Independent generations are not expected to produce identical text or usage.

## 9. Cancellation, failures, ownership, and privacy

Request cancellation, enumerator cancellation, and early stream disposal MUST
be connected to native work. Cancellation MUST NOT become an empty successful
response. Adapters MUST keep callbacks and request context alive until native
completion/termination, release owned resources safely, and observe late faults
even if the consumer has left.

Validation, model readiness, context-limit, schema/decode, tool, and final native
status failures MUST be explicit. Providers MUST NOT turn malformed native success
into invented content, automatically retry side-effecting tool execution, or
silently fall back to another provider. A native inability to abort immediately
MUST be documented; cancellation is not a guarantee that a shared model has
already recovered.

Concurrent requests MUST have isolated state; serialize native access where
necessary without cross-request history leakage. Document narrower native limits
consistently with [MEAI's concurrent-use expectation][ichatclient]. Callers must
not dispose clients still in use. Providers MUST dispose resources they own, not
borrowed models/services, and disposal MUST be idempotent. Do not impose a
universal post-disposal exception rule: valid stateless/no-op wrappers exist.

Local inference MUST NOT upload input or fall back to cloud inference without
explicit application authorization. Model downloads and application-invoked tools
have separate network/consent policies; on-device inference does not imply those
are offline. Remote content fetching MUST be explicit. Prompt, image, argument,
and result logging MUST require sensitive-data opt-in; use standard MEAI logging/
telemetry middleware and preserve native callback trace context where supported.

## 10. Conformance evidence

Providers MUST bind every applicable [shared chat suite][shared-tests] and declare
capability differences through explicit hooks/overrides, preserving assertions
and `RequiresModel` traits. Provider-only tests MUST name the native policy or
API that prevents sharing. Prefer model-free conversion/protocol tests for
backend-neutral rules; add native/live tests for behavior those tests cannot prove.

| Area | Required evidence for applicable capabilities |
|------|-----------------------------------------------|
| Instructions | Exact native layout and live complementary semantics; options-first order; blanks, repeats, interleaved System/Developer; case-insensitive roles; no last-wins assumption. |
| Inputs/content | Single enumeration, caller nonmutation, null/defaults, unsupported parts beside valid text, empty User preservation or declared rejection, malformed supported payloads, accepted history order. |
| Tools | IDs/names/arguments/results, prior-call matching, ambiguity/duplicate/parallel policy, retained context, None/required/named modes, exact-once native/middleware execution. |
| Schema | Input immutability, transform reuse, explicit keyword conflicts, optional versus nullable, nested shapes, unsupported constructs, schema-free versus constrained behavior. |
| Options/identity | Native defaults and valid/invalid boundaries, signed conversions, unsupported guarantees, actual model selection and update metadata. |
| Streaming | Snapshots versus deltas, duplicates/revisions, same-execution stream/final equivalence, message/tool order, metadata-only updates, unavailable progress, late final failure without duplicate text. |
| Usage | Final totals versus additive deltas, absent versus measured counts, aggregation and telemetry without sensitive payloads. |
| Lifecycle | Pre/in-flight cancellation, early break, assignment/completion races, late faults, concurrency isolation, owned/borrowed disposal, explicit errors and no unauthorized fallback. |

Live baselines MUST use configured cloud deployments/secrets, not hardcoded model
assumptions. Disable reasoning for temperature-based cloud tests only where that
deployment requires it; do not promote that setting to a universal contract.
Compare a stream with its **own** authoritative final result, not a separate
generation. Hardware/model limitations and tests not run MUST remain visible;
compilation and a skipped suite are not inference evidence.

## 11. Current implementations and open gaps

This is an evidence snapshot as of **2026-10-18**, not a release/support matrix.
Apple is in this layer; Windows and Android evidence comes from the stacked
provider branches. The requirements above are the target, not a claim that
existing implementations already satisfy every rule.

| Provider/profile | Observed behavior | Gaps and qualifications |
|------------------|-------------------|-------------------------|
| Apple / exact native history | [Managed conversion][apple-managed] prepends options as a System message; existing System messages stay interleaved. [Swift][apple-native] maps them to `Transcript.instructions`; the final effective prompt must be User. Native tools, schema-constrained JSON, OS 27 vision/usage, and native JSON deltas are distinct capabilities. | Developer is currently ignored. Required/named tool mode is not enforced (only None versus registration). [Tool schema parse failures][apple-tools] substitute a string schema. A null/empty native success becomes an empty Assistant response. These are gaps, not recommended fallback policies. |
| Windows / single instruction slot plus prompt-only history | [Normalization][windows-snapshot] collects nonblank options first, then System messages in encounter order and removes them from converted history. Text history/tool replay is flattened; execution requests reject; usage is absent. Structured generation embeds instructions in its prompt rather than a native system context. | Good shared baseline coverage is not complete conformance. Developer is omitted; separators use `Environment.NewLine` rather than fixed LF. Textual tool replay does not retain both name and call ID. Token-limit enforcement, structured progress/final status, and schema/option limitations remain explicitly provider-specific. |
| Android Gemini Nano / single instruction slot plus weaker history | [Managed conversion][android-managed] prepends options as a System DTO; [Kotlin][android-native] hoists all System parts in encounter order using `\n\n` and removes them from conversation contents. Tools/JSON reject; native streaming, usage and cancellation have dedicated lifecycle handling. | Blank instruction chunks are not filtered; Developer/custom/Tool roles and unknown content reject rather than use the default tolerance profile. Empty messages reject even with options instructions. None plus definitions needs alignment; applicable shared Messages/Options/JSON/Function/Cancellation/Usage suite binding is incomplete. Native model/hardware inference gaps remain. |

Apple's ordered [Transcript][apple-transcript] mapping MAY remain preferable to
single-slot hoisting when OS/model evidence confirms its instruction semantics.
It MUST NOT be described as already implementing single-slot normalization.
Repeated/interleaved instructions across Apple OS versions, Developer hierarchy
collapse, Windows structured progress/final behavior, snapshot revision patterns,
schema conflicts, duplicate/parallel tool identity, native concurrency and cancel
recovery, deployment-specific cloud sampling, and Android hardware inference all
remain explicit validation questions. Resolve them with evidence and update
provider profiles/tests; do not silently broaden a passing test into a guarantee.

## References

The linked upstream adapter snapshots are evidence for the decision process,
not extra universal contracts. Recheck mappings when upgrading dependencies.

- MEAI: [IChatClient contract][ichatclient], [role semantics][chatrole],
  [OpenAI Chat instructions][openai-chat],
  [Responses instructions][openai-responses-options] and
  [role/content conversion][openai-responses-content],
  [stream message boundaries][stream-boundaries], [usage aggregation][stream-usage],
  [schema transformations][schema-transform].
- Comparable APIs/adapters: [Google conversion][google-instructions],
  [Google content roles][google-content], [AWS system conversion][aws-instructions],
  [Bedrock Converse][bedrock-converse], [Bedrock Message][bedrock-message],
  [Anthropic Messages][anthropic-messages], [Apple Transcript][apple-transcript].
- Repository: [testing guidance](../../.github/instructions/testing.instructions.md),
  [Apple images](apple-foundation-models-image-input.md),
  [stream chunking](json-stream-chunker-design.md).

[ichatclient]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/IChatClient.cs#L13-L69
[chatrole]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatRole.cs#L19-L58
[openai-chat]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs#L144-L165
[openai-responses-options]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L900-L905
[openai-responses-content]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L1007-L1109
[stream-boundaries]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatResponseExtensions.cs#L540-L551
[stream-usage]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatResponseExtensions.cs#L622-L664
[schema-transform]: https://github.com/dotnet/extensions/blob/v10.4.1/src/Libraries/Microsoft.Extensions.AI.Abstractions/Utilities/AIJsonUtilities.Schema.Transform.cs#L127-L141
[google-instructions]: https://github.com/googleapis/dotnet-genai/blob/dcde8ab01e38bbebd9282ad0f8ed3f36cf376409/Google.GenAI/GoogleGenAIChatClient.cs#L373-L401
[google-content]: https://github.com/googleapis/dotnet-genai/blob/dcde8ab01e38bbebd9282ad0f8ed3f36cf376409/Google.GenAI/types/Content.cs#L29-L46
[aws-instructions]: https://github.com/aws/aws-dotnet-ai/blob/843677a6a29c36c2b118caedf32004e9e1a12e3e/src/AWS.Bedrock.MEAI/BedrockChatClient.cs#L651-L683
[bedrock-converse]: https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_Converse.html
[bedrock-message]: https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_Message.html
[anthropic-messages]: https://platform.claude.com/docs/en/api/messages
[apple-transcript]: https://developer.apple.com/documentation/foundationmodels/transcript
[shared-tests]: ../../tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Tests
[apple-managed]: ../../src/AI/Microsoft.Maui.Essentials.AI/Platform/MaciOS/AppleIntelligenceChatClient.cs
[apple-native]: ../../src/AI/AppleNative/EssentialsAI/ChatClient.swift
[apple-tools]: ../../src/AI/AppleNative/EssentialsAI/ToolNative.swift
[windows-snapshot]: https://github.com/dotnet/maui-labs/blob/791030e39ba6bd100ff4b4cfd7cb4b75ea5153c8/src/AI/Microsoft.Maui.Essentials.AI/Platform/Windows/WindowsAIChatClient.cs#L361-L441
[android-managed]: https://github.com/dotnet/maui-labs/blob/b38b74b6c098847f2a6dba4905a8d7023171bc65/src/AI/Microsoft.Maui.Essentials.AI/Platform/Android/GeminiNanoChatClient.cs#L157-L196
[android-native]: https://github.com/dotnet/maui-labs/blob/b38b74b6c098847f2a6dba4905a8d7023171bc65/src/AI/AndroidNative/EssentialsAI/src/main/kotlin/com/microsoft/maui/essentials/ai/GeminiNanoNativeClient.kt#L96-L153
