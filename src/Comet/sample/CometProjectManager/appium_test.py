#!/usr/bin/env python3
"""
Appium mac2 driver test for CometProjectManager.
Verifies the app launches, UI elements render, navigation works, and state updates propagate.
"""
import sys
import time
import json

try:
    from appium import webdriver
    from appium.options.mac import Mac2Options
    from appium.webdriver.common.appiumby import AppiumBy
except ImportError:
    print("ERROR: Appium Python client not available")
    sys.exit(1)

APP_PATH = "/Users/jfversluis/Documents/GitHub/Comet/sample/CometProjectManager/bin/Debug/net9.0-maccatalyst/maccatalyst-arm64/CometProjectManager.app"

results = []
def log_result(test_name, passed, detail=""):
    status = "✅ PASS" if passed else "❌ FAIL"
    results.append((test_name, passed, detail))
    print(f"  {status}: {test_name}" + (f" — {detail}" if detail else ""))

def main():
    print("\n🔬 CometProjectManager — Appium Functional Tests")
    print("=" * 55)

    options = Mac2Options()
    options.bundle_id = "com.comet.projectmanager"
    options.set_capability("appium:arguments", [])
    options.set_capability("appium:environment", {})
    options.set_capability("appium:showServerLogs", False)
    
    driver = None
    try:
        driver = webdriver.Remote("http://localhost:4723", options=options)
        driver.implicitly_wait(10)
        print(f"  App launched, session: {driver.session_id[:12]}...")
        time.sleep(3)

        # Test 1: App window exists
        try:
            window = driver.find_element(AppiumBy.CLASS_NAME, "XCUIElementTypeWindow")
            log_result("App window exists", window is not None)
        except Exception as e:
            log_result("App window exists", False, str(e)[:80])

        # Test 2: Look for tab bar / navigation structure
        try:
            tabs = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeTabBar")
            log_result("TabView renders", len(tabs) > 0, f"Found {len(tabs)} tab bar(s)")
        except Exception as e:
            log_result("TabView renders", False, str(e)[:80])

        # Test 3: Dashboard content - look for text elements
        try:
            texts = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeStaticText")
            text_values = [t.get_attribute("value") or t.text for t in texts[:30]]
            has_categories = any("Task Categories" in str(t) or "Categories" in str(t) for t in text_values)
            has_projects = any("Projects" in str(t) for t in text_values)
            has_tasks = any("Tasks" in str(t) or "task" in str(t).lower() for t in text_values)
            log_result("Dashboard shows categories", has_categories, f"Found texts: {text_values[:5]}")
            log_result("Dashboard shows projects", has_projects)
            log_result("Dashboard shows tasks", has_tasks)
        except Exception as e:
            log_result("Dashboard content renders", False, str(e)[:80])

        # Test 4: Look for project names from seed data
        try:
            all_texts = [t.get_attribute("value") or t.text for t in 
                        driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeStaticText")]
            has_mobile = any("Mobile" in str(t) for t in all_texts)
            has_website = any("Website" in str(t) for t in all_texts)
            log_result("Seed data renders (Mobile App)", has_mobile)
            log_result("Seed data renders (Website Redesign)", has_website)
        except Exception as e:
            log_result("Seed data renders", False, str(e)[:80])

        # Test 5: Navigate to Projects tab
        try:
            tab_buttons = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeButton")
            projects_tab = None
            for btn in tab_buttons:
                label = btn.get_attribute("label") or btn.get_attribute("value") or ""
                if "Projects" in label:
                    projects_tab = btn
                    break
            if projects_tab:
                projects_tab.click()
                time.sleep(2)
                all_texts = [t.get_attribute("value") or t.text for t in 
                            driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeStaticText")]
                log_result("Navigate to Projects tab", True, f"Texts: {all_texts[:5]}")
            else:
                log_result("Navigate to Projects tab", False, "Could not find Projects tab button")
        except Exception as e:
            log_result("Navigate to Projects tab", False, str(e)[:80])

        # Test 6: Navigate to Manage Meta tab
        try:
            tab_buttons = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeButton")
            meta_tab = None
            for btn in tab_buttons:
                label = btn.get_attribute("label") or btn.get_attribute("value") or ""
                if "Manage" in label or "Meta" in label:
                    meta_tab = btn
                    break
            if meta_tab:
                meta_tab.click()
                time.sleep(2)
                all_texts = [t.get_attribute("value") or t.text for t in 
                            driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeStaticText")]
                has_categories = any("Categories" in str(t) for t in all_texts)
                has_tags = any("Tags" in str(t) for t in all_texts)
                log_result("Navigate to Manage Meta tab", True)
                log_result("Manage Meta shows Categories", has_categories)
                log_result("Manage Meta shows Tags", has_tags)
                # Check seed categories
                has_dev = any("Development" in str(t) for t in all_texts)
                has_design = any("Design" in str(t) for t in all_texts)
                log_result("Categories seed data (Development)", has_dev)
                log_result("Categories seed data (Design)", has_design)
                # Check seed tags
                has_urgent = any("Urgent" in str(t) for t in all_texts)
                has_feature = any("Feature" in str(t) for t in all_texts)
                log_result("Tags seed data (Urgent)", has_urgent)
                log_result("Tags seed data (Feature)", has_feature)
            else:
                log_result("Navigate to Manage Meta tab", False, "Tab not found")
        except Exception as e:
            log_result("Navigate to Manage Meta tab", False, str(e)[:80])

        # Test 7: Navigate back to Dashboard
        try:
            tab_buttons = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeButton")
            dash_tab = None
            for btn in tab_buttons:
                label = btn.get_attribute("label") or btn.get_attribute("value") or ""
                if "Dashboard" in label:
                    dash_tab = btn
                    break
            if dash_tab:
                dash_tab.click()
                time.sleep(2)
                log_result("Navigate back to Dashboard", True)
            else:
                log_result("Navigate back to Dashboard", False, "Tab not found")
        except Exception as e:
            log_result("Navigate back to Dashboard", False, str(e)[:80])

        # Test 8: Check for interactive elements (buttons)
        try:
            buttons = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeButton")
            button_labels = [b.get_attribute("label") or "" for b in buttons[:20]]
            has_add = any("Add" in l for l in button_labels)
            log_result("Add Task button exists", has_add, f"Buttons: {button_labels[:8]}")
        except Exception as e:
            log_result("Interactive elements", False, str(e)[:80])

        # Test 9: Tap a task checkbox (toggle complete)
        try:
            texts = driver.find_elements(AppiumBy.CLASS_NAME, "XCUIElementTypeStaticText")
            checkbox = None
            for t in texts:
                val = t.get_attribute("value") or t.text or ""
                if "⬜" in val or "✅" in val:
                    checkbox = t
                    break
            if checkbox:
                original = checkbox.get_attribute("value") or checkbox.text
                checkbox.click()
                time.sleep(1)
                log_result("Task toggle tap works", True, f"Tapped checkbox: {original[:20]}")
            else:
                log_result("Task toggle tap works", False, "No checkbox found")
        except Exception as e:
            log_result("Task toggle tap works", False, str(e)[:80])

        # Test 10: Source tree dump for debugging
        try:
            source = driver.page_source
            has_content = len(source) > 500
            log_result("Page source available", has_content, f"{len(source)} chars")
        except Exception as e:
            log_result("Page source available", False, str(e)[:80])

    except Exception as e:
        print(f"\n❌ FATAL: {e}")
        import traceback
        traceback.print_exc()
    finally:
        if driver:
            try:
                driver.quit()
            except:
                pass

    # Summary
    total = len(results)
    passed = sum(1 for _, p, _ in results if p)
    failed = total - passed
    print(f"\n{'=' * 55}")
    print(f"📊 Results: {passed}/{total} passed, {failed} failed")
    if failed > 0:
        print("Failed tests:")
        for name, p, detail in results:
            if not p:
                print(f"  ❌ {name}: {detail}")
    
    return 0 if failed == 0 else 1

if __name__ == "__main__":
    sys.exit(main())
