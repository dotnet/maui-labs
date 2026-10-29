# Apple chat quality evaluation

The [device probes](../Microsoft.Maui.Essentials.AI.DeviceTests/Tests/MaciOS/AppleIntelligenceChatClientGroundingTests.cs)
exercise the actual `AppleIntelligenceChatClient`, not a fake client. They use
synthetic facts, assert the expected values, and emit observations through xUnit's
test output. A schema-valid hallucination is a **failed** quality probe.
`RequiresModel=true` keeps these opt-in cases out of the default, model-free CI run.

Run on an eligible Apple Intelligence Mac with the model ready:

```sh
dotnet test tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests \
  -f net10.0-maccatalyst -c Debug -p:UserSecretsId= --logger trx \
  --results-directory artifacts/TestResults \
  --filter FullyQualifiedName~AppleIntelligenceChatClientGroundingTests
```

The command returns nonzero if a ground-truth assertion fails. Do not replace
expected facts with the model's incorrect output just to make it pass.
Keep all trial outputs, not only successful runs. The Python 3.9+ collector
extracts observations from one or more saved TRX files:

```sh
python3 tests/AI/AppleChatEvaluation/collect-results.py \
  artifacts/TestResults/first.trx artifacts/TestResults/second.trx \
  --output artifacts/apple-chat-results.json
```

Replace the example TRX names with the files produced by the device runner.
The collector's successful exit only means collection succeeded; its output
reports the failed assertion count. It omits TRX machine names and stack paths,
but **does not redact prompt/response content**. Use it for these synthetic
fixtures, not private conversations.

## Retained observations

Both datasets were collected on Mac Catalyst 26.7, arm64, with Xcode 26.6 and
.NET SDK 10.0.401. No cloud model or user-secret configuration was used.

- [Before the stream fix](results-before-stream-fix.json): two runs, 20
  observations, six failed assertions. The first overload experiment repeated
  `item` and hit language detection, not a context-window diagnostic. The next
  used full English sentences and reached the context limit.
- [After the stream fix](results-after-stream-fix.json): two runs with the
  unchanged factual prompts and corrected English overload control, 20
  observations, four failed assertions. All 18 structured responses parsed as
  JSON; two requests returned the expected context-window error. Two extraction
  responses had an incorrect departure field and two absent-fact responses
  fabricated an answer.

The before/after run counts are not a quality improvement estimate. Native model
behavior and schema property ordering vary; the deterministic, model-free
chunker tests establish the transport fix. The remaining live failures show why
applications must validate factual output. See the
[full evaluation report](../../../docs/ai/apple-ai-evaluation.md) for methods,
source references, and the distinction between adapter and model behavior.
