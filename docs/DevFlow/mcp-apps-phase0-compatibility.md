# MCP Apps Phase 0 compatibility evidence

**Verdict: GO for a production implementation without package changes.**

Tested against the centrally pinned `ModelContextProtocol` **1.1.0** using
`McpAppsCompatibilitySpikeTests` and the stable 2026-01-26 MCP Apps specification
in `modelcontextprotocol/ext-apps`.

The current package can represent:

- typed tool return models with generated `outputSchema` and `structuredContent`;
- direct resources and RFC 6570 resource templates;
- `text/html;profile=mcp-app` resource contents;
- capability negotiation at `extensions["io.modelcontextprotocol/ui"].mimeTypes`;
- current nested tool metadata at `_meta.ui.resourceUri` and `_meta.ui.visibility`;
- current nested resource metadata at `_meta.ui.csp` and `_meta.ui.prefersBorder`;
- negotiating behavior that advertises UI metadata and resources, plus non-negotiating
  behavior that omits them while retaining meaningful text and structured tool results.

The package does not provide Apps-specific CLR metadata classes. That is not a blocker:
small local records can be serialized into the protocol-owned extension and metadata
containers without hand-written/raw JSON strings. The `Extensions` property is marked
`MCPEXP001`; production code should use a narrowly scoped suppression and retain the exact
serialization and fallback contract tests. No package change is required.

Authoritative references:

- <https://github.com/modelcontextprotocol/ext-apps/blob/main/specification/2026-01-26/apps.mdx>
- <https://modelcontextprotocol.io/extensions/apps/overview>
- <https://apps.extensions.modelcontextprotocol.io/api/documents/csp-and-cors.html>

Production registration now uses this proven contract for the compact read-only resource and
structured Problems, Layout, Tree, and Evidence Preview results. Non-negotiating clients retain
the same text fallback, and the explicit local artifact-inspection tool remains resource-free.

Run the executable evidence:

```powershell
dotnet test src\Cli\Microsoft.Maui.Cli.UnitTests\Microsoft.Maui.Cli.UnitTests.csproj `
  --filter FullyQualifiedName~McpAppsCompatibilitySpikeTests
```
