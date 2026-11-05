#!/usr/bin/env python3
"""Read Android comparison conditions without changing device settings."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import sys


ANIMATION_SETTINGS = (
    "window_animation_scale",
    "transition_animation_scale",
    "animator_duration_scale",
)


def numeric_field(text, name):
    match = re.search(rf"^\s*{re.escape(name)}:\s*(-?\d+(?:\.\d+)?)\s*$",
                      text, re.MULTILINE)
    if match is None:
        raise ValueError(f"Missing or invalid device field: {name}")
    return float(match.group(1))


def battery_values(text):
    scale = numeric_field(text, "scale")
    level = numeric_field(text, "level")
    if scale <= 0 or not 0 <= level <= scale:
        raise ValueError("Invalid battery level or scale")
    power = {}
    for key in ("AC powered", "USB powered", "Wireless powered"):
        match = re.search(rf"^\s*{re.escape(key)}:\s*(true|false)\s*$",
                          text, re.MULTILINE)
        if match is None:
            raise ValueError(f"Missing power-source field: {key}")
        power[key] = match.group(1) == "true"
    return {
        "percent": level * 100 / scale,
        "temperature_c": numeric_field(text, "temperature") / 10,
        "status": numeric_field(text, "status"),
        "power": power,
    }


def evaluate(values, min_battery=80, max_temperature=35,
             baseline_temperature=None, temperature_tolerance=2):
    reasons = []
    battery = values["battery"]
    if battery["percent"] < min_battery:
        reasons.append(f"Battery is below {min_battery}%")
    if battery["temperature_c"] > max_temperature:
        reasons.append(f"Battery temperature exceeds {max_temperature} C")
    if baseline_temperature is not None:
        if abs(battery["temperature_c"] - baseline_temperature) > temperature_tolerance:
            reasons.append("Battery temperature is outside the accepted baseline")
    if values["thermal_status"] != 0:
        reasons.append("Android thermal status is not NONE")
    if values["battery_saver"] != 0:
        reasons.append("Battery saver is enabled")
    for name, value in values["animation_scales"].items():
        if value != 0:
            reasons.append(f"{name} differs from the approved value of 0")
    if values["android_user"] != 0:
        reasons.append("Active Android user differs from the approved user 0")
    if values["model"] != "Pixel 5" or values["api"] != 34:
        reasons.append("Device is not the approved Pixel 5 / API 34 target")
    if values["abi"] != "arm64-v8a":
        reasons.append("Device ABI is not arm64-v8a")
    return reasons


def inspect_device(adb, serial):
    def shell(*args):
        result = subprocess.run(
            [adb, "-s", serial, "shell", *args], check=True,
            capture_output=True, text=True, timeout=30,
        )
        return result.stdout.strip()

    values = {
        "schema_version": 1,
        "recorded_at_utc": datetime.now(timezone.utc).isoformat(),
        "serial": serial,
        "model": shell("getprop", "ro.product.model"),
        "fingerprint": shell("getprop", "ro.build.fingerprint"),
        "api": int(shell("getprop", "ro.build.version.sdk")),
        "abi": shell("getprop", "ro.product.cpu.abi"),
        "android_user": int(shell("am", "get-current-user")),
        "battery": battery_values(shell("dumpsys", "battery")),
        "thermal_status": numeric_field(
            shell("dumpsys", "thermalservice"), "Thermal Status"),
        "battery_saver": int(shell("settings", "get", "global", "low_power")),
        "animation_scales": {
            name: float(shell("settings", "get", "global", name))
            for name in ANIMATION_SETTINGS
        },
    }
    if not values["fingerprint"]:
        raise ValueError("Device returned no build fingerprint")
    return values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--baseline-temperature", type=float)
    args = parser.parse_args()
    if args.serial != "13041FDD4007MT":
        parser.error("This comparison is approved only for Pixel 5 serial 13041FDD4007MT")
    try:
        values = inspect_device(args.adb, args.serial)
        values["policy"] = {
            "min_battery_percent": 80,
            "max_battery_temperature_c": 35,
            "baseline_temperature_c": args.baseline_temperature,
            "baseline_tolerance_c": 2,
            "animation_scales": 0,
        }
        values["blocking_conditions"] = evaluate(
            values, baseline_temperature=args.baseline_temperature)
        values["condition_gate_passed"] = not values["blocking_conditions"]
        values["scope"] = (
            "Read-only battery/thermal/identity/settings gate. This does not "
            "verify screen unlock, application state, temperature stability "
            "over time, or absence of background interference."
        )
        text = json.dumps(values, indent=2, allow_nan=False) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            with args.output.open("x") as stream:
                stream.write(text)
        print(text, end="")
        return 0 if values["condition_gate_passed"] else 2
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"Device conditions failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
