// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import assert from 'node:assert/strict';
import { copyFileSync, mkdtempSync, mkdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import test from 'node:test';
import { auditDirectory, auditSource } from '../../../plugins/dotnet-maui/skills/maui-xcode27-migration/scripts/audit-lifecycle.mjs';

const script = fileURLToPath(new URL('../../../plugins/dotnet-maui/skills/maui-xcode27-migration/scripts/audit-lifecycle.mjs', import.meta.url));
const rules = source => auditSource(source, 'Example.cs').map(finding => finding.rule);

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'maui lifecycle audit-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  return {
    root,
    write(path, content) {
      const file = join(root, path);
      mkdirSync(dirname(file), { recursive: true });
      writeFileSync(file, content);
      return file;
    },
  };
}

test('reports the retained FinishedLaunching window body, not its justification comment', () => {
  const source = `class AppDelegate : MauiUIApplicationDelegate {
    // Kept cold-only, but AppDelegate.Window is still null.
    bool FinishedLaunching() {
      Window.RootViewController.Title = url.AbsoluteString;
      return true;
    }
  }`;
  const [finding] = auditSource(source, 'Platforms/iOS/AppDelegate.cs');
  assert.equal(finding.rule, 'X27_APP_WINDOW');
  assert.equal(finding.line, 4);
  assert.equal(finding.file, 'Platforms/iOS/AppDelegate.cs');
  assert.match(finding.message, /even in FinishedLaunching/);
});

for (const expression of ['Window.Root', 'this.Window.Root', 'base.Window?.Root', 'Window!.Root']) {
  test(`reports app-window access: ${expression}`, () => {
    assert.deepEqual(rules(`class Startup : Microsoft.Maui.MauiUIApplicationDelegate { void Run() { ${expression}.Title = url; } }`),
      ['X27_APP_WINDOW']);
  });
}

for (const selection of [
  '.First()', '.FirstOrDefault()', '.Single()', '.SingleOrDefault()', '.ElementAt(1)',
  '.ElementAtOrDefault(0)', '.Take(1)', '.ToArray<UIOpenUrlContext>()[0]', '.ToArray<UIOpenUrlContext>()[^1]',
  '!.ToArray<UIOpenUrlContext>().FirstOrDefault()', '.ToArray<UIOpenUrlContext>()!.FirstOrDefault()',
  '?.FirstOrDefault()', '.ToArray<UIOpenUrlContext>()?[0]',
  '.ToArray<UIOpenUrlContext>()\n .FirstOrDefault()',
]) {
  test(`reports single cold-URL selection: ${selection}`, () => {
    assert.deepEqual(rules(`var first = connectionOptions.UrlContexts${selection};`), ['X27_SINGLE_URL']);
  });
}

test('reports both independent risks instead of returning after the first match', () => {
  assert.deepEqual(rules(`class AppDelegate : MauiUIApplicationDelegate {
    void Run() { Window.Root.Title = options.UrlContexts.First().Url; }
  }`), ['X27_APP_WINDOW', 'X27_SINGLE_URL']);
});

test('does not flag scene-owned deferred work processing every cold URL', () => {
  assert.deepEqual(rules(`class ExistingSceneDelegate : MauiUISceneDelegate {
    readonly Queue<Action<UIWindow>> pending = new();
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options) {
      foreach (var context in options.UrlContexts.ToArray<UIOpenUrlContext>()) {
        var url = context.Url;
        pending.Enqueue(window => {
          if (window.RootViewController is UIViewController rootViewController)
            rootViewController.Title = url.AbsoluteString;
        });
      }
      base.WillConnect(scene, session, options);
    }
    public override void OnActivated(UIScene scene) {
      base.OnActivated(scene);
      if (Window is not UIWindow window) return;
      while (pending.Count > 0) pending.Dequeue()(window);
    }
  }`), []);
});

test('reports expression-bodied cold work that dereferences a missing root controller', () => {
  const findings = auditSource(`foreach (var context in connectionOptions.UrlContexts?.ToArray<UIOpenUrlContext>() ?? [])
    pending.Enqueue(window => window.RootViewController.Title = context.Url.AbsoluteString);`);

  assert.equal(findings.length, 1);
  assert.equal(findings[0].rule, 'X27_UNGUARDED_ROOT');
});

test('reports an unguarded cold-launch helper dereference', () => {
  const findings = auditSource(`static void SetColdLaunchTitle(UIWindow window, NSUrl url)
  {
    window.RootViewController.Title = url.AbsoluteString;
  }`);

  assert.equal(findings.length, 1);
  assert.equal(findings[0].rule, 'X27_UNGUARDED_ROOT');
});

test('does not report guarded root-controller cold work', () => {
  const findings = auditSource(`pending.Enqueue(window =>
  {
    if (window.RootViewController is UIViewController rootViewController)
      rootViewController.Title = url.AbsoluteString;
  });`);

  assert.deepEqual(findings, []);
});

test('does not report a direct assignment protected by an explicit root null guard', () => {
  const findings = auditSource(`if (window.RootViewController is not null)
    window.RootViewController.Title = url.AbsoluteString;`);

  assert.deepEqual(findings, []);
});

test('keeps window ownership local to a nested scene class', () => {
  assert.deepEqual(rules(`class AppDelegate : MauiUIApplicationDelegate {
    class SceneDelegate : MauiUISceneDelegate { void Run() { Window.Root.Title = title; } }
  }`), []);
});

test('does not mistake another object window or unrelated single-item selection for these patterns', () => {
  assert.deepEqual(rules(`class AppDelegate : MauiUIApplicationDelegate {
    void Run() {
      other.Window.Root.Title = title;
      other . Window.Root.Title = title;
      scene?.Window.Root.Title = title;
      var item = orders.FirstOrDefault();
      var selected = options.UrlContexts.Select(context => context.Options.First());
      var copy = options.UrlContexts.ToArray<UIOpenUrlContext>()[..];
    }
  }`), []);
});

test('reports an obsolete app activity registration retained beside a scene override', t => {
  const app = fixture(t);
  app.write('MauiProgram.cs', `builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
    .ContinueUserActivity((application, activity, restorationHandler) => false)));`);
  app.write('Platforms/iOS/SceneDelegate.cs', `class SceneDelegate : MauiUISceneDelegate {
    public override bool ContinueUserActivity(UIScene scene, NSUserActivity activity) {
      return base.ContinueUserActivity(scene, activity);
    }
  }`);

  const report = auditDirectory(app.root);

  assert.equal(report.findings.length, 1);
  const [finding] = report.findings;
  assert.equal(finding.rule, 'X27_DUPLICATE_ACTIVITY');
  assert.equal(finding.file, 'MauiProgram.cs');
  assert.equal(finding.line, 2);
});

test('does not report one scene activity path without the obsolete app registration', t => {
  const app = fixture(t);
  app.write('MauiProgram.cs', `builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
    .SceneContinueUserActivity((scene, activity) => false)));`);

  assert.deepEqual(auditDirectory(app.root).findings, []);
});

test('reports an obsolete app activity registration beside a scene lifecycle registration', t => {
  const app = fixture(t);
  app.write('MauiProgram.cs', `builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
    .ContinueUserActivity((application, activity, restorationHandler) => false)
    .SceneContinueUserActivity((scene, activity) => false)));`);

  const report = auditDirectory(app.root);

  assert.equal(report.findings.length, 1);
  assert.equal(report.findings[0].rule, 'X27_DUPLICATE_ACTIVITY');
});

test('ignores duplicate activity patterns that occur only in comments and strings', t => {
  const app = fixture(t);
  app.write('MauiProgram.cs', `// .ContinueUserActivity((application, activity, restorationHandler) => false)
var text = ".SceneContinueUserActivity((scene, activity) => false)";
var raw = """public override bool ContinueUserActivity(UIScene scene, NSUserActivity activity)""";`);

  assert.deepEqual(auditDirectory(app.root).findings, []);
});

test('ignores comments and regular, verbatim, raw and interpolated literal text', () => {
  assert.deepEqual(rules(`class AppDelegate : MauiUIApplicationDelegate {
    // Window.Root.Title = options.UrlContexts.First().Url;
    /* this.Window.Root.Title = url; */
    string a = "Window.Root";
    string b = @"options.UrlContexts.First() ""Window.Root""";
    string c = """ Window.Root.Title = options.UrlContexts.First().Url; """;
    string d = $"Window.Root.Title = options.UrlContexts.First().Url";
    char quote = '"';
  }`), []);
});

test('a preview selection remains an advisory finding, not proof that the full loop loses URLs', () => {
  const [finding] = auditSource(`
    var preview = options.UrlContexts.FirstOrDefault();
    foreach (var context in options.UrlContexts) Handle(context);
  `, 'Scene.cs');
  assert.equal(finding.severity, 'warning');
  assert.match(finding.message, /not itself proof/);
});

test('the CLI scans both heads without changing files or including generated output', t => {
  const app = fixture(t);
  const ios = app.write('Platforms/iOS/AppDelegate.cs', 'class AppDelegate { void Run() { Window.Root.Title = url; } }');
  const catalyst = app.write('Platforms/MacCatalyst/Scene.cs', 'var first = options.UrlContexts.ToArray<UIOpenUrlContext>().FirstOrDefault();');
  const generated = app.write('obj/Generated.cs', 'class AppDelegate { void Run() { Window.Root.Title = url; } }');
  const files = [ios, catalyst, generated];
  const before = files.map(file => [readFileSync(file), statSync(file).mtimeMs]);
  const result = spawnSync(process.execPath, [script, app.root, '--json'], { encoding: 'utf8' });
  assert.equal(result.status, 1, result.stderr);
  const report = JSON.parse(result.stdout);
  assert.equal(report.status, 'review-required');
  assert.equal(report.filesScanned, 2);
  assert.deepEqual(report.findings.map(finding => finding.rule).sort(), ['X27_APP_WINDOW', 'X27_SINGLE_URL']);
  assert.equal(report.errors.length, 0);
  assert.deepEqual(files.map(file => [readFileSync(file), statSync(file).mtimeMs]), before);
});

test('no findings is explicitly not a compatibility certification', t => {
  const app = fixture(t);
  app.write('Scene.cs', 'class Scene : MauiUISceneDelegate { void Run() { Window.Root.Title = url; } }');
  const result = spawnSync(process.execPath, [script, app.root, '--json'], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const report = JSON.parse(result.stdout);
  assert.equal(report.status, 'no-patterns-found');
  assert.match(report.limitations, /not C# semantic analysis or migration certification/);
});

test('a standard iOS-only app gets simple-migration and missing scene configuration guidance', t => {
  const app = fixture(t);
  app.write('MyApp.csproj', `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
    <TargetFrameworks>net11.0-android;net11.0-ios</TargetFrameworks>
    <UseMaui>true</UseMaui>
  </PropertyGroup></Project>`);
  app.write('MauiProgram.cs', `class MauiProgram {
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
  }`);
  app.write('Platforms/iOS/AppDelegate.cs', `class AppDelegate : MauiUIApplicationDelegate {
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
  }`);
  app.write('Platforms/iOS/Info.plist', '<plist><dict><!-- <key>UIApplicationSceneManifest</key> --></dict></plist>');
  const result = spawnSync(process.execPath, [script, app.root], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /No lifecycle code found: simple migration/);
  assert.match(result.stdout, /iOS: SceneDelegate.cs not found/);
  assert.match(result.stdout, /UIApplicationSceneManifest not found/);
  assert.doesNotMatch(result.stdout, /MacCatalyst/);
  app.write('Platforms/iOS/SceneDelegate.cs', '[Register("SceneDelegate")] class SceneDelegate : MauiUISceneDelegate {}');
  app.write('Platforms/iOS/Info.plist', '<plist><dict><key>UIApplicationSceneManifest</key><dict/></dict></plist>');
  const migrated = spawnSync(process.execPath, [script, app.root], { encoding: 'utf8' });
  assert.equal(migrated.status, 0, migrated.stderr);
  assert.match(migrated.stdout, /No lifecycle code found: simple migration/);
  assert.match(migrated.stdout, /iOS: SceneDelegate.cs present/);
  assert.match(migrated.stdout, /UIApplicationSceneManifest present \(source only\)/);
  assert.doesNotMatch(migrated.stdout, /MacCatalyst/);
});

test('the audit reports source scene configuration for both discovered Apple heads', t => {
  const app = fixture(t);
  for (const platform of ['iOS', 'MacCatalyst']) {
    app.write(`Platforms/${platform}/SceneDelegate.cs`, 'class SceneDelegate : MauiUISceneDelegate {}');
    app.write(`Platforms/${platform}/Info.plist`, '<plist><dict><key>UIApplicationSceneManifest</key><dict/></dict></plist>');
  }
  const report = auditDirectory(app.root);
  assert.equal(report.lifecycle.status, 'simple-migration-candidate');
  assert.equal(report.appleHeads.length, 2);
  for (const head of report.appleHeads) {
    assert.deepEqual(head.sceneDelegateFiles, [`Platforms/${head.platform}/SceneDelegate.cs`]);
    assert.deepEqual(head.manifests, [{ file: `Platforms/${head.platform}/Info.plist`, sceneManifestPresent: true }]);
  }
  assert.equal(report.status, 'no-patterns-found');
});

for (const source of [
  'builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios.OnActivated(app => Log())));',
  'class AppDelegate : MauiUIApplicationDelegate { public override bool FinishedLaunching() => true; }',
  'class AppDelegate : MauiUIApplicationDelegate { public override void OnActivated(UIApplication app) {} }',
  'class Startup : Microsoft.Maui.MauiUIApplicationDelegate { public override bool FinishedLaunching() => true; }',
  'class SceneDelegate : MauiUISceneDelegate { public override void OnActivated(UIScene scene) {} }',
  'void Handle() { OpenUrl(url); ContinueUserActivity(activity); PerformActionForShortcutItem(item); }',
]) {
  test(`custom lifecycle code prevents simple-migration guidance: ${source}`, t => {
    const app = fixture(t);
    app.write('Custom.cs', source);
    const report = auditDirectory(app.root);
    assert.equal(report.lifecycle.status, 'review-required');
    assert.deepEqual(report.lifecycle.files, ['Custom.cs']);
  });
}

test('commented callbacks do not prevent the simple migration candidate', t => {
  const app = fixture(t);
  app.write('App.cs', '// ConfigureLifecycleEvents(events); override void OnActivated() {}\nclass App {}');
  assert.equal(auditDirectory(app.root).lifecycle.status, 'simple-migration-candidate');
});

test('invalid plist encoding makes the source configuration scan incomplete', t => {
  const app = fixture(t);
  app.write('App.cs', 'class App {}');
  app.write('Platforms/iOS/Info.plist', Buffer.from([0xff, 0xff, 0xff]));
  const report = auditDirectory(app.root);
  assert.equal(report.status, 'incomplete');
  assert.equal(report.lifecycle.status, 'incomplete');
  assert.doesNotMatch(report.lifecycle.message, /No lifecycle code found/);
  assert.ok(report.errors.some(error => error.file === 'Platforms/iOS/Info.plist'));
});

test('reads UTF-16 source without changing its encoding', t => {
  const app = fixture(t);
  const bytes = Buffer.from('\ufeffclass AppDelegate { void Run() { Window.Root.Title = url; } }', 'utf16le');
  const file = app.write('AppDelegate.cs', bytes);
  assert.equal(auditDirectory(app.root).findings[0].rule, 'X27_APP_WINDOW');
  assert.deepEqual(readFileSync(file), bytes);
});

test('fails explicitly for unreadable encoding instead of reporting no patterns', t => {
  const app = fixture(t);
  app.write('Broken.cs', Buffer.from([0xff, 0xff, 0xff]));
  const report = auditDirectory(app.root);
  assert.equal(report.status, 'incomplete');
  assert.ok(report.errors.some(error => error.file === 'Broken.cs'));
});

test('does not follow source directory links outside the selected root', t => {
  const app = fixture(t);
  app.write('source/Scene.cs', 'class Scene : MauiUISceneDelegate {}');
  app.write('outside/AppDelegate.cs', 'class AppDelegate { void Run() { Window.Root.Title = url; } }');
  symlinkSync(join(app.root, 'outside'), join(app.root, 'source', 'linked'), 'junction');
  const report = auditDirectory(join(app.root, 'source'));
  assert.equal(report.status, 'incomplete');
  assert.equal(report.filesScanned, 1);
  assert.equal(report.findings.length, 0);
  assert.match(report.errors[0].message, /not followed/);
});

test('CLI usage errors and empty source directories are not successful audits', t => {
  const app = fixture(t);
  for (const argumentsList of [[], [app.root, '--unknown'], [app.root, '--json'], [join(app.root, 'missing'), '--json']]) {
    const result = spawnSync(process.execPath, [script, ...argumentsList], { encoding: 'utf8' });
    assert.equal(result.status, 2);
    assert.ok(result.stderr || JSON.parse(result.stdout).errors.length);
  }
});

for (const nodeOptions of [[], ['--preserve-symlinks-main']]) {
  test(`CLI executes through linked installation ancestors (${nodeOptions.join(' ') || 'default'})`, t => {
    const app = fixture(t);
    const source = join(app.root, 'source');
    app.write('source/AppDelegate.cs', 'class AppDelegate { void Run() { Window.Root.Title = url; } }');
    mkdirSync(join(app.root, 'installed'));
    copyFileSync(script, join(app.root, 'installed', 'audit.mjs'));
    symlinkSync(join(app.root, 'installed'), join(app.root, 'linked'), 'junction');
    const result = spawnSync(process.execPath, [...nodeOptions, join(app.root, 'linked', 'audit.mjs'), source, '--json'], { encoding: 'utf8' });
    assert.equal(result.status, 1, result.stderr);
    const report = JSON.parse(result.stdout);
    assert.equal(report.filesScanned, 1);
    assert.equal(report.findings[0].rule, 'X27_APP_WINDOW');
  });
}

test('importing the library with an absent entry path does not execute its CLI', t => {
  const app = fixture(t);
  const code = `process.argv[1]=${JSON.stringify(join(app.root, 'missing.mjs'))}; await import(${JSON.stringify(pathToFileURL(script).href)});`;
  const result = spawnSync(process.execPath, ['--input-type=module', '--eval', code], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, '');
});
