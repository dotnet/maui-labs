import { createHash } from "node:crypto";
import { realpath } from "node:fs/promises";
import * as path from "node:path";

export async function resolveLocalSourceFile(file: string): Promise<string> {
  const resolved = await realpath(file);
  if (/^[\\/]{2}/.test(resolved)) {
    throw new Error("UNC/network source paths are not supported.");
  }
  return resolved;
}

export function isInside(root: string, target: string): boolean {
  const rel = path.relative(root, target);
  return rel.length > 0 && !rel.startsWith("..") && !path.isAbsolute(rel);
}

export function sourceHashMatches(text: string, sourceHash: string): boolean {
  const currentHash = createHash("sha256").update(text, "utf8").digest("hex").slice(0, 32);
  return currentHash === sourceHash.toLowerCase();
}
