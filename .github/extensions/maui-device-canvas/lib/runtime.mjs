import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";
import { createHash } from "node:crypto";

const manifestUrl = new URL(
  "../../../../src/Cli/Microsoft.Maui.Cli/DevFlow/Devices/mobile-canvas-runtime-v0.1.16.json",
  import.meta.url,
);

function runtimeKey(platform = process.platform, architecture = process.arch) {
  if (!["win32", "darwin", "linux"].includes(platform)) return null;
  if (!["x64", "arm64"].includes(architecture)) return null;
  return `${platform}-${architecture}`;
}

async function sha256(path) {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest("hex");
}

function validateName(value, label) {
  if (!value || value.includes("/") || value.includes("\\") || value === "." || value === "..") {
    throw new Error(`The pinned Mobile Canvas manifest contains an invalid ${label}.`);
  }
}

export async function loadManifest() {
  const response = await import("node:fs/promises");
  const source = await response.readFile(manifestUrl, "utf8");
  const manifest = JSON.parse(source);
  if (manifest?.schema !== 1 || manifest?.version !== "0.1.16" ||
      manifest?.validatedRevision !== "0f0d7806a08d41b3b0b932c05b313686486f75ca") {
    throw new Error("The pinned Mobile Canvas manifest is inconsistent.");
  }
  return manifest;
}

export async function resolveCommand(options = {}) {
  const manifest = options.manifest || await loadManifest();
  const key = options.runtimeKey || runtimeKey(options.platform, options.architecture);
  const runtime = key && manifest.runtimes?.[key];
  if (!runtime) {
    throw new Error("No pinned Mobile Canvas runtime is available for this platform and architecture.");
  }

  validateName(key, "runtime key");
  validateName(runtime.executable, "executable name");
  if (!/^[a-f0-9]{64}$/.test(runtime.id || "")) {
    throw new Error("The pinned Mobile Canvas runtime ID is invalid.");
  }

  const home = options.homeDirectory || join(homedir(), ".mobile-canvas");
  const directory = join(home, "runtimes", `${key}-${runtime.id.slice(0, 12)}`);

  for (const [name, file] of Object.entries(runtime.files || {})) {
    validateName(name, "runtime file name");
    const path = join(directory, name);
    let info;
    try {
      info = await stat(path);
    } catch {
      throw new Error(
        `Mobile Canvas ${manifest.version} is not installed. ` +
        "Run 'maui devflow devices host install' first.",
      );
    }
    if (!info.isFile() || info.size !== file.size || await sha256(path) !== file.sha256) {
      throw new Error(
        `The installed Mobile Canvas runtime failed integrity verification. ` +
        "Run 'maui devflow devices host update' to repair it.",
      );
    }
  }

  return {
    command: join(directory, runtime.executable),
    directory,
    runtimeKey: key,
    version: manifest.version,
  };
}
