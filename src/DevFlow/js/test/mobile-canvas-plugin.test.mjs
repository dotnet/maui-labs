import test from "node:test";
import assert from "node:assert/strict";
import { readFile, access } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../../../..");

/**
 * The Mobile Canvas companion is a separately installed, experimental binary that most machines do
 * not have. Registering its stdio MCP server from the plugin manifest would start a permanently
 * failing server for every user who enables the `dotnet-maui` skills, which makes an optional layer
 * a visible dependency of the plugin.
 *
 * The manifest format has no way to gate a server behind a setting, so the plugin does not register
 * it at all. The two surfaces that can gate it do: the VS Code extension exposes an off-by-default
 * `mauiDevflow.registerMobileCanvasMcpServer`, and a user can always run
 * `maui devflow devices host mcp` themselves.
 */
test("dotnet-maui plugin does not auto-register the optional Mobile Canvas MCP server", async () => {
  const plugin = JSON.parse(await readFile(
    resolve(root, "plugins/dotnet-maui/plugin.json"),
    "utf8",
  ));

  assert.equal(plugin.mcpServers, undefined);
  await assert.rejects(() => access(resolve(root, "plugins/dotnet-maui/.mcp.json")));
});

test("the device layer skill states that it is experimental and optional", async () => {
  const skill = await readFile(
    resolve(root, "plugins/dotnet-maui/skills/devflow-device-layer/SKILL.md"),
    "utf8",
  );

  assert.match(skill, /Experimental and optional/);
  assert.match(skill, /no Mobile Canvas host installed/);
});

test("the VS Code host keeps the optional device MCP server off by default", async () => {
  const manifest = JSON.parse(await readFile(
    resolve(root, "src/DevFlow/js/vscode-inspector/package.json"),
    "utf8",
  ));

  const setting =
    manifest.contributes.configuration.properties["mauiDevflow.registerMobileCanvasMcpServer"];
  assert.equal(setting.default, false);
  assert.deepEqual(
    manifest.contributes.mcpServerDefinitionProviders.map((provider) => provider.id),
    ["mauiDevflow.mobileCanvasMcp"],
  );
});
