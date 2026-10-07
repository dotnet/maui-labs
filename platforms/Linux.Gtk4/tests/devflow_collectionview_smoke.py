import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


parser = argparse.ArgumentParser()
parser.add_argument("--sample", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
sample = args.sample.resolve(strict=True)
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
lease = "gtk-collectionview-" + uuid.uuid4().hex
base = "http://127.0.0.1:9223/api/v1/"


class DevFlowHttpError(RuntimeError):
    """An HTTP error response from the DevFlow agent.

    Exposes the parsed ``reason`` field so callers can distinguish genuine
    failures from the protocol's documented transient conditions
    ("stale-capture-epoch", "ui-mutation-busy"), which the real AgentClient
    (see Microsoft.Maui.DevFlow.Client/AgentClient.cs) marks Retryable=true
    and expects callers to re-capture and retry rather than treat as fatal.
    """

    def __init__(self, status, payload):
        super().__init__(payload)
        self.status = status
        try:
            self.body = json.loads(payload)
        except json.JSONDecodeError:
            self.body = None

    @property
    def reason(self):
        return self.body.get("reason") if isinstance(self.body, dict) else None


ACTION_RETRYABLE_REASONS = {"stale-capture-epoch", "ui-mutation-busy"}
# Read endpoints (ui/elements, ui/tree, hit-test, ...) report a disjoint pair of transient
# reasons for the same underlying race — see AgentClient.GetStringWithRetryableUiReadAsync in
# Microsoft.Maui.DevFlow.Client/AgentClient.cs, which retries exactly these for up to 2s before
# surfacing an error to the caller.
READ_RETRYABLE_REASONS = {"capture-changed-during-read", "native-probe-busy"}


def request(route, body=None, method=None, raw=False):
    req = urllib.request.Request(
        base + route,
        data=json.dumps(body).encode() if body is not None else None,
        method=method,
        headers={"Content-Type": "application/json", "X-DevFlow-Lease": lease},
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            data = response.read()
            return data if raw else json.loads(data)
    except urllib.error.HTTPError as error:
        raise DevFlowHttpError(error.code, error.read().decode()) from error


def with_retry(perform, retryable_reasons, description):
    """Run ``perform()``, retrying on the protocol's documented transient
    responses named in ``retryable_reasons``.

    A background layout/measure pass (frame-clock driven — the same class of
    race already fixed in the native xUnit tests) can legitimately invalidate
    a capture between a query/read and a dependent request that consumes it.
    The real AgentClient marks exactly these reasons Retryable=true (see
    Microsoft.Maui.DevFlow.Client/AgentClient.cs) and documents that callers
    should re-capture/re-read and retry rather than fail outright.
    """
    deadline = time.monotonic() + 20
    while True:
        try:
            return perform()
        except DevFlowHttpError as error:
            if error.reason not in retryable_reasons or time.monotonic() >= deadline:
                raise
            time.sleep(0.1)


def with_capture_retry(perform, description):
    """Run ``perform()`` (which re-queries a fresh captureEpoch and acts on
    it), retrying on the action endpoints' documented transient reasons."""
    return with_retry(perform, ACTION_RETRYABLE_REASONS, description)


def query(**filters):
    def read():
        return request("ui/elements?" + urllib.parse.urlencode(filters, quote_via=urllib.parse.quote))
    return with_retry(read, READ_RETRYABLE_REASONS, "query " + repr(filters))


def save(name, value):
    (output / name).write_text(json.dumps(value, indent=2), encoding="utf-8")


def wait_for(action, description):
    deadline = time.monotonic() + 20
    while True:
        result = action()
        if result:
            return result
        if time.monotonic() >= deadline:
            raise AssertionError("Timed out waiting for " + description)
        time.sleep(0.1)


with (output / "app.log").open("w", encoding="utf-8") as log:
    app = subprocess.Popen(
        ["dotnet", str(sample)],
        cwd=sample.parent,
        env={**os.environ, "GSK_RENDERER": "cairo", "MAUI_SAMPLE_COLLECTIONVIEW": "1"},
        stdout=log,
        stderr=subprocess.STDOUT,
    )
    try:
        deadline = time.monotonic() + 45
        while True:
            if app.poll() is not None:
                raise RuntimeError("GTK sample exited; inspect app.log")
            try:
                status = request("agent/status")
                break
            except urllib.error.URLError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.2)
        assert status["app"]["processId"] == app.pid, "Refusing to mutate another process"
        assert status["app"]["name"] == "Linux.Gtk4.Sample", status
        save("identity.json", status)
        granted = request("agent/lease", {
            "action": "claim", "leaseId": lease, "holderKind": "test",
            "label": "GTK CollectionView CI",
        })
        assert granted["allowed"], granted

        def set_selected_index():
            picker = wait_for(lambda: query(type="Picker"), "example picker")[0]
            return request("ui/elements/" + picker["id"] + "/properties/SelectedIndex",
                           {"value": "3", "captureEpoch": picker["captureEpoch"]}, "PUT")

        selected = with_capture_retry(set_selected_index, "set SelectedIndex")
        assert selected["property"] == "SelectedIndex" and selected["value"] == "3", selected

        for name, filters in [
            ("Alice Johnson", {"text": "Alice Johnson"}),
            ("Bob Smith", {"automationId": "Bob Smith"}),
        ]:
            def tap_named_card(name=name, filters=filters):
                cards = wait_for(lambda: query(**filters), name + " realization")
                card = next(card for card in cards if card["text"] == name)
                tapped = request("ui/actions/tap",
                                 {"elementId": card["id"], "captureEpoch": card["captureEpoch"]})
                return cards, tapped

            cards, tapped = with_capture_retry(tap_named_card, name + " tap")
            save(name + "-query.json", cards)
            assert tapped["success"], tapped
            result = wait_for(lambda: query(text="Tapped: " + name), name + " command outcome")
            assert any(item["text"] == "Tapped: " + name for item in result), result
            save(name + "-tap.json", result)

        save("tree.json", with_retry(
            lambda: request("ui/tree?depth=20"), READ_RETRYABLE_REASONS, "final tree capture"))
        deadline = time.monotonic() + 10
        while True:
            try:
                (output / "screenshot.png").write_bytes(request("ui/screenshot", raw=True))
                break
            except RuntimeError as error:
                if "Failed to capture screenshot" not in str(error) or time.monotonic() >= deadline:
                    raise
                time.sleep(0.1)
        print("PASS: owned GTK sample; Alice text and Bob AutomationId taps changed status.")
    finally:
        if app.poll() is None:
            app.terminate()
            try:
                app.wait(timeout=10)
            except subprocess.TimeoutExpired:
                app.kill()
                app.wait(timeout=5)
