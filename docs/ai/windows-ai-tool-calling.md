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
evidence. Parameterized functions use their ordinary schema with top-level
`required` removed during extraction to represent missing values.

The host verifies evidence provenance and the supported schema subset before
emitting standard `FunctionCallContent`. `UseFunctionInvocation()` executes the
application functions. The final answer receives typed JSON tool results,
not the raw function-call transcript. Final-answer streaming remains streaming;
planning and extraction are buffered. Caller response formats are preserved for
the final answer. All inner calls clear `Tools`, `ToolMode`, and
`AllowMultipleToolCalls`; the native client's no-tool guard remains intact.

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
  ECMA-compatible patterns. Other keywords fail explicitly.
- Each native phase has a 30-second bound and no automatic retry. An interrupted
  phase terminally disables the wrapper instance because native termination
  cannot be confirmed. Native stalls can persist across fresh clients.

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

```powershell
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.UnitTests\Microsoft.Maui.Essentials.AI.UnitTests.csproj -f net10.0 --filter 'FullyQualifiedName~WindowsAIToolCallingClientTests'
dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 --filter 'FullyQualifiedName~WindowsAIToolCallingClientTests' --logger trx
```

Real-model evaluators additionally require
`-p:EnableWindowsAIToolEvaluation=true` and `RequiresModel=true`. Run one category
at a time with no other model probe active, archive TRX and runner events, and
stop on the first timeout rather than repeatedly retrying. Historical XHarness
commands are not the current runner contract.
