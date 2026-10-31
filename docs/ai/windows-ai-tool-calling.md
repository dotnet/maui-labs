# Experimental Windows AI tool calling

`WindowsAIToolCallingClient` is an explicitly `[Experimental(MAUIAI0001)]`
adapter above `WindowsAIChatClient`, not a native Windows function-calling API.
The [Windows language-model guide](windows-ai-language-models.md) describes the
current SDK, hardware, packaging, readiness, and native lifetime requirements.
The Windows Chat Playground always installs the adapter. Library consumers
opt in by constructing it and placing function invocation outside it:

```csharp
IChatClient client = new WindowsAIToolCallingClient(new WindowsAIChatClient())
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();
```

## Flow

The model sees ordinary `AIFunction` names, descriptions, parameter types,
required state, and parameter descriptions. Default selection returns
`{reason,decision}` with one function or `answer_without_tool`.
`AllowMultipleToolCalls=true` instead requests
`{reason,tool_names,more_tools_after_results}`: independent functions can be
selected together, followed by another planning round for dependent calls.

Every selected function gets a constrained extraction request:
`{evidence: string[], arguments: object}`. It sees labeled user messages and
completed tool results as source data rather than the original role history.
Zero-argument functions return an empty argument object but still cite request
evidence. Parameterized functions use their ordinary schema with argument
properties made nullable during extraction. Windows requires every property
key, so the model returns `null` for an unavailable value instead of inventing
one. The host removes those placeholders recursively, preserves array positions,
and validates the remaining values against the original function schema.
Missing required values clarify; omitted optional parameters retain their
ordinary C# defaults. This is generic schema handling, not a per-tool normalizer.
When the caller requests structured JSON, clarification uses the final-answer
phase with that same schema rather than returning an unformatted text question.

The host verifies evidence provenance and the supported schema subset before
emitting standard `FunctionCallContent`. `UseFunctionInvocation()` executes the
application functions. The final answer receives typed JSON tool results,
not the raw function-call transcript. Final-answer streaming remains streaming;
planning and extraction are buffered. Caller response formats are preserved for
the final answer. All inner calls clear `Tools`, `ToolMode`, and
`AllowMultipleToolCalls`; the native client does not register managed functions.
The adapter validates original caller options and recognized message content
before any selection or tool dispatch. Function calls belong in Assistant
history and results in Tool history; misplaced content is rejected rather than
reclassified as trusted tool data.

## Bounds and limitations

- One call per user turn by default; up to three when multiple calls are enabled.
- Each function name is selectable once per user turn, even with different arguments.
- Valid independent calls can dispatch while missing/invalid dependent calls
  defer. Diagnostics are attached in `FunctionCallContent.AdditionalProperties`
  under `windows_ai.reason`, `windows_ai.evidence`, `windows_ai.deferred_tools`,
  and `windows_ai.deferred_reasons`.
- Unsupported zero-argument selections are suppressed rather than retried, with
  `windows_ai.suppressed_tools` and `windows_ai.suppressed_reasons` diagnostics.
- If all selected calls lack required values, the adapter asks for them.
  Malformed output and schema failures surface explicitly when no call is valid.
- Exact quotes, exact labeled sources, and JSON-encoded exact quotes are
  accepted. Structurally identical JSON tool-result evidence is accepted after
  removing decoder-inserted control characters; user JSON does not get that
  exception.
- Quote-opened role prefixes such as `Assistant:`, `System:`, `User:`,
  `<assistant>`, and `<|assistant|>` are vetoed as executable evidence.
  This is a narrow mitigation, not general prompt-injection protection.
- Evidence proves textual provenance, not semantic correctness. The model can
  infer `ORD-204` from `204`, but can also infer a wrong schema-valid value.
  No developer normalizer or per-function resolver is required.
- Supported schemas include closed objects, nested objects, arrays, primitive
  types, enum, required, numeric bounds, Unicode scalar string length, and
  ECMA-compatible patterns. Nullable types in the original function schema,
  general unions/references, and `format` annotations remain unsupported and
  fail explicitly. The nullable extraction grammar does not broaden this
  supported input-schema subset.
- Each native phase has a 30-second bound and no automatic retry. An interrupted
  phase terminally disables the wrapper instance because native termination
  cannot be confirmed. Native stalls can persist across fresh clients.
  In a long-lived playground client, cancellation therefore requires recreating
  the client (currently restarting the app). Ownership-aware recovery is not
  implemented for wrappers around arbitrary or caller-supplied native models.
  An ordinary completed validation, readiness, or generation error propagates
  without quarantining the wrapper. The 30-second bound is a total phase
  deadline, including final answers and tool-less requests; it is not an
  inactivity timer.

Do not expose consequential, privacy-sensitive, or side-effecting functions
without separate application authorization. This research adapter does not
enable image input or image generation.

## Evidence and validation

Historical measurements on 2026-09-28 used an AMD Ryzen AI 7 PRO 350, Windows
build 26200, and KB5124881 component 1.2608.951.0; the active model version was
not exposed. The adapter completed 10/10 core turns (26 native requests) and
20/20 frozen development-regression turns (50 native requests). Both quoted-role
cases still selected weather in the model; the host veto prevented dispatch.
These cases were used during development and are not an untouched accuracy
benchmark. These observations do not validate newer Windows model/runtime
versions or establish general reliability.

The original scripted suite passed 109/109 both portably and in a packaged MSIX
process with TCP begin/result/end events. Real-model independent-batch,
derived-order, and dependent-chain probes are retained separately.

### Restart measurements (2026-10-31)

The adapter was rebased onto main, retaining the current native lifecycle,
strict schema transformation, telemetry, and device runner. The native provider
now owns a separate model per request; the evaluators reuse a single *client*,
not a single native model. A reproduced missing-value failure caused by the
strict schema transform was repaired with the nullable extraction contract above.

| Check | Observation |
| --- | --- |
| Portable AI unit suite | 414/414 passed, including 120 wrapper cases |
| Packaged scripted wrapper cases | 120/120; fresh TCP begin/result/end and matching TRX |
| Native preflight | Complete in 2.3 seconds |
| Core development regression | 10/10; 26 native requests; no wrong calls or errors |
| Missing order ID | Both trials produced `orderId: null` and clarification, not a fabricated ID |
| Ordinary calculator | Selected `calculate(2, "add", 3)` and answered `5` |
| Multiple/dependent regression | Stopped on the first independent-two case: weather cited fabricated evidence, was withheld, then replanning hit the 30-second native timeout |

No retry or prompt tuning followed that timeout. The later batch, dependency,
prior-result, and role-text cases were **not reached** in this run. Single-call
results are encouraging, but these observations do not establish reliable
multiple/dependent calls. The old 20/20 development-regression figure is not a
measurement of the updated extraction grammar.

In that cleanup iteration, the main client was reduced from 1,517 to 1,182 lines.
Subsequent review fixes added request-validation and lifecycle handling, so that
historical line count is not a description of the current file. Schema validation
has one focused production helper; moving it does not itself reduce total
complexity. The helper retains exact numeric bounds and all previous validation
cases. No new classifier/critic, tokenizer-specific delimiters, per-function
argument processing, or generic orchestration framework was added.

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.UnitTests\Microsoft.Maui.Essentials.AI.UnitTests.csproj -f net10.0 --filter 'FullyQualifiedName~WindowsAIToolCallingClientTests'
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 --filter 'FullyQualifiedName~WindowsAIToolCallingClientTests' --logger trx
```

Real-model evaluators additionally require
`-p:EnableWindowsAIToolEvaluation=true` and `RequiresModel=true`. Run one category
at a time with no other model probe active, archive TRX and runner events, and
stop on the first timeout rather than repeatedly retrying. Historical XHarness
commands are not the current runner contract.

The broader development set is named
`WindowsAIToolCallingEvidenceEvaluationTests.ProductionAdapter_DevelopmentRegressionCases`.
It is not a held-out benchmark: every unexpected or wrong dispatch fails,
including quoted-role cases. Answer-quality checks are scenario-specific
regressions, not semantic quality scores. The sampling A/B evaluator identifies
adapter phases by their structured-schema names, asserts a nonzero phase count,
and verifies the requested sampling override; a structured final answer is not
classified as a planner phase.

### Review hardening (2026-10-31)

Whole-PR reviews by Sonnet 5.5 and Gemini 4 Argon identified and verified fixes
for completed streaming faults, structured clarification, pre-dispatch request
validation, role-safe history, evidence-span handling, tool-mode value semantics,
optional/default handling, and evaluator correctness. Subsequent passes by both
reviewers reported no actionable findings.

The final model-free validation passed:

- 682/682 portable AI unit tests, including 388 wrapper/evaluator-proxy cases.
- 391/391 packaged Windows cases: the same 388 cases plus three dispatch-gate
  rejection tests. Fresh TCP begin/result/end records and matching TRX were checked.
- 166/166 Chat Playground tests.
- Windows library and Playground builds: zero warnings and errors.
- Evaluated compile items exclude the three Windows-only scripted source files
  from Android, iOS, and Mac Catalyst device-test builds while retaining the
  portable test project's explicit links and the Windows compilation items.

No live-model accuracy run was repeated for this contract-hardening pass. These
results establish the scripted dispatch/lifecycle contracts, not general model
intent accuracy or recovery from a native platform stall.
