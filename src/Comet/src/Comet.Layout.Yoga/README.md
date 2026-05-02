# Comet.Layout.Yoga

Pure C# port of [Meta's Yoga](https://github.com/facebook/yoga) flexbox layout engine.

## Provenance

Ported from [microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor)
at commit `7c90d29` (April 2026), `src/Reactor/Yoga/`. The Reactor port is a
faithful C# translation of Meta's C++ Yoga (method-for-method).

Namespace renamed `Microsoft.UI.Reactor.Layout` → `Comet.Layout.Yoga`. The
WinUI-specific `FlexPanel.cs` was not ported — Comet wraps `YogaNode` with
its own MAUI-facing layout managers in `Comet/Layout/`.

## Licence

MIT. See `THIRD-PARTY-NOTICES.md` for upstream attribution to Meta Platforms
(Yoga) and Microsoft (Reactor C# port).

## Scope

Layout algorithm only — `YogaNode`, `YogaStyle`, `YogaAlgorithm`, and
supporting types. **Zero** dependencies on MAUI, WinUI, or any UI framework.
Intentionally extractable for future Reactor-for-MAUI sharing or upstream
`dotnet/maui` adoption.

## Upstream sync

- Initial port: `microsoft-ui-reactor@7c90d29` (April 2026)
