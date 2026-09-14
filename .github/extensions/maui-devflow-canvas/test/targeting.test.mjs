import assert from "node:assert/strict";
import test from "node:test";
import { agentIdentityKey, resolveAgentTarget } from "../targeting.mjs";

const todo = {
  id: "todo-old",
  project: "C:\\src\\MauiTodo.csproj",
  tfm: "net10.0-windows10.0.19041.0",
  platform: "Windows",
  appName: "MauiTodo",
  sessionId: "worktree-a",
  port: 9225,
};

test("Canvas target identity survives a process id and port change", () => {
  const restarted = {
    ...todo,
    id: "todo-new",
    project: "C:/src/MauiTodo.csproj",
    platform: "windows",
    port: 9335,
  };

  const result = resolveAgentTarget([restarted], {
    targetId: todo.id,
    targetIdentity: agentIdentityKey(todo),
    preferredPort: todo.port,
  });

  assert.equal(result.state, "ready");
  assert.equal(result.agent.id, restarted.id);
});

test("Canvas never substitutes an unrelated app for a pinned target", () => {
  const other = { ...todo, id: "other", appName: "Weather", port: todo.port };

  const result = resolveAgentTarget([other], {
    targetId: todo.id,
    targetIdentity: agentIdentityKey(todo),
    preferredPort: todo.port,
  });

  assert.deepEqual(result, { agent: null, state: "target" });
});

test("Canvas does not reconnect to an identical app from another session", () => {
  const otherWorktree = { ...todo, id: "other", sessionId: "worktree-b", port: 9445 };

  const result = resolveAgentTarget([otherWorktree], {
    targetId: todo.id,
    targetIdentity: agentIdentityKey(todo),
    preferredPort: todo.port,
  });

  assert.deepEqual(result, { agent: null, state: "target" });
});

test("Canvas requires a new choice for relative project identities shared by worktrees", () => {
  for (const project of ["MauiTodo.csproj", "samples/MauiTodo.csproj", "C:MauiTodo.csproj"]) {
    for (const sessionId of ["dwampledevflowsamplecsproj", undefined]) {
      const selected = { ...todo, project, sessionId };
      const otherWorktree = { ...selected, id: "other-worktree", port: selected.port };

      assert.equal(agentIdentityKey(selected), null);
      assert.deepEqual(resolveAgentTarget([otherWorktree], {
        targetId: selected.id,
        targetIdentity: agentIdentityKey(selected),
        preferredPort: selected.port,
      }), { agent: null, state: "target" });

      assert.equal(resolveAgentTarget([otherWorktree], {
        targetId: otherWorktree.id,
      }).agent.id, otherWorktree.id);
    }
  }
});

test("Canvas reconnects the same registered process with relative metadata", () => {
  const selected = { ...todo, project: "MauiTodo.csproj" };

  assert.equal(resolveAgentTarget([selected], {
    targetId: selected.id,
    targetIdentity: agentIdentityKey(selected),
  }).agent.id, selected.id);
});

test("Canvas distinguishes full project paths in separate worktrees", () => {
  const otherWorktree = { ...todo, id: "other", project: "C:\\other\\MauiTodo.csproj" };

  assert.deepEqual(resolveAgentTarget([otherWorktree], {
    targetId: todo.id,
    targetIdentity: agentIdentityKey(todo),
  }), { agent: null, state: "target" });
});

test("Canvas requires a choice when no target is pinned and several apps run", () => {
  const other = { ...todo, id: "other", appName: "Weather", port: 9445 };

  const result = resolveAgentTarget([todo, other]);

  assert.deepEqual(result, { agent: null, state: "multiple" });
});
