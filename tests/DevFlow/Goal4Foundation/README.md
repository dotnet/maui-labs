# Diagnostic qualification corpus

`qualification-example.json` is an intentionally insufficient schema example,
not measurements. `Goal4QualificationTests` is the deterministic boundary corpus:
Wilson precision versus point estimate, five emulator passes, each Tier-1 flow's
own denominator, zero false-heal/privacy thresholds, ECE and p95 boundaries,
and malformed/missing counts.

These cases are generated host policy evidence, never device observations.
Even caller-supplied metrics that satisfy every numeric gate produce
`qualification: not-qualified`. Provenance, immutable first-attempt artifacts,
independent reviews, report/recording validity, Android overhead and platform
runtime evidence require separate owners and are not certified by this evaluator.

The policy thresholds are adapted from fork `fb2db33e`'s
`MauiQualificationGateThresholds`. This smaller corpus does not claim the old
Inspector corpus has been qualified or that the full live classifier was ported.
