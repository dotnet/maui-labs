#!/bin/bash
# Phase 1.5 E2E runner — iPhone 17 Pro / iOS 26.2
# Per sample: clean-build, install, launch, alive-check, screenshot,
# smoke-interact (toggle appearance), screenshot, terminate, record.
set +e

UDID="${UDID:-95EC018A-A8CF-4FAB-98A4-EF49D2E626B3}"
ROOT=/Users/davidortinau/work/maui-labs/src/Comet
SAMPLE_ROOT="$ROOT/sample"
ART_ROOT="$ROOT/.squad/artifacts/phase15-e2e"
LOG_ROOT="$ROOT/.squad/e2elogs"
RESULT_CSV="$LOG_ROOT/results.csv"
mkdir -p "$ART_ROOT" "$LOG_ROOT"
: > "$RESULT_CSV"
echo "sample,bundle_id,build,launch,interact,note" >> "$RESULT_CSV"

sample_at() {
  case "$1" in
    Comet.Sample)            echo "com.companyname.comet.sample" ;;
    CometMauiApp)            echo "com.companyname.cometmauiapp" ;;
    CometVideoApp)           echo "com.comet.videoapp" ;;
    CometBaristaNotes)       echo "com.comet.baristanotes" ;;
    CometControlsGallery)    echo "com.companyname.cometcontrolsgallery" ;;
    CometAllTheLists)        echo "com.comet.allthelists" ;;
    CometDigitsGame)         echo "com.comet.digitsgame" ;;
    CometFeatureShowcase)    echo "com.comet.featureshowcase" ;;
    CometMailApp)            echo "com.comet.mailapp" ;;
    CometMarvelousApp)       echo "com.comet.marvelous" ;;
    CometOrderingApp)        echo "com.comet.orderingapp" ;;
    CometProjectManager)     echo "com.comet.projectmanager" ;;
    CometRecipeApp)          echo "com.comet.recipeapp" ;;
    CometStressTest)         echo "com.comet.stresstest" ;;
    CometSurfingApp)         echo "com.comet.surfingapp" ;;
    CometTaskApp)            echo "com.comet.taskapp" ;;
    CometTodoApp)            echo "com.comet.todoapp" ;;
    CometTrackizerApp)       echo "com.comet.trackizerapp" ;;
    CometWeather)            echo "com.comet.weather" ;;
  esac
}

run_sample() {
  local name="$1"
  local bid; bid=$(sample_at "$name")
  local dir="$SAMPLE_ROOT/$name"
  local csproj; csproj=$(ls "$dir"/*.csproj 2>/dev/null | head -1)
  local art="$ART_ROOT/$name"
  mkdir -p "$art"
  local blog="$LOG_ROOT/$name.build.log"
  local rlog="$LOG_ROOT/$name.run.log"
  local note=""
  local build=FAIL launch=FAIL interact=SKIP

  echo "================================================================="
  echo ">>> $name ($bid)"
  echo "================================================================="

  # Terminate any prior instance
  xcrun simctl terminate "$UDID" "$bid" >/dev/null 2>&1

  # Build (incremental). If build fails, skip.
  dotnet build "$csproj" -c Debug -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64 \
    -nologo -v:q -clp:NoSummary > "$blog" 2>&1
  local brc=$?
  local errs; errs=$(grep -cE ": error " "$blog")
  if [ "$brc" -ne 0 ] || [ "$errs" -gt 0 ]; then
    note="build rc=$brc errs=$errs; see $name.build.log"
    echo "$name,$bid,BUILD-FAIL,,,\"$note\"" >> "$RESULT_CSV"
    return
  fi
  build=PASS

  # Locate .app
  local app
  app=$(find "$dir/bin/Debug/net11.0-ios" -maxdepth 3 -name "*.app" -type d 2>/dev/null | head -1)
  if [ -z "$app" ]; then
    note="build ok but no .app produced"
    echo "$name,$bid,PASS,MISSING-APP,,\"$note\"" >> "$RESULT_CSV"
    return
  fi

  # Install
  xcrun simctl install "$UDID" "$app" >/dev/null 2>&1

  # Launch — "bid: pid" prints to stdout, app keeps running after simctl exits
  local lout; lout=$(xcrun simctl launch --terminate-running-process "$UDID" "$bid" 2>&1)
  local pid; pid=$(echo "$lout" | grep -oE "^${bid}: [0-9]+" | awk '{print $2}')
  echo "launched pid=$pid" > "$rlog"
  sleep 6
  # Take launch screenshot
  xcrun simctl io "$UDID" screenshot "$art/launch.png" >/dev/null 2>&1

  local alive=""
  if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then alive=yes; else alive=no; fi

  # If dead, pull crash diagnostics from DiagnosticReports (newest for this sample)
  local crashtxt=""
  if [ "$alive" = "no" ]; then
    local ips; ips=$(ls -t ~/Library/Logs/DiagnosticReports/${name}-*.ips 2>/dev/null | head -1)
    if [ -n "$ips" ] && [ $(( $(date +%s) - $(stat -f %m "$ips") )) -lt 120 ]; then
      # Fresh crash log - parse reason via python3
      crashtxt=$(python3 - "$ips" <<'PY' 2>/dev/null
import sys, json
raw = open(sys.argv[1]).read().split('\n', 1)
d = json.loads(raw[1])
leb = d.get('lastExceptionBacktrace') or []
# Try to surface the reason from the ObjC exception preprocessing
# Most useful data is in the termination reason
t = d.get('termination') or {}
ex = d.get('exception') or {}
print(f"{ex.get('type','?')}/{ex.get('signal','?')} {t.get('indicator','')}")
PY
      )
      cp "$ips" "$art/crash.ips" 2>/dev/null
    fi
  fi

  if [ "$alive" = "yes" ]; then
    launch=PASS
  else
    launch=CRASH
    [ -n "$crashtxt" ] && note="$crashtxt"
    echo "$name,$bid,$build,CRASH-ON-LAUNCH,,\"$note\"" >> "$RESULT_CSV"
    echo "<<< $name launch=CRASH note=$note"
    return
  fi

  # Smoke interact: toggle appearance (exercises trait change / color resolution)
  local cur new
  cur=$(xcrun simctl ui "$UDID" appearance 2>/dev/null | tr -d '\n')
  if [ "$cur" = "light" ]; then new=dark; else new=light; fi
  xcrun simctl ui "$UDID" appearance "$new" >/dev/null 2>&1
  sleep 2
  # Second screenshot
  xcrun simctl io "$UDID" screenshot "$art/after-interact.png" >/dev/null 2>&1
  # Still alive?
  if kill -0 "$pid" 2>/dev/null; then
    interact=PASS
  else
    interact=CRASH
    if grep -q "Terminating app due to uncaught exception" "$rlog"; then
      note="crash-on-appearance-toggle: $(grep -m1 'reason:' "$rlog" | sed 's/.*reason://' | head -c 200)"
    else
      note="crash-on-appearance-toggle (see $name.run.log)"
    fi
  fi
  # Restore appearance
  xcrun simctl ui "$UDID" appearance light >/dev/null 2>&1

  # Terminate (no follower to clean up)
  xcrun simctl terminate "$UDID" "$bid" >/dev/null 2>&1

  echo "$name,$bid,$build,$launch,$interact,\"$note\"" >> "$RESULT_CSV"
  echo "<<< $name build=$build launch=$launch interact=$interact note=$note"
}

for s in "$@"; do
  run_sample "$s"
done

echo
echo "=== RESULTS ==="
column -t -s, "$RESULT_CSV" 2>/dev/null || cat "$RESULT_CSV"
