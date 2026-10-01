#!/usr/bin/env python3
"""Real Copilot CLI evaluations; stdlib only, artifacts outside the checkout."""

import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
SKILL = REPO / "plugins/dotnet-maui/skills/maui-xcode27-migration"
NAME = SKILL.name


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n")


def fixture(root, kind):
    root.mkdir(parents=True)
    major = "9" if kind == "net9" else "11" if kind == "net11" else "10"
    version = "11.0.100-rc.1.26458.5" if kind == "net11" else f"{major}.0.100"
    tfms = f"net{major}.0-android" if kind == "android" else f"net{major}.0-ios;net{major}.0-maccatalyst"
    ios, catalyst = ("16.0", "18.0") if kind == "central" else ("14.0", "15.0")
    central = kind == "central"
    pin = "" if central else f"<MauiVersion>{version}</MauiVersion>"
    reference = '<PackageReference Include="Microsoft.Maui.Controls" />' if central else '<PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />'
    (root / "MyApp.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>{tfms}</TargetFrameworks>
    <OutputType>Exe</OutputType><RootNamespace>MigrationFixture</RootNamespace>
    <UseMaui>true</UseMaui><SingleProject>true</SingleProject>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <ApplicationTitle>Migration Fixture</ApplicationTitle>
    <ApplicationId>com.example.migrationfixture</ApplicationId>
    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion><ApplicationVersion>1</ApplicationVersion>
    {pin}
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">{ios}</SupportedOSPlatformVersion>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">{catalyst}</SupportedOSPlatformVersion>
  </PropertyGroup>
  <ItemGroup>{reference}</ItemGroup>
</Project>
""")
    # A pinned SDK makes this a reproducible pre-migration app, not latest-template output.
    sdk = "9.0.300" if kind == "net9" else "11.0.100-rc.1.26458.6" if kind == "net11" else "10.0.400"
    write_json(root / "global.json", {"sdk": {"version": sdk, "rollForward": "disable"}})
    workflow = root / ".github/workflows/ci.yml"
    workflow.parent.mkdir(parents=True)
    workflow.write_text(f"""name: Existing app CI
on: [push]
jobs:
  build:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: {sdk}
""")
    if central:
        (root / "Directory.Packages.props").write_text(f"""<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><PackageVersion Include="Microsoft.Maui.Controls" Version="{version}" /></ItemGroup>
</Project>
""")
    (root / "App.cs").write_text("""using Microsoft.Maui;
using Microsoft.Maui.Controls;
namespace MigrationFixture;
public class App : Application
{
    protected override Window CreateWindow(IActivationState? state) =>
        new(new ContentPage { Content = new Label { Text = "Scene migration fixture" } });
}
""")
    (root / "MauiProgram.cs").write_text("""using Microsoft.Maui.Hosting;
namespace MigrationFixture;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}
""")
    if kind == "android":
        return
    for platform in ("iOS", "MacCatalyst"):
        directory = root / "Platforms" / platform
        directory.mkdir(parents=True)
        info = {
            "UIDeviceFamily": [1, 2] if platform == "iOS" else [2],
            "UISupportedInterfaceOrientations": ["UIInterfaceOrientationPortrait"],
            "CFBundleURLTypes": [{"CFBundleURLSchemes": ["migrationfixture"]}],
            "NSCameraUsageDescription": "Existing camera purpose must remain.",
        }
        if kind == "custom":
            info["UIApplicationSceneManifest"] = {
                "UIApplicationSupportsMultipleScenes": True,
                "UISceneConfigurations": {"UIWindowSceneSessionRoleApplication": [{
                    "UISceneConfigurationName": "__MAUI_DEFAULT_SCENE_CONFIGURATION__",
                    "UISceneDelegateClassName": "ExistingSceneDelegate",
                }]},
            }
            (directory / "SceneDelegate.cs").write_text("""using Foundation;
using Microsoft.Maui;
using UIKit;
namespace MigrationFixture;
[Register("ExistingSceneDelegate")]
public class ExistingSceneDelegate : MauiUISceneDelegate
{
    public override void OnActivated(UIScene scene)
    {
        base.OnActivated(scene);
        System.Diagnostics.Debug.WriteLine("preserve-scene-activation");
    }
}
""")
        (directory / "Info.plist").write_bytes(plistlib.dumps(info, sort_keys=False))
        custom = """
    public override void OnActivated(UIApplication application)
    {
        base.OnActivated(application);
        System.Diagnostics.Debug.WriteLine("preserve-app-activation");
    }
    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        System.Diagnostics.Debug.WriteLine("preserve-warm-link:" + url.AbsoluteString);
        return base.OpenUrl(app, url, options);
    }
    public override bool FinishedLaunching(UIApplication app, NSDictionary options)
    {
        var result = base.FinishedLaunching(app, options);
        if (options?[UIApplication.LaunchOptionsUrlKey] is NSUrl url)
        {
            System.Diagnostics.Debug.WriteLine("preserve-cold-link:" + url.AbsoluteString);
            Window!.RootViewController!.Title = url.AbsoluteString;
        }
        return result;
    }
    public override void PerformActionForShortcutItem(UIApplication app, UIApplicationShortcutItem shortcut, UIOperationHandler completion)
    {
        System.Diagnostics.Debug.WriteLine("preserve-custom-shortcut:" + shortcut.Type);
        completion(shortcut.Type == "fixture.handled");
    }
""" if kind == "custom" else ""
        (directory / "AppDelegate.cs").write_text(f"""using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using UIKit;
namespace MigrationFixture;
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
{custom}}}
""")
        (directory / "Program.cs").write_text("""using UIKit;
namespace MigrationFixture;
public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
""")
    if kind == "custom":
        (root / "MauiProgram.cs").write_text("""using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
namespace MigrationFixture;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
#if IOS || MACCATALYST
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
            .PerformActionForShortcutItem((app, shortcut, completion) =>
                System.Diagnostics.Debug.WriteLine("preserve-observer:" + shortcut.Type))
            .ContinueUserActivity((app, activity, restore) =>
            {
                System.Diagnostics.Debug.WriteLine("preserve-user-activity:" + activity.ActivityType);
                return false;
            })));
#endif
        return builder.Build();
    }
}
""")


def snapshot(root, include_build_outputs=False):
    excluded = (".git",) if include_build_outputs else (".git", "bin", "obj")
    return {
        str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in root.rglob("*") if p.is_file()
        and not any(part in excluded for part in p.relative_to(root).parts)
    }


def events(path):
    result = []
    for line in path.read_text().splitlines():
        try:
            value = json.loads(line)
            if isinstance(value, dict):
                result.append(value)
        except json.JSONDecodeError:
            continue
    return result


def invoked(trace):
    succeeded = {
        e.get("data", {}).get("toolCallId") for e in trace
        if e.get("type") == "tool.execution_complete" and e.get("data", {}).get("success") is True
    }
    return [
        e for e in trace if e.get("type") == "tool.execution_start"
        and e.get("data", {}).get("toolName") == "skill"
        and NAME in json.dumps(e.get("data", {}).get("arguments", {}))
        and e.get("data", {}).get("toolCallId") in succeeded
    ]


def agent(root, run, prompt, trigger=False, timeout=300):
    env = os.environ.copy()
    env["COPILOT_HOME"] = str(run / "copilot-home")
    command = [
        "copilot", "-C", str(root), "--no-auto-update", "--no-remote-export",
        "--disable-builtin-mcps", "--no-custom-instructions", "--output-format", "json",
        "--allow-all-tools", "--log-dir", str(run / "logs"),
    ]
    if trigger:
        # Measures real skill invocation, not a predicted yes/no or a keyword heuristic.
        command += ["--available-tools", "skill,view,glob,rg", "-p", prompt]
    else:
        command += ["-p", prompt + "\nWork only in this fixture directory. Do not commit, install tools, change global settings, or use a simulator. Do not access other projects. Honor audit-only/no-edit requests; otherwise implement requested edits rather than merely suggesting them. Report build limitations honestly."]
    write_json(run / "command.json", command)
    started = time.monotonic()
    with (run / "transcript.jsonl").open("w") as log:
        try:
            process = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGTERM)
            process.wait()
            code = 124
    trace = events(run / "transcript.jsonl")
    write_json(run / "timing.json", {"total_duration_seconds": time.monotonic() - started})
    responses = [e["data"].get("content", "") for e in trace if e.get("type") == "assistant.message"]
    (run / "response.md").write_text("\n\n".join(responses))
    write_json(run / "execution.json", {
        "exit_code": code, "skill_invocations": invoked(trace),
        "installed_skill_names": sorted(p.name for p in (root / ".github/skills").glob("*") if p.is_dir()),
        "candidate_skill_hashes": snapshot(root / ".github/skills" / NAME) if (root / ".github/skills" / NAME).is_dir() else {},
        "models": sorted({e.get("data", {}).get("model") for e in trace if e.get("type") == "session.tools_updated" and e.get("data", {}).get("model")}),
        "result": next((e for e in reversed(trace) if e.get("type") == "result"), None),
    })
    return code, bool(invoked(trace))


def install_skill(root, competing=False):
    destination = root / ".github/skills" / NAME
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(SKILL, destination)
    if competing:
        for skill in SKILL.parent.iterdir():
            if skill.is_dir() and skill != SKILL:
                shutil.copytree(skill, destination.parent / skill.name)


def grade(root, kind, before, audit=False):
    checks = []

    def check(text, passed, evidence):
        checks.append({"text": text, "passed": bool(passed), "evidence": evidence})

    after = snapshot(root, include_build_outputs=audit)
    if audit:
        changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
        check("Audit leaves project files unchanged", not changed, f"Changed paths: {changed}")
        return checks
    tree = ET.parse(root / "MyApp.csproj")
    tfms = tree.findtext(".//TargetFrameworks", "")
    check("Preserves .NET 10 Apple targets", tfms == "net10.0-ios;net10.0-maccatalyst", tfms)
    for platform, identifier, minimum in (("iOS", "ios", "16.0" if kind == "central" else "15.0"),
                                           ("MacCatalyst", "maccatalyst", "18.0" if kind == "central" else "17.0")):
        directory = root / "Platforms" / platform
        info = plistlib.loads((directory / "Info.plist").read_bytes())
        manifest = info.get("UIApplicationSceneManifest", {})
        configs = manifest.get("UISceneConfigurations", {}).get("UIWindowSceneSessionRoleApplication", [])
        custom = kind == "custom"
        expected = "ExistingSceneDelegate" if custom else "SceneDelegate"
        config_name = "__MAUI_DEFAULT_SCENE_CONFIGURATION__"
        check(f"{platform}: correct manifest", len(configs) == 1 and configs[0].get("UISceneDelegateClassName") == expected and configs[0].get("UISceneConfigurationName") == config_name and manifest.get("UIApplicationSupportsMultipleScenes") is custom, json.dumps(manifest))
        all_source = "\n".join(p.read_text() for p in directory.glob("*.cs"))
        check(f"{platform}: registered MAUI scene delegate", f'[Register("{expected}")]' in all_source and "MauiUISceneDelegate" in all_source, all_source)
        check(f"{platform}: preserves AppDelegate", "MauiUIApplicationDelegate" in (directory / "AppDelegate.cs").read_text(), (directory / "AppDelegate.cs").read_text())
        check(f"{platform}: preserves unrelated plist values", info.get("NSCameraUsageDescription") == "Existing camera purpose must remain." and info.get("CFBundleURLTypes") == [{"CFBundleURLSchemes": ["migrationfixture"]}], json.dumps(info))
        values = [n.text for n in tree.findall(".//SupportedOSPlatformVersion") if f"'{identifier}'" in n.get("Condition", "")]
        check(f"{platform}: exact inline minimum", values == [minimum], repr(values))
        # plistlib silently accepts duplicate keys; check the XML representation too.
        xml = ET.parse(directory / "Info.plist")
        keys = [n.text for n in xml.findall("./dict/key")]
        check(f"{platform}: no duplicate root keys", len(keys) == len(set(keys)), repr(keys))
    if kind == "central":
        owner = ET.parse(root / "Directory.Packages.props")
        version = next(n.get("Version", "") for n in owner.findall(".//PackageVersion") if n.get("Include") == "Microsoft.Maui.Controls")
        check("No ineffective project MauiVersion pin", tree.find(".//MauiVersion") is None, (root / "MyApp.csproj").read_text())
    else:
        version = tree.findtext(".//MauiVersion", "")
    parts = version.split(".")
    check("Compatible .NET 10 MAUI servicing version", len(parts) == 3 and parts[0] == "10" and parts[1] == "0" and parts[2].isdigit() and int(parts[2]) >= 110, version)
    if kind == "custom":
        source = "\n".join(p.read_text() for p in root.rglob("*.cs") if not any(part in ("obj", "bin", ".github") for part in p.parts))
        app_delegates = "\n".join(p.read_text() for p in root.glob("Platforms/*/AppDelegate.cs"))
        scene_sources = "\n".join(p.read_text() for p in root.glob("Platforms/*/SceneDelegate.cs"))
        shared_source = (root / "MauiProgram.cs").read_text()
        check("Custom activation has a scene delivery path",
              "preserve-app-activation" in scene_sources or
              ("SceneOnActivated" in shared_source and "preserve-app-activation" in shared_source),
              scene_sources + shared_source)
        check("Custom quick actions have moved off the application-only override",
              "preserve-custom-shortcut" in source and "preserve-custom-shortcut" not in app_delegates,
              app_delegates)
        check("No duplicate Essentials quick-action forwarding",
              "Platform.PerformActionForShortcutItem" not in source, source)
        check("Existing quick-action observer side effect is preserved",
              "preserve-observer:" in source, source)
        check("Cold user activities are inspected, not just cold URL contexts",
              "connectionOptions.UserActivities" in source, source)
        check("Cold-link behavior no longer dereferences AppDelegate.Window",
              "preserve-cold-link" in source and "Window!.RootViewController" not in app_delegates,
              app_delegates)
    return checks


def save_grade(path, checks):
    passed = sum(x["passed"] for x in checks)
    write_json(path, {"expectations": checks, "summary": {
        "passed": passed, "failed": len(checks) - passed, "total": len(checks),
        "pass_rate": passed / len(checks) if checks else 0,
    }})


def run_case(case, output, with_skill, timeout=600):
    config = "with_skill" if with_skill else "without_skill"
    run = output / case["name"] / config
    if run.exists():
        raise ValueError(f"Refusing to overwrite prior evidence: {run}")
    root = run / "outputs"
    fixture(root, case["fixture"])
    if with_skill:
        install_skill(root)
    before = snapshot(root, include_build_outputs=case["id"] >= 4)
    write_json(run / "before.json", before)
    write_json(run.parent / "eval_metadata.json", {
        "eval_id": case["id"], "eval_name": case["name"], "prompt": case["prompt"],
        "assertions": case["expectations"],
    })
    code, used = agent(root, run, case["prompt"], timeout=timeout)
    try:
        checks = grade(root, case["fixture"], before, audit=case["id"] >= 4)
    except (ET.ParseError, plistlib.InvalidFileException, OSError, ValueError) as ex:
        checks = [{"text": "Output is structurally readable", "passed": False, "evidence": str(ex)}]
    checks.insert(0, {"text": "Agent completed", "passed": code == 0, "evidence": f"exit={code}"})
    expected = with_skill and case["id"] not in (7, 9)
    checks.insert(1, {"text": "Skill activation matches scenario scope", "passed": used == expected, "evidence": f"expected={expected}, successful invocation={used}; see execution.json"})
    save_grade(run / "grading.json", checks)
    return {"case": case["name"], "configuration": config, "exit": code, "invoked": used, "passed": sum(c["passed"] for c in checks), "total": len(checks)}


def run_trigger(item, index, output):
    run = output / f"trigger-{index:02}"
    root = run / "outputs"
    root.mkdir(parents=True)
    install_skill(root, competing=True)
    write_json(run / "prompt.json", item)
    code, used = agent(root, run, item["query"], trigger=True, timeout=120)
    result = {**item, "invoked": used, "exit": code, "passed": code == 0 and used == item["should_trigger"]}
    write_json(run / "grading.json", result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["triggers", "migration", "idempotency", "prepare"])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cases", nargs="*", default=[])
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--timeout", type=int, default=600, help="Per migration run; timeouts are failures")
    parser.add_argument("--configuration", choices=["paired", "with_skill"], default="paired")
    parser.add_argument("--trigger-file", type=Path, default=HERE / "triggers.json")
    args = parser.parse_args()
    output = args.output.resolve()
    if output == REPO or REPO in output.parents:
        parser.error("Keep logs and generated projects outside the checkout (use session artifacts).")
    output.mkdir(parents=True, exist_ok=True)
    if args.mode == "prepare":
        fixture(output / "outputs", args.cases[0] if args.cases else "simple")
        return
    if args.mode == "idempotency":
        # --output identifies an existing with_skill run, not a new fixture.
        root = output / "outputs"
        before = snapshot(root, include_build_outputs=True)
        run = output / "idempotency"
        run.mkdir()
        write_json(run / "before.json", before)
        code, used = agent(root, run, "Re-audit this MAUI project for Xcode 27.0 compatibility. The existing OS minima are approved. If already migrated correctly, do not rewrite files. Do not install tools.")
        checks = grade(root, "simple", before, audit=True)
        checks += [{"text": "Agent completed and invoked skill", "passed": code == 0 and used, "evidence": f"exit={code}, invoked={used}"}]
        save_grade(run / "grading.json", checks)
        raise SystemExit(0 if all(check["passed"] for check in checks) else 1)
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        if args.mode == "triggers":
            items = json.loads(args.trigger_file.read_text())
            futures = [pool.submit(run_trigger, item, i, output) for i, item in enumerate(items)]
        else:
            cases = json.loads((HERE / "evals.json").read_text())["evals"]
            cases = [c for c in cases if not args.cases or c["name"] in args.cases]
            futures = [pool.submit(run_case, case, output, enabled, args.timeout) for case in cases for enabled in ([True, False] if args.configuration == "paired" else [True])]
        results = []
        for future in concurrent.futures.as_completed(futures):
            result = future.result()
            results.append(result)
            print(json.dumps(result), flush=True)
            write_json(output / "results.json", results)
    passed = all(result["passed"] if args.mode == "triggers"
                 else result["passed"] == result["total"] for result in results)
    raise SystemExit(0 if results and passed else 1)


if __name__ == "__main__":
    main()
