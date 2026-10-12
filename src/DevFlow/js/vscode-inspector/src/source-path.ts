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
