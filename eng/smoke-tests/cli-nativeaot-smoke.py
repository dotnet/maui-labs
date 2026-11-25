#!/usr/bin/env python3
"""Smoke-tests a published NativeAOT `maui` executable.

ILC cannot flag every runtime failure (for example reflection-based serialization or COM
activation), so this runs the real executable through the paths that are most likely to break:
source-generated JSON output, MCP tool listing *and* invocation, and - on Windows - the UI
Automation COM layer behind `maui devflow ui alert`.

Usage: cli-nativeaot-smoke.py <path-to-maui-executable> [--windows-uia]
"""

import json
import queue
import subprocess
import sys
import threading
import time
import uuid


def run(cli, *args, check=True):
    result = subprocess.run([cli, *args], capture_output=True, text=True, timeout=120)
    if check and result.returncode != 0:
        raise SystemExit(
            f"'{' '.join(args)}' exited with {result.returncode}\n{result.stdout}\n{result.stderr}")
    return result


def stop_broker(cli):
    """Stops the broker and waits until it is gone, so the next command starts a fresh daemon
    instead of racing the old one for its port."""
    run(cli, "devflow", "broker", "stop", check=False)
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        status = run(cli, "devflow", "broker", "status", check=False).stdout
        if '"running": true' not in status:
            return
        time.sleep(0.5)
    raise SystemExit("The DevFlow broker did not stop.")


def smoke_json_commands(cli):
    print(run(cli, "devflow", "version").stdout.strip())

    commands = json.loads(run(cli, "devflow", "commands", "--json").stdout)
    assert isinstance(commands, list) and commands, "Expected at least one command description"
    assert all("command" in c and "description" in c for c in commands), \
        "Command entries must include command and description"

    stop_broker(cli)
    agents = json.loads(run(cli, "devflow", "list", "--json").stdout)
    assert isinstance(agents, list), "Expected list --json output to be a JSON array"
    stop_broker(cli)


def smoke_mcp(cli):
    mcp = subprocess.Popen(
        [cli, "devflow", "mcp"],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, bufsize=1)
    responses = queue.Queue()

    def read_responses():
        for line in mcp.stdout:
            try:
                responses.put(json.loads(line))
            except ValueError:
                pass

    threading.Thread(target=read_responses, daemon=True).start()

    def send(message):
        mcp.stdin.write(json.dumps(message) + "\n")
        mcp.stdin.flush()

    def wait_for(message_id, timeout=30):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if mcp.poll() is not None:
                raise SystemExit(f"MCP server exited early: {mcp.stderr.read()}")
            try:
                response = responses.get(timeout=1)
            except queue.Empty:
                continue
            if response.get("id") == message_id:
                return response
        raise SystemExit(f"Timed out waiting for MCP response {message_id}")

    try:
        send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "nativeaot-smoke", "version": "1.0"}}})
        wait_for(1)
        send({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}})

        send({"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}})
        tools = wait_for(2).get("result", {}).get("tools")
        assert isinstance(tools, list) and tools, "Expected a non-empty MCP tool catalog"

        # tools/call binds arguments and serializes results, which tools/list does not exercise.
        send({"jsonrpc": "2.0", "id": 3, "method": "tools/call",
              "params": {"name": "maui_tap", "arguments": {"elementId": "smoke"}}})
        call = wait_for(3)
        assert "result" in call and call["result"].get("content"), \
            f"Expected MCP tools/call to return content, got: {call}"
        print(f"MCP: {len(tools)} tools listed, tools/call returned content")
    finally:
        mcp.terminate()
        try:
            mcp.wait(timeout=5)
        except subprocess.TimeoutExpired:
            mcp.kill()
            mcp.wait()
        # tools/call can start the DevFlow broker daemon, which would keep the executable locked.
        stop_broker(cli)


def smoke_windows_uia(cli):
    """Drives the Windows UI Automation COM layer through the NativeAOT executable."""
    title = f"DevFlowAotSmoke-{uuid.uuid4().hex[:8]}"
    button = "DevFlowAotSmokeButton"
    script = (
        "Add-Type -AssemblyName System.Windows.Forms;"
        f"$f = New-Object System.Windows.Forms.Form; $f.Text = '{title}';"
        f"$b = New-Object System.Windows.Forms.Button; $b.Text = '{button}'; $f.Controls.Add($b);"
        "[System.Windows.Forms.Application]::Run($f)")
    host = subprocess.Popen(["powershell", "-NoProfile", "-NonInteractive", "-Command", script])
    try:
        deadline = time.monotonic() + 90
        tree = ""
        last_error = ""
        while time.monotonic() < deadline:
            if host.poll() is not None:
                raise SystemExit(f"The PowerShell window host exited early with code {host.returncode}.")
            result = run(cli, "devflow", "ui", "alert", "tree", "--pid", str(host.pid), check=False)
            tree, last_error = result.stdout, result.stderr
            if title in tree and button in tree:
                break
            time.sleep(2)
        else:
            raise SystemExit(
                f"UI Automation tree never contained the test window.\nstdout:\n{tree}\nstderr:\n{last_error}")

        # Detection walks windows/buttons/texts through the same COM interfaces.
        run(cli, "devflow", "ui", "alert", "detect", "--pid", str(host.pid))
        print("Windows UI Automation: NativeAOT COM layer enumerated the test window")
    finally:
        host.kill()
        host.wait(timeout=10)


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)

    cli = sys.argv[1]
    smoke_json_commands(cli)
    smoke_mcp(cli)
    if "--windows-uia" in sys.argv[2:]:
        smoke_windows_uia(cli)


if __name__ == "__main__":
    main()
