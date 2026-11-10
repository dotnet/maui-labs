import { probePreviewMediaCapabilities } from "./capability-probe.mjs";

const output = document.getElementById("result");

async function run() {
  output.textContent = "Running…";
  const result = await probePreviewMediaCapabilities();
  output.textContent = JSON.stringify(result, null, 2);
  document.title = "DONE: DevFlow Preview media capability probe";
}

document.getElementById("rerun").addEventListener("click", run);
await run();
