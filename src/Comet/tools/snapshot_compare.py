#!/usr/bin/env python3
"""Snapshot comparison: MAUI Reference vs Comet MVU — pixel-level + SSIM analysis."""

import subprocess, time, os, sys
import numpy as np
from PIL import Image

try:
    from skimage.metrics import structural_similarity as ssim
    HAS_SSIM = True
except ImportError:
    HAS_SSIM = False

SIM = "9B25DEC2-88C8-4612-B5E2-2B04C8313D4D"
MAUI_BUNDLE = "com.companyname.mauireference"
COMET_BUNDLE = "com.comet.projectmanager"
OUTPUT_DIR = "/tmp/snapshots"
COMET_DIR = "/Users/jfversluis/Documents/GitHub/Comet"

os.makedirs(OUTPUT_DIR, exist_ok=True)

def simctl(*args):
    return subprocess.run(["xcrun", "simctl"] + list(args),
                         capture_output=True, text=True, timeout=30, cwd=COMET_DIR)

def screenshot(name):
    path = f"{OUTPUT_DIR}/{name}.png"
    simctl("io", SIM, "screenshot", path)
    return path

def launch_app(bundle_id, extra_args=None, wait=10):
    simctl("terminate", SIM, bundle_id)
    time.sleep(1)
    args = ["launch", SIM, bundle_id]
    if extra_args:
        args += ["--"] + extra_args
    simctl(*args)
    time.sleep(wait)

def pixel_compare(img1, img2, tolerance=15):
    """Fast pixel comparison using numpy."""
    a1 = np.array(img1)[:,:,:3].astype(np.int16)
    a2 = np.array(img2)[:,:,:3].astype(np.int16)
    h = min(a1.shape[0], a2.shape[0])
    w = min(a1.shape[1], a2.shape[1])
    a1, a2 = a1[:h,:w], a2[:h,:w]
    diff = np.abs(a1 - a2)
    matching = np.all(diff <= tolerance, axis=2)
    return np.mean(matching) * 100

def ssim_compare(img1, img2):
    """SSIM comparison (perceptual similarity)."""
    if not HAS_SSIM:
        return None
    a1 = np.array(img1.convert('L'))
    a2 = np.array(img2.convert('L'))
    h = min(a1.shape[0], a2.shape[0])
    w = min(a1.shape[1], a2.shape[1])
    return ssim(a1[:h,:w], a2[:h,:w]) * 100

def analyze_page(maui_path, comet_path, page_name):
    """Detailed analysis of a page comparison."""
    maui = Image.open(maui_path)
    comet = Image.open(comet_path)

    print(f"\n{'='*50}")
    print(f"📊 {page_name}")
    print(f"{'='*50}")

    # Full-page pixel comparison
    pix_sim = pixel_compare(maui, comet)
    status = "✅" if pix_sim >= 90 else "⚠️" if pix_sim >= 75 else "❌"
    print(f"  {status} Pixel match: {pix_sim:.1f}%")

    # SSIM comparison
    ssim_val = ssim_compare(maui, comet)
    if ssim_val is not None:
        status2 = "✅" if ssim_val >= 85 else "⚠️" if ssim_val >= 70 else "❌"
        print(f"  {status2} SSIM: {ssim_val:.1f}%")

    # Region analysis
    h = min(maui.height, comet.height)
    regions = {
        "Nav Bar": (0, 400),
        "Content": (400, h - 300),
        "Bottom": (h - 300, h),
    }

    for rname, (y1, y2) in regions.items():
        if y1 >= h or y2 <= y1:
            continue
        m_region = maui.crop((0, y1, maui.width, min(y2, maui.height)))
        c_region = comet.crop((0, y1, comet.width, min(y2, comet.height)))
        rpix = pixel_compare(m_region, c_region)
        rs = "✅" if rpix >= 90 else "⚠️" if rpix >= 75 else "❌"
        print(f"    {rs} {rname}: {rpix:.1f}%")

    # Save side-by-side
    w = min(maui.width, comet.width)
    side = Image.new('RGB', (w*2 + 10, h), (128, 128, 128))
    side.paste(maui.crop((0, 0, w, h)), (0, 0))
    side.paste(comet.crop((0, 0, w, h)), (w + 10, 0))
    side.save(f"{OUTPUT_DIR}/side_{page_name.lower().replace(' ', '_')}.png")

    # Save diff overlay
    a1 = np.array(maui.crop((0, 0, w, h)))[:,:,:3].astype(np.int16)
    a2 = np.array(comet.crop((0, 0, w, h)))[:,:,:3].astype(np.int16)
    diff_mask = np.any(np.abs(a1 - a2) > 15, axis=2)
    diff_img = np.array(maui.crop((0, 0, w, h)))[:,:,:3].copy()
    diff_img[diff_mask] = [255, 0, 0]
    Image.fromarray(diff_img.astype(np.uint8)).save(
        f"{OUTPUT_DIR}/diff_{page_name.lower().replace(' ', '_')}.png")

    return pix_sim, ssim_val

# Page mapping: (comet_arg, maui_arg, wait_time)
PAGES = {
    "Dashboard": ("dashboard", "main", 30),
    "Projects": ("projects", "projects", 30),
    "ManageMeta": ("manage", "manage", 10),
    "ProjectDetail": ("projectdetail", "projectdetail", 35),
    "TaskDetail": ("taskdetail", "taskdetail", 35),
}

def main():
    print("🔍 SNAPSHOT COMPARISON: MAUI Reference vs Comet MVU")
    print("=" * 60)

    if not HAS_SSIM:
        print("⚠️  scikit-image not installed, skipping SSIM. pip install scikit-image")

    # Build Comet
    print("\n🔨 Building Comet app...")
    subprocess.run(["dotnet", "build", "-f", "net9.0-ios",
                    "sample/CometProjectManager/CometProjectManager.csproj",
                    "--no-restore", "-v", "q"],
                   capture_output=True, cwd=COMET_DIR)
    simctl("install", SIM,
           f"{COMET_DIR}/sample/CometProjectManager/bin/Debug/net9.0-ios/iossimulator-arm64/CometProjectManager.app")

    results = {}

    for page_name, (comet_arg, maui_arg, wait) in PAGES.items():
        print(f"\n📸 {page_name}...")

        # Capture MAUI
        launch_app(MAUI_BUNDLE, [f"--page={maui_arg}"], wait=wait)
        maui_path = screenshot(f"maui_{maui_arg}")

        # Capture Comet
        launch_app(COMET_BUNDLE, [f"--page={comet_arg}"], wait=10)
        comet_path = screenshot(f"comet_{comet_arg}")

        pix, ssim_val = analyze_page(maui_path, comet_path, page_name)
        results[page_name] = (pix, ssim_val)

    # Summary
    print(f"\n{'='*60}")
    print("📋 RESULTS SUMMARY")
    print(f"{'='*60}")
    for name, (pix, ssim_val) in results.items():
        status = "✅ PASS" if pix >= 90 else "⚠️ CLOSE" if pix >= 80 else "❌ FAIL"
        ssim_str = f", SSIM={ssim_val:.1f}%" if ssim_val else ""
        print(f"  {status} {name}: Pixel={pix:.1f}%{ssim_str}")

    avg_pix = np.mean([p for p, _ in results.values()])
    print(f"\n  📈 Average Pixel: {avg_pix:.1f}%")
    if HAS_SSIM:
        avg_ssim = np.mean([s for _, s in results.values() if s])
        print(f"  📈 Average SSIM: {avg_ssim:.1f}%")

    print(f"\n  Output: {OUTPUT_DIR}/")

if __name__ == "__main__":
    main()
