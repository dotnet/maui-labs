import * as vscode from "vscode";

const PROVIDER_ID = "mauiDevflow.mobileCanvasMcp";
const SETTING = "mauiDevflow.registerMobileCanvasMcpServer";

/**
 * Offers the OPTIONAL Mobile Canvas device automation server as an MCP server definition.
 *
 * This is deliberately narrow. It does not offer the full local `maui devflow mcp` surface, and it
 * does not fold the companion's tools into the `maui_*` inventory: the companion is a separate
 * product with its own tool namespace, and merging the two would silently change what a `maui_*`
 * name means. Registering it as its own named server keeps the boundary visible to the user and
 * leaves both the full and restricted DevFlow profiles exactly as they were.
 *
 * It is off by default. The companion is an experimental, separately installed binary that this
 * extension does not ship, so registering it unasked would advertise a server that is usually not
 * installed. When the dedicated Mobile Canvas extension is present, that extension owns the
 * definition and this one stands down rather than offering a duplicate.
 *
 * The command itself fails closed: `maui devflow devices host mcp` refuses to start unless the
 * pinned companion is installed and its recorded hashes still verify.
 */
export function registerMobileCanvasMcpProvider(context: vscode.ExtensionContext): void {
  if (typeof vscode.lm?.registerMcpServerDefinitionProvider !== "function") return;

  try {
    context.subscriptions.push(vscode.lm.registerMcpServerDefinitionProvider(PROVIDER_ID, {
      provideMcpServerDefinitions: async () => {
        if (!vscode.workspace.getConfiguration("mauiDevflow").get<boolean>("registerMobileCanvasMcpServer")) {
          return [];
        }
        if (vscode.extensions.getExtension("redth.mobile-canvas")) return [];

        const { resolveMauiCli } = await import("@maui-devflow/client");
        return [
          new vscode.McpStdioServerDefinition(
            "Mobile Canvas (optional device automation)",
            resolveMauiCli(),
            ["devflow", "devices", "host", "mcp"],
            {},
            "1",
          ),
        ];
      },
    }));
  } catch (error) {
    // An optional device feature must never take down the Inspector, which is the only trusted
    // native approval host. VS Code throws here when the provider id is not declared in
    // contributes.mcpServerDefinitionProviders, and on older builds that shape has changed before.
    console.warn("MAUI DevFlow: the optional Mobile Canvas MCP provider was not registered.", error);
  }
}

/** The setting that gates {@link registerMobileCanvasMcpProvider}. Exported for tests. */
export const MOBILE_CANVAS_MCP_SETTING = SETTING;
