const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");
const { installBodyGuard, withBodyLimit } = require("./body-limit-helpers.cjs");

const directory = path.resolve(__dirname, "..");
const shared = fs.readFileSync(path.join(directory, "shared/docs-output-body-limit.md"), "utf8");
for (const workflow of ["pr-docs-check", "ai-docs-audit"]) {
  const compiled = fs.readFileSync(path.join(directory, `${workflow}.lock.yml`), "utf8");
  test(`${workflow}: compiled final-payload guard matches shared source and precedes dispatch`, () => {
    assert.equal(installBodyGuard(compiled), installBodyGuard(shared));
    const job = compiled.slice(compiled.indexOf("\n  safe_outputs:"));
    const guard = job.indexOf("id: guard_output_body_sizes");
    assert.ok(guard > job.indexOf("name: Setup Scripts"));
    assert.ok(guard < job.indexOf("id: process_safe_outputs"));
    assert.doesNotMatch(job.slice(guard, job.indexOf("id: process_safe_outputs")), /continue-on-error: true/);
    const tools = JSON.parse(compiled.match(/cat > \/opt\/gh-aw\/safeoutputs\/config.json << 'GH_AW_SAFE_OUTPUTS_CONFIG_EOF'\n\s+([^\n]+)/)[1]);
    assert.equal(Object.hasOwn(tools, "noop"), workflow === "ai-docs-audit", "Keep each workflow's existing noop policy");
  });
  test(`${workflow}: exact final-body boundary enforced before every issue/PR create/update request`, async () => {
    const failures = [];
    const calls = [];
    const github = { rest: {} };
    for (const resource of ["issues", "pulls"]) {
      github.rest[resource] = {};
      for (const operation of ["create", "update"]) {
        github.rest[resource][operation] = async params => { calls.push(params); return "sent"; };
      }
    }
    const original = github.rest.issues.create;
    await withBodyLimit(compiled, async () => {
      for (const resource of ["issues", "pulls"]) {
        for (const operation of ["create", "update"]) {
          for (const body of ["x".repeat(65535), "x".repeat(65536), "\u{1f600}".repeat(32768)]) {
            assert.equal(await github.rest[resource][operation]({ body }), "sent");
          }
          const count = calls.length;
          for (const body of ["x".repeat(65537), "\u{1f600}".repeat(32768) + "x"]) {
            await assert.rejects(github.rest[resource][operation]({ body }), /after disclosure and generated content/);
          }
          assert.equal(calls.length, count, "Oversized payloads must never reach the API");
        }
      }
      assert.equal(await github.rest.issues.update({ state: "closed" }), "sent");
    }, github, { setFailed(message) { failures.push(message); } });
    assert.equal(failures.length, 8, "Mark failure even when upstream handlers catch exceptions");
    assert.equal(github.rest.issues.create, original, "Restore client after dispatch");
  });
}
