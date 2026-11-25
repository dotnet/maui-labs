#!/usr/bin/env python3
"""Measure the edit-to-proof loop for a DevFlow-enabled MAUI app.

Launches the app with `dotnet watch` (mode "watch") or `dotnet run`
(mode "run"), waits for its DevFlow agent, then repeatedly applies a source edit
and measures how long it takes for the running UI to show the expected text.

In watch mode the edit should hot reload into the same process. In run mode
every edit is followed by a full rebuild and relaunch, which is the baseline
that watch mode is compared against. The edited file is always restored.

Example:
  python3 eng/scripts/devflow-hot-reload-probe.py \
    --project samples/DevFlow.Sample/DevFlow.Sample.csproj \
    --framework net11.0-maccatalyst --mode watch \
    --property DevFlowSampleWatchTarget=net11.0-maccatalyst \
    --file samples/DevFlow.Sample/MainPage.xaml \
    --find 'Text="📝 My Todos"' --replace 'Text="📝 My Todos (probe)"' \
    --automation-id HeaderLabel --expect "(probe)" --iterations 3
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import signal
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path


def log(message: str) -> None:
    print(f"[probe {time.strftime('%H:%M:%S')}] {message}", flush=True)


def http_json(port: int, path: str, timeout: float = 5.0):
    with urllib.request.urlopen(f"http://localhost:{port}{path}", timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def find_agents(project: Path, framework: str, exclude_pids: set[int]) -> list[dict]:
    result = subprocess.run(
        ["maui", "devflow", "list", "--json"],
        cwd=project.parent, capture_output=True, text=True, timeout=30)
    if result.returncode != 0:
        return []
    try:
        agents = json.loads(result.stdout)
    except json.JSONDecodeError:
        return []
    if isinstance(agents, dict):
        agents = agents.get("agents") or []
    matches = []
    for agent in agents:
        agent_project = agent.get("project") or ""
        if Path(agent_project).name != project.name:
            continue
        if framework and agent.get("tfm") not in (None, framework):
            continue
        if agent.get("processId") in exclude_pids or not agent.get("port"):
            continue
        matches.append(agent)
    # The broker can briefly list agents of apps that already exited; newest first.
    return sorted(matches, key=lambda a: a.get("connectedAt") or "", reverse=True)


def read_ui(port: int, automation_id: str) -> tuple[int | None, str | None]:
    status = http_json(port, "/api/v1/agent/status")
    pid = (status.get("app") or {}).get("processId")
    query = urllib.parse.urlencode({"automationId": automation_id})
    elements = http_json(port, f"/api/v1/ui/elements?{query}")
    text = elements[0].get("text") if isinstance(elements, list) and elements else None
    return pid, text


def process_cwd(pid: int) -> str | None:
    try:
        output = subprocess.run(["lsof", "-a", "-p", str(pid), "-d", "cwd", "-Fn"],
                                capture_output=True, text=True, timeout=10).stdout
    except (OSError, subprocess.TimeoutExpired):
        return None
    return next((line[1:] for line in output.splitlines() if line.startswith("n")), None)


def preflight(args) -> None:
    """Refuse to measure while another watcher or app instance for the project is alive.

    A leftover `dotnet watch` rebuilds and relaunches the app on every probe edit,
    so the probe would time a second, competing instance."""
    problems = []
    ps = subprocess.run(["ps", "-axo", "pid=,command="], capture_output=True, text=True).stdout
    for line in ps.splitlines():
        pid_text, _, command = line.strip().partition(" ")
        if "dotnet-watch" in command and process_cwd(int(pid_text)) == str(args.project.parent):
            problems.append(f"dotnet watch pid {pid_text} is already watching {args.project.parent}")
    for agent in find_agents(args.project, args.framework, set()):
        try:
            pid, _ = read_ui(int(agent["port"]), args.automation_id)
        except Exception:
            continue  # stale broker entry for an app that already exited
        problems.append(f"app pid {pid} is already running with an agent on port {agent['port']}")
    if problems:
        raise SystemExit("probe preflight failed (stop these first; dotnet watch needs SIGINT or SIGKILL):\n  "
                         + "\n  ".join(problems))


def wait_for_agent(args, launcher: subprocess.Popen, exclude_pids: set[int], deadline: float) -> tuple[int, int]:
    """Return (port, pid) once a new agent for the project answers UI queries."""
    while time.monotonic() < deadline:
        if launcher.poll() not in (None, 0):
            raise RuntimeError(f"launcher exited with code {launcher.returncode}")
        for agent in find_agents(args.project, args.framework, exclude_pids):
            port = int(agent["port"])
            # Let the CLI repair Android adb forwarding before direct HTTP use.
            subprocess.run(
                ["maui", "devflow", "agent", "status", "--agent-port", str(port), "--json"],
                cwd=args.project.parent, capture_output=True, timeout=30)
            try:
                pid, text = read_ui(port, args.automation_id)
                if pid and pid not in exclude_pids and text is not None:
                    return port, pid
            except Exception:
                pass
        time.sleep(0.5)
    raise TimeoutError("DevFlow agent did not become ready")


def wait_for_text(port: int, automation_id: str, expected: str, present: bool, deadline: float):
    """Poll the running UI until `expected` is (or is no longer) in the element text."""
    last_error = None
    while time.monotonic() < deadline:
        try:
            pid, text = read_ui(port, automation_id)
            if text is not None and (expected in text) == present:
                return pid, text
        except Exception as error:  # the agent may be briefly unreachable during a reload
            last_error = error
        time.sleep(0.2)
    raise TimeoutError(f"UI did not {'show' if present else 'drop'} {expected!r} ({last_error})")


def start_launcher(args, log_path: Path) -> subprocess.Popen:
    if args.mode == "watch":
        command = ["dotnet", "watch", "--non-interactive", "-f", args.framework]
        if args.device:
            command += ["--device", args.device]
    else:
        # `dotnet run` builds then deploys; `dotnet build -t:Run` does not rebuild on every platform.
        command = ["dotnet", "run", "-f", args.framework]
        if args.device:
            command += ["--device", args.device]
    command += [f"--property:{p}" for p in args.property]
    env = dict(os.environ, DOTNET_WATCH_SUPPRESS_EMOJIS="1")
    log(f"launch: {' '.join(command)}")
    log_file = open(log_path, "a", encoding="utf-8")
    log_file.write(f"\n===== {time.strftime('%H:%M:%S')} {' '.join(command)}\n")
    log_file.flush()
    return subprocess.Popen(
        command, cwd=args.project.parent, stdout=log_file, stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL, env=env, start_new_session=True)


def stop_launcher(process: subprocess.Popen | None) -> None:
    if process is None or process.poll() is not None:
        return
    try:
        os.killpg(process.pid, signal.SIGINT)
        process.wait(timeout=20)
    except Exception:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


def stop_app(framework: str, pid: int | None) -> None:
    # Mac Catalyst apps are started through `open` and iOS Simulator apps run as
    # host processes; neither exits when the launcher stops. Android pids are
    # device-side, so leave those to the next deploy.
    if not pid or "android" in framework:
        return
    try:
        os.kill(pid, signal.SIGTERM)
        for _ in range(50):
            time.sleep(0.1)
            os.kill(pid, 0)
        os.kill(pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


def raise_interrupt(signum, frame):
    raise KeyboardInterrupt(f"signal {signum}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--framework", required=True)
    parser.add_argument("--device", help="Device id passed to dotnet watch --device")
    parser.add_argument("--mode", choices=["watch", "run"], default="watch")
    parser.add_argument("--property", action="append", default=[], help="MSBuild property Name=Value")
    parser.add_argument("--file", type=Path, required=True, help="Source file to edit")
    parser.add_argument("--find", required=True, help="Exact text to replace in --file")
    parser.add_argument("--replace", required=True)
    parser.add_argument("--automation-id", required=True, help="Element whose text proves the edit")
    parser.add_argument("--expect", required=True, help="Text the element shows only after the edit")
    parser.add_argument("--iterations", type=int, default=3, help="Edits to apply (alternating apply/revert)")
    parser.add_argument("--launch-timeout", type=float, default=600)
    parser.add_argument("--edit-timeout", type=float, default=600)
    parser.add_argument("--output", type=Path, help="Write JSON results here")
    parser.add_argument("--log", type=Path, help="Launcher log (default: next to --output or cwd)")
    args = parser.parse_args()

    args.project = args.project.resolve()
    args.file = args.file.resolve()
    if shutil.which("maui") is None:
        parser.error("the `maui` CLI must be on PATH")
    if args.device and "android" in args.framework:
        # One phone on both USB and wireless adb looks like two devices; the CLI then
        # refuses to pick one and silently skips the agent port forward.
        os.environ["ANDROID_SERIAL"] = args.device
    # Work on bytes so line endings and encoding are preserved exactly.
    original = args.file.read_bytes()
    find = args.find.encode("utf-8")
    if original.count(find) != 1:
        parser.error(f"--find must occur exactly once in {args.file}")
    edited = original.replace(find, args.replace.encode("utf-8"))
    preflight(args)
    signal.signal(signal.SIGTERM, raise_interrupt)
    log_path = (args.log or (args.output.with_suffix(".log") if args.output else Path("hot-reload-probe.log"))).resolve()

    results: dict = {"mode": args.mode, "framework": args.framework, "device": args.device,
                     "file": str(args.file), "iterations": []}
    launcher = None
    pid = None
    started = time.monotonic()
    try:
        launcher = start_launcher(args, log_path)
        port, pid = wait_for_agent(args, launcher, set(), started + args.launch_timeout)
        seen_pids = {pid}
        results["launch_seconds"] = round(time.monotonic() - started, 2)
        _, text = read_ui(port, args.automation_id)
        if args.expect in text:
            raise RuntimeError(f"baseline already contains {args.expect!r}; is the file restored?")
        log(f"agent ready on port {port}, pid {pid}, in {results['launch_seconds']}s; baseline {text!r}")

        for index in range(args.iterations):
            apply = index % 2 == 0
            edit_started = time.monotonic()
            args.file.write_bytes(edited if apply else original)
            previous_pid = pid
            if args.mode == "run":
                stop_launcher(launcher)
                stop_app(args.framework, previous_pid)
                launcher = start_launcher(args, log_path)
                port, pid = wait_for_agent(args, launcher, seen_pids, edit_started + args.edit_timeout)
                seen_pids.add(pid)
            new_pid, text = wait_for_text(port, args.automation_id, args.expect, apply, edit_started + args.edit_timeout)
            entry = {"edit": "apply" if apply else "revert",
                     "seconds": round(time.monotonic() - edit_started, 2),
                     "same_process": new_pid == previous_pid, "text": text}
            pid = new_pid
            results["iterations"].append(entry)
            log(f"{entry['edit']}: {entry['seconds']}s, same process: {entry['same_process']}, text {text!r}")
        results["success"] = True
    except (Exception, KeyboardInterrupt) as error:
        results["success"] = False
        results["error"] = str(error)
        log(f"FAILED: {error} (see {log_path})")
    finally:
        # Stop the launcher before restoring: a watcher would treat the restore as
        # an edit and relaunch the app while shutting down.
        stop_launcher(launcher)
        stop_app(args.framework, pid)
        if args.file.read_bytes() != original:
            args.file.write_bytes(original)
        results["total_seconds"] = round(time.monotonic() - started, 2)

    seconds = [entry["seconds"] for entry in results["iterations"]]
    if seconds:
        results["median_edit_seconds"] = sorted(seconds)[len(seconds) // 2]
    output = json.dumps(results, indent=2, ensure_ascii=False)
    if args.output:
        args.output.write_text(output + "\n", encoding="utf-8")
    print(output)
    return 0 if results["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
