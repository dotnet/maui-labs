const assert = require("node:assert/strict");
const vm = require("node:vm");

function installBodyGuard(workflow) {
  const blocks = [...workflow.matchAll(/^( +)script: \|-?\n((?:\1  .*(?:\n|$)|\n)+)/gm)];
  const scripts = blocks.map(([, indent, body]) =>
    body.replace(new RegExp(`^${indent}  `, "gm"), ""));
  const matches = scripts.filter(script => script.includes('const original = "/opt/gh-aw/actions/docs_original_handler_manager.cjs";'));
  assert.equal(matches.length, 1, "One final-body guard installer must be compiled");
  let renamed = false;
  let wrapper;
  const entrypoint = "/opt/gh-aw/actions/safe_output_handler_manager.cjs";
  const original = "/opt/gh-aw/actions/docs_original_handler_manager.cjs";
  vm.runInNewContext(matches[0], {
    require(name) {
      assert.equal(name, "node:fs");
      return {
        existsSync(file) { assert.equal(file, original); return false; },
        renameSync(from, to) {
          assert.equal(from, entrypoint);
          assert.equal(to, original);
          renamed = true;
        },
        writeFileSync(file, content, encoding) {
          assert.equal(file, entrypoint);
          assert.equal(encoding, "utf8");
          assert.ok(renamed);
          wrapper = content;
        },
      };
    },
  });
  assert.equal(typeof wrapper, "string");
  return wrapper;
}

async function withBodyLimit(workflow, action, github, core) {
  const module = { exports: {} };
  vm.runInNewContext(installBodyGuard(workflow), {
    module, github, core,
    require(name) {
      assert.equal(name, "./docs_original_handler_manager.cjs");
      return { main: action };
    },
  });
  return module.exports.main();
}

module.exports = { installBodyGuard, withBodyLimit };
