// recorder.mjs — workflow-test file storage for the MAUI DevFlow Inspector Canvas host.
//
// Recording itself is broker-owned so every DevFlow host observes the same mutation stream.
// This module only resolves the app's maui-tests directory, persists bounded Markdown returned
// by the broker, and lists saved tests for replay.

import { Buffer } from "node:buffer";
import { existsSync, mkdirSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { basename, dirname, join } from "node:path";
import { homedir } from "node:os";

export const RECORDING_MAX_BYTES = 1024 * 1024;

function slugify(name) {
  const s = String(name || "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return s || "scenario";
}
export { slugify };

function findProjectFiles(root, projectName, depth = 0, matches = []) {
  if (!root || !projectName || depth > 6 || matches.length > 1) return matches;
  let entries;
  try {
    entries = readdirSync(root, { withFileTypes: true });
  } catch {
    return matches;
  }
  for (const entry of entries) {
    if (entry.isSymbolicLink()) continue;
    const full = join(root, entry.name);
    if (entry.isFile() && entry.name.toLowerCase() === projectName.toLowerCase()) {
      matches.push(full);
      if (matches.length > 1) return matches;
      continue;
    }
    if (!entry.isDirectory() || /^(?:\.git|bin|obj|node_modules|artifacts)$/i.test(entry.name)) continue;
    findProjectFiles(full, projectName, depth + 1, matches);
    if (matches.length > 1) return matches;
  }
  return matches;
}

export class Recorder {
  outputRoot(store) {
    const proj = store?.device?.resolvedAgent?.()?.project;
    if (proj && existsSync(proj)) {
      try {
        const dir = statSync(proj).isDirectory() ? proj : dirname(proj);
        if (dir) return join(dir, "maui-tests");
      } catch {
        /* fall through to workspace resolution */
      }
    }

    const projectRoot = store?.device?.opts?.projectRoot;
    const projectName = proj ? basename(proj) : null;
    if (projectRoot && projectName && existsSync(projectRoot)) {
      const matches = findProjectFiles(projectRoot, projectName);
      if (matches.length === 1) return join(dirname(matches[0]), "maui-tests");
    }

    return join(homedir(), ".copilot", "maui-live-canvas", "tests");
  }

  persist(store, { markdown, name } = {}) {
    const md = typeof markdown === "string" ? markdown : "";
    if (!md) return { ok: false, error: "no markdown" };
    if (Buffer.byteLength(md, "utf8") > RECORDING_MAX_BYTES) {
      return { ok: false, error: "recording exceeds the 1 MiB limit" };
    }

    const root = this.outputRoot(store);
    const file = join(root, `${slugify(name || "recording")}.md`);
    try {
      mkdirSync(root, { recursive: true });
      writeFileSync(file, md, "utf8");
      return { ok: true, file, root };
    } catch (e) {
      return { ok: false, error: `Save failed: ${String(e?.message || e)}` };
    }
  }

  list(store) {
    const root = this.outputRoot(store);
    if (!existsSync(root)) return { ok: true, root, tests: [] };
    try {
      const tests = readdirSync(root)
        .filter((f) => f.toLowerCase().endsWith(".md"))
        .map((f) => ({ name: f.replace(/\.md$/i, ""), file: join(root, f) }));
      return { ok: true, root, tests };
    } catch (e) {
      return { ok: false, root, error: String(e?.message || e) };
    }
  }
}
