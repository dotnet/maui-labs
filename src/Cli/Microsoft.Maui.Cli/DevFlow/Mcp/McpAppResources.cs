using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

[McpServerResourceType]
public sealed class McpAppResources
{
	[McpServerResource(
		UriTemplate = McpAppMetadata.ResourceUri,
		Name = "maui_devflow_compact_view",
		MimeType = McpAppMetadata.MimeType)]
	[Description("Compact read-only renderer for DevFlow Problems, layout findings, visual-tree fragments, flow-run results, and evidence previews.")]
	public static string CompactView()
		=> """
			<!doctype html>
			<html lang="en">
			<head>
			  <meta charset="utf-8">
			  <meta name="viewport" content="width=device-width,initial-scale=1">
			  <title>MAUI DevFlow</title>
			  <style>
			    :root { color-scheme: light dark; font-family: system-ui, sans-serif; }
			    body { margin: 0; padding: 12px; }
			    h2 { margin: 0 0 8px; font-size: 14px; }
			    pre { white-space: pre-wrap; overflow-wrap: anywhere; font: 12px/1.45 ui-monospace, monospace; }
			    .muted { opacity: .72; }
			  </style>
			</head>
			<body>
			  <h2>MAUI DevFlow</h2>
			  <div id="status" class="muted">Waiting for bounded tool data…</div>
			  <pre id="content"></pre>
			  <script type="module">
			    const status = document.getElementById('status');
			    const content = document.getElementById('content');
			    const render = (value) => {
			      status.textContent = value?.kind || 'DevFlow result';
			      content.textContent = JSON.stringify(value, null, 2);
			    };
			    window.addEventListener('message', (event) => {
			      const data = event.data;
			      if (!data || typeof data !== 'object') return;
			      if (data.type === 'ui/notifications/tool-result' || data.type === 'tool-result')
			        render(data.params?.structuredContent ?? data.structuredContent ?? data.params ?? data);
			    });
			  </script>
			</body>
			</html>
			""";
}
