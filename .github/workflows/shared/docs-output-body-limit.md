---
safe-outputs:
  # Importing this guard must not enable noop in workflows that disable it.
  noop: false
  steps:
    - name: Guard final documentation output body sizes
      id: guard_output_body_sizes
      uses: actions/github-script@v8
      with:
        script: |
          const fs = require("node:fs");
          const entrypoint = "/opt/gh-aw/actions/safe_output_handler_manager.cjs";
          const original = "/opt/gh-aw/actions/docs_original_handler_manager.cjs";
          if (fs.existsSync(original)) {
            throw new Error("Documentation output body guard was already installed.");
          }
          fs.renameSync(entrypoint, original);
          // gh-aw v0.53.5 has no final-body hook; retain its dispatcher and guard its API client.
          fs.writeFileSync(entrypoint, `
          const handler = require("./docs_original_handler_manager.cjs");
          module.exports = {
            ...handler,
            async main(...args) {
              const restorations = [];
              for (const resource of ["issues", "pulls"]) {
                for (const operation of ["create", "update"]) {
                  const client = github.rest[resource];
                  const send = client[operation];
                  client[operation] = async function (params) {
                    // UTF-16 length is conservative for non-BMP text; never truncate disclosure or provenance.
                    if (typeof params.body === "string" && params.body.length > 65536) {
                      const message = "Documentation output " + resource + "." + operation
                        + " body exceeds GitHub's 65,536-character limit after disclosure and generated content ("
                        + params.body.length + " UTF-16 code units). Shorten the generated body and retry.";
                      core.setFailed(message);
                      throw new Error(message);
                    }
                    return send.call(this, params);
                  };
                  restorations.push(() => { client[operation] = send; });
                }
              }
              try {
                return await handler.main(...args);
              } finally {
                for (const restore of restorations) restore();
              }
            }
          };
          `, "utf8");
---
