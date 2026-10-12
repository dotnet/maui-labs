import copy
import unittest

from device_conditions import battery_values, evaluate, numeric_field


BATTERY = """Current Battery Service state:
  AC powered: true
  USB powered: false
  Wireless powered: false
  level: 82
  scale: 100
  temperature: 310
  status: 2
"""


def accepted_values():
    return {
        "battery": battery_values(BATTERY),
        "thermal_status": 0,
        "battery_saver": 0,
        "animation_scales": {
            "window_animation_scale": 0,
            "transition_animation_scale": 0,
            "animator_duration_scale": 0,
        },
        "android_user": 0,
        "model": "Pixel 5",
        "api": 34,
        "abi": "arm64-v8a",
    }


class DeviceConditionsTests(unittest.TestCase):
    def test_battery_units_and_power_source(self):
        result = battery_values(BATTERY)
        self.assertEqual(82, result["percent"])
        self.assertEqual(31, result["temperature_c"])
        self.assertTrue(result["power"]["AC powered"])
        self.assertFalse(result["power"]["USB powered"])

    def test_missing_and_invalid_fields_fail(self):
        for text in ("", "Thermal Status: null", "Thermal Status: NaN"):
            with self.subTest(text=text), self.assertRaises(ValueError):
                numeric_field(text, "Thermal Status")
        with self.assertRaises(ValueError):
            battery_values(BATTERY.replace("scale: 100", "scale: 0"))
        with self.assertRaises(ValueError):
            battery_values(BATTERY.replace("level: 82", "level: 101"))
        with self.assertRaises(ValueError):
            battery_values(BATTERY.replace("AC powered: true", "AC powered: unknown"))

    def test_accepted_values_pass_without_mutation(self):
        values = accepted_values()
        before = copy.deepcopy(values)
        self.assertEqual([], evaluate(values, baseline_temperature=31))
        self.assertEqual(before, values)

    def test_each_condition_can_block(self):
        changes = (
            ("thermal_status", 1),
            ("battery_saver", 1),
            ("android_user", 10),
            ("model", "Pixel 8"),
            ("api", 35),
            ("abi", "x86_64"),
        )
        for key, value in changes:
            with self.subTest(key=key):
                values = accepted_values()
                values[key] = value
                self.assertTrue(evaluate(values))

    def test_battery_temperature_and_animation_block(self):
        values = accepted_values()
        values["battery"]["percent"] = 79
        values["battery"]["temperature_c"] = 36
        values["animation_scales"]["animator_duration_scale"] = 1
        self.assertEqual(3, len(evaluate(values)))

    def test_baseline_and_threshold_boundaries(self):
        values = accepted_values()
        values["battery"]["percent"] = 80
        values["battery"]["temperature_c"] = 35
        self.assertEqual([], evaluate(values, baseline_temperature=33))
        self.assertTrue(evaluate(values, baseline_temperature=32.9))


if __name__ == "__main__":
    unittest.main()
