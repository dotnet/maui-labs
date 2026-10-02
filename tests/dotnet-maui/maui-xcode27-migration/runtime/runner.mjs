import { createHash, randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync, copyFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { evaluate } from './assertions.mjs';
import { beneath as containedOutput, inspectAppBundle } from './preflight.mjs';

const root = dirname(fileURLToPath(import.meta.url));
const bundle = 'com.example.xcode27qualification';
const [operation, ...args] = process.argv.slice(2);
const options = {};
for (let i = 0; i < args.length; i += 2) {
  if (!args[i]?.startsWith('--') || !args[i + 1] || args[i + 1].startsWith('--'))
    throw new Error('Expected --name value pairs.');
  options[args[i].slice(2)] = args[i + 1];
}
const required = name => {
  if (!options[name]) throw new Error(`Missing --${name}`);
  return options[name];
};
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const writeJson = (path, value) => writeFileSync(path, JSON.stringify(value, null, 2) + '\n');
const beneath = path => containedOutput(root, path);
const files = directory => readdirSync(directory, { withFileTypes: true })
  .flatMap(entry => {
    if (entry.isSymbolicLink()) throw new Error(`Symlinks are not accepted: ${entry.name}`);
    return entry.isDirectory() ? files(join(directory, entry.name)) : [join(directory, entry.name)];
  });
const ownedFiles = () => [
  ...files(join(root, 'Harness')), ...files(join(root, 'Platforms')),
  ...['LifecycleFixture.csproj', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'assertions.mjs', 'runner.mjs', 'preflight.mjs'].map(p => join(root, p)),
].sort();
const fingerprint = paths => Object.fromEntries(paths.map(path => [relative(root, path), hash(readFileSync(path))]));
const verify = (manifestPath, candidateDirectory) => {
  const manifest = JSON.parse(readFileSync(manifestPath));
  const expectedId = hash(JSON.stringify({ schema: manifest.schema, provenance: manifest.provenance,
    candidate: manifest.candidate, fixture: manifest.fixture }));
  if (manifest.id !== expectedId ||
      JSON.stringify(fingerprint(ownedFiles())) !== JSON.stringify(manifest.fixture))
    throw new Error('Fixture changed since preparation; prepare a new run, never update old evidence.');
  const currentCandidate = Object.fromEntries(files(candidateDirectory).sort().map(path =>
    [relative(candidateDirectory, path), hash(readFileSync(path))]));
  if (JSON.stringify(currentCandidate) !== JSON.stringify(manifest.candidate))
    throw new Error('Compiled candidate must match the prepared manifest byte-for-byte.');
  return manifest;
};

async function main() {
  if (operation === 'verify-build') {
    const manifest = verify(resolve(required('manifest')), resolve(required('candidate')));
    console.log(`Verified candidate and fixture: ${manifest.id}`);
    return;
  }
  if (operation === 'prepare') {
    const source = resolve(required('candidate'));
    const output = beneath(required('output'));
    if (existsSync(output)) throw new Error('Refusing to overwrite an existing preparation.');
    const kind = options['candidate-kind'] ?? (source === join(root, 'Candidate') ? 'reference-control' : 'unclassified');
    if (!['reference-control', 'agent-output', 'unclassified'].includes(kind))
      throw new Error('--candidate-kind must be reference-control, agent-output, or unclassified.');
    if (kind === 'agent-output' && source === join(root, 'Candidate'))
      throw new Error('The bundled reference control is not agent evaluation output.');
    const evaluationId = kind === 'agent-output' ? required('evaluation-id') : null;
    const candidates = files(source);
    if (!candidates.length || candidates.some(p => !p.endsWith('.cs') || dirname(p) !== source))
      throw new Error('Candidate must contain only top-level .cs files implementing the documented fixture contract.');
    mkdirSync(join(output, 'candidate'), { recursive: true });
    const candidateHashes = {};
    for (const sourceFile of candidates.sort()) {
      const name = relative(source, sourceFile);
      const bytes = readFileSync(sourceFile);
      candidateHashes[name] = hash(bytes);
      copyFileSync(sourceFile, join(output, 'candidate', name));
    }
    const manifest = { schema: 2, provenance: { kind, evaluationId, classification: 'operator-claim-not-evaluator-verification' },
      candidate: candidateHashes, fixture: fingerprint(ownedFiles()) };
    manifest.id = hash(JSON.stringify(manifest));
    writeJson(join(output, 'candidate-manifest.json'), manifest);
    console.log(JSON.stringify({ candidateDirectory: join(output, 'candidate'),
      candidateManifest: join(output, 'candidate-manifest.json'), id: manifest.id }, null, 2));
    return;
  }

  if (!['verify-app', 'run-ios', 'run-catalyst'].includes(operation))
    throw new Error('Use prepare, verify-build, verify-app, run-ios, or run-catalyst; see README.md.');
  const prepared = beneath(required('prepared'));
  const manifest = verify(join(prepared, 'candidate-manifest.json'), join(prepared, 'candidate'));
  const app = resolve(required('app'));
  const appPreflight = inspectAppBundle(app, manifest,
    operation === 'verify-app' ? required('platform') : operation === 'run-ios' ? 'ios' : 'maccatalyst');
  if (operation === 'verify-app') {
    console.log(JSON.stringify(appPreflight, null, 2));
    return;
  }
  const toolchain = required('toolchain');
  if (!['released', 'supplemental'].includes(toolchain)) throw new Error('--toolchain must be released or supplemental.');
  const buildLog = resolve(required('build-log'));
  const buildLogBytes = readFileSync(buildLog);
  const output = beneath(join(prepared, `run-${randomUUID()}`));
  mkdirSync(output);
  writeJson(join(output, 'app-preflight.json'), appPreflight);
  copyFileSync(buildLog, join(output, 'build.log'));
  const commands = [];
  const execute = (command, parameters, allowFailure = false) => {
    const result = spawnSync(command, parameters, { encoding: 'utf8', timeout: 60_000 });
    commands.push({ command, parameters, status: result.status, stdout: result.stdout, stderr: result.stderr, error: result.error?.message });
    writeJson(join(output, 'commands.json'), commands);
    if (!allowFailure && result.status !== 0) throw new Error(`${command} failed: ${result.stderr || result.error}`);
    return result.stdout?.trim() ?? '';
  };
  const simctl = (...parameters) => execute('xcrun', ['simctl', ...parameters]);
  let eventFile;
  let startupProcess;
  let coldProcess;
  let observed = [];
  let baseline = new Set();
  const nonce = randomUUID();
  const warmUrls = ['warm-a', 'warm-b'].map(name => `x27qualification://qualification/${name}?run=${nonce}`);
  const coldUrl = `x27qualification://qualification/cold?run=${nonce}`;
  const readEvents = () => {
    if (!eventFile || !existsSync(eventFile)) return [];
    // Ignore only an incomplete final write. Malformed complete records fail closed.
    const text = readFileSync(eventFile, 'utf8');
    return text.slice(0, text.lastIndexOf('\n') + 1).split('\n').filter(Boolean).map(line => JSON.parse(line));
  };
  const refresh = () => {
    observed = readEvents().filter(e => !baseline.has(e.process));
    writeFileSync(join(output, 'events.jsonl'), observed.map(e => JSON.stringify(e)).join('\n') + '\n');
    return observed;
  };
  const wait = async (predicate, description) => {
    const deadline = Date.now() + 45_000;
    do {
      const match = predicate(refresh());
      if (match) return match;
      await new Promise(resolve => setTimeout(resolve, 150));
    } while (Date.now() < deadline);
    throw new Error(`Timed out: ${description}`);
  };
  let failure;
  try {
    execute('xcodebuild', ['-version']);
    if (operation === 'run-ios') {
      const udid = required('udid');
      const inventory = JSON.parse(simctl('list', 'devices', '--json'));
      if (!Object.values(inventory.devices).flat().some(d => d.udid === udid && d.state === 'Booted'))
        throw new Error('The explicitly selected simulator must already be booted. Runner will not boot it.');
      execute('xcrun', ['simctl', 'terminate', udid, bundle], true);
      simctl('install', udid, app);
      eventFile = join(simctl('get_app_container', udid, bundle, 'data'), 'Documents', 'lifecycle-events.jsonl');
    } else {
      eventFile = resolve(required('events'));
      // Parent must close any existing fixture instance before this explicit launch.
      const prior = readEvents().findLast(e => e.kind === 'boot');
      if (prior) {
        try { process.kill(prior.pid, 0); throw new Error('Close the existing fixture before run-catalyst.'); }
        catch (error) { if (error.code !== 'ESRCH') throw error; }
      }
    }
    baseline = new Set(readEvents().map(e => e.process));
    if (operation === 'run-ios') simctl('launch', required('udid'), bundle);
    else execute('/usr/bin/open', ['-n', '-a', app]);
    const boot = await wait(events => events.find(e => e.kind === 'boot'), 'fresh startup');
    startupProcess = boot.process;
    await wait(events => events.some(e => e.process === startupProcess && e.kind === 'original.application-activation'), 'startup activation');
    const openUrl = url => operation === 'run-ios'
      ? simctl('openurl', required('udid'), url)
      : execute('/usr/bin/open', ['-a', app, url]);
    for (const url of warmUrls) {
      openUrl(url);
      await wait(events => events.some(e => e.kind === 'original.warm-url' && e.delivery === 'os' && e.value === url), url);
    }
    openUrl(`x27qualification://qualification/contracts?run=${nonce}`);
    await wait(events => events.some(e => e.kind === 'contract.complete'), 'injected contracts');
    if (operation === 'run-ios') simctl('terminate', required('udid'), bundle);
    else {
      // This PID came from this run's own app, not a name-based process lookup.
      process.kill(boot.pid, 'SIGTERM');
      const deadline = Date.now() + 10_000;
      while (true) {
        try { process.kill(boot.pid, 0); }
        catch (error) { if (error.code === 'ESRCH') break; throw error; }
        if (Date.now() > deadline) throw new Error('Fixture did not terminate.');
        await new Promise(resolve => setTimeout(resolve, 100));
      }
    }
    openUrl(coldUrl);
    const coldBoot = await wait(events => events.find(e => e.kind === 'boot' && e.process !== startupProcess), 'OS cold URL launch');
    coldProcess = coldBoot.process;
    await wait(events => events.some(e => e.kind === 'original.cold-url' && e.process === coldProcess && e.value === coldUrl), 'cold URL title effect');
  } catch (error) {
    failure = String(error);
  } finally {
    try { refresh(); } catch (error) { failure ??= String(error); }
    const report = evaluate(observed, { manifestId: manifest.id, startupProcess, coldProcess, warmUrls, coldUrl });
    if (failure) report.runtimeSubset = 'failed';
    Object.assign(report, { failure, manifestId: manifest.id, candidateProvenance: manifest.provenance, toolchainClaim: toolchain,
      appPreflight,
      buildLogSha256: hash(buildLogBytes), operation, startupProcess, coldProcess, warmUrls, coldUrl });
    writeJson(join(output, 'report.json'), report);
    console.log(JSON.stringify({ output, ...report }, null, 2));
    // No subset pass may be confused with full public-toolchain qualification.
    process.exitCode = report.runtimeSubset === 'failed' ? 1 : 2;
  }
}

main().catch(error => { console.error(String(error)); process.exitCode = 1; });
