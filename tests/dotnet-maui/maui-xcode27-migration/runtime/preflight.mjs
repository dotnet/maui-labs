import { createHash } from 'node:crypto';
import { lstatSync, readFileSync, readdirSync, readlinkSync, realpathSync, statSync } from 'node:fs';
import { isAbsolute, join, relative, resolve, sep } from 'node:path';
import { spawnSync } from 'node:child_process';
import { isDeepStrictEqual } from 'node:util';

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const outside = path => path === '..' || path.startsWith(`..${sep}`) || isAbsolute(path);

export function beneath(root, path) {
  const full = resolve(path);
  const rel = relative(root, full);
  if (!rel || outside(rel))
    throw new Error('Output must be a new child directory of runtime/.');
  const canonicalRoot = realpathSync(root);
  let ancestor = root;
  for (const part of rel.split(sep)) {
    ancestor = join(ancestor, part);
    const entry = lstatSync(ancestor, { throwIfNoEntry: false });
    if (!entry) break;
    if (entry.isSymbolicLink())
      throw new Error(`Output paths cannot contain symlink ancestors: ${ancestor}`);
    if (outside(relative(canonicalRoot, realpathSync(ancestor))))
      throw new Error(`Output escapes the canonical runtime directory: ${ancestor}`);
  }
  return full;
}

export function inspectAppBundle(path, manifest, platform) {
  if (!['ios', 'maccatalyst'].includes(platform))
    throw new Error('App platform must be ios or maccatalyst.');
  const app = realpathSync(path);
  if (!statSync(app).isDirectory()) throw new Error('--app must be an app bundle directory.');
  const contained = file => {
    const canonical = realpathSync(file);
    if (outside(relative(app, canonical))) throw new Error(`App bundle symlink escapes the bundle: ${file}`);
    return canonical;
  };
  const contents = platform === 'ios' ? app : join(app, 'Contents');
  const infoPath = contained(join(contents, 'Info.plist'));
  const parsed = spawnSync('/usr/bin/plutil', ['-convert', 'json', '-o', '-', infoPath],
    { encoding: 'utf8', timeout: 10_000 });
  if (parsed.status !== 0) throw new Error(`Cannot read app Info.plist: ${parsed.stderr || parsed.error}`);
  const info = JSON.parse(parsed.stdout);
  if (info.CFBundleIdentifier !== 'com.example.xcode27qualification' || info.CFBundlePackageType !== 'APPL')
    throw new Error('App bundle identity does not match the lifecycle qualification fixture.');
  const executableName = info.CFBundleExecutable;
  if (typeof executableName !== 'string' || !executableName ||
      executableName === '.' || executableName === '..' || /[/\\]/.test(executableName))
    throw new Error('App bundle has an invalid executable name.');
  const executable = contained(join(platform === 'ios' ? app : join(contents, 'MacOS'), executableName));
  if (!statSync(executable).isFile()) throw new Error('App executable is not a regular file.');
  const manifestPath = contained(join(platform === 'ios' ? app : join(contents, 'Resources'), 'candidate-manifest.json'));
  const manifestBytes = readFileSync(manifestPath);
  if (!isDeepStrictEqual(JSON.parse(manifestBytes), manifest))
    throw new Error('App published manifest does not match the prepared candidate manifest.');

  const entries = [];
  const visit = directory => {
    for (const entry of readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const file = join(directory, entry.name);
      const name = relative(app, file);
      if (entry.isSymbolicLink()) {
        contained(file);
        entries.push({ path: name, symlink: readlinkSync(file) });
      } else if (entry.isDirectory()) {
        visit(file);
      } else if (entry.isFile()) {
        entries.push({ path: name, sha256: sha256(readFileSync(file)) });
      } else {
        throw new Error(`Unsupported file in app bundle: ${file}`);
      }
    }
  };
  visit(app);
  return {
    assurance: 'Local identity and manifest consistency only; not cryptographic authenticity of operator-supplied code.',
    app, platform, manifestId: manifest.id,
    bundleIdentifier: info.CFBundleIdentifier,
    bundleVersion: info.CFBundleVersion,
    bundleShortVersion: info.CFBundleShortVersionString,
    bundlePackageType: info.CFBundlePackageType,
    supportedPlatforms: info.CFBundleSupportedPlatforms,
    sdkName: info.DTSDKName,
    executable: executableName,
    executableSha256: sha256(readFileSync(executable)),
    infoPlistSha256: sha256(readFileSync(infoPath)),
    publishedManifestSha256: sha256(manifestBytes),
    appSha256: sha256(JSON.stringify(entries)),
    appHashFormat: 'sha256 of JSON directory inventory: relative file paths/content hashes and internal symlink targets',
    entries,
  };
}
