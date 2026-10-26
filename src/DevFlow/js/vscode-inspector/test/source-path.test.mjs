import assert from "node:assert/strict";
import { mkdtemp, mkdir, realpath, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { isInside, resolveLocalSourceFile, sourceHashMatches } from "../dist-test/source-path.js";

test("source hashes retain 128 bits and reject stale or legacy metadata", () => {
  const text = "\u65e5\u672c\u8a9e";
  const expected = "77710aedc74ecfa33685e33a6c7df5cc";
  assert.equal(sourceHashMatches(text, expected), true);
  assert.equal(sourceHashMatches(text, expected.toUpperCase()), true);
  assert.equal(sourceHashMatches(`${text}\n`, expected), false);
  assert.equal(sourceHashMatches(text, expected.slice(0, 16)), false);
  assert.equal(sourceHashMatches(text, `${expected.slice(0, 16)}0000000000000000`), false);
});

test("a source file beneath a parent junction outside the workspace requires consent", async (t) => {
  const temporary = await mkdtemp(join(tmpdir(), "devflow-source-path-"));
  t.after(() => rm(temporary, { recursive: true, force: true }));
  const root = await realpath(temporary);
  const workspace = join(root, "workspace");
  const outside = join(root, "outside");
  await mkdir(workspace);
  await mkdir(outside);
  await writeFile(join(outside, "Page.xaml"), "<ContentPage />");
  await symlink(outside, join(workspace, "linked"), "junction");
  const lexicalPath = join(workspace, "linked", "Page.xaml");

  assert.equal(isInside(workspace, lexicalPath), true);
  const resolved = await resolveLocalSourceFile(lexicalPath);
  assert.equal(resolved, join(outside, "Page.xaml"));
  assert.equal(isInside(workspace, resolved), false);
});

test("ordinary local and external linked XAML remain resolvable with correct containment", async (t) => {
  const temporary = await mkdtemp(join(tmpdir(), "devflow-source-path-"));
  t.after(() => rm(temporary, { recursive: true, force: true }));
  const root = await realpath(temporary);
  const workspace = join(root, "workspace");
  await mkdir(workspace);
  const inside = join(workspace, "Inside.xaml");
  const outside = join(root, "Shared.xaml");
  await writeFile(inside, "<ContentPage />");
  await writeFile(outside, "<ContentPage />");

  assert.equal(await resolveLocalSourceFile(inside), inside);
  assert.equal(isInside(workspace, await resolveLocalSourceFile(inside)), true);
  assert.equal(await resolveLocalSourceFile(outside), outside);
  assert.equal(isInside(workspace, await resolveLocalSourceFile(outside)), false);
  await assert.rejects(resolveLocalSourceFile(join(workspace, "Missing.xaml")), { code: "ENOENT" });
});
