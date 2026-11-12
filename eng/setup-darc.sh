#!/usr/bin/env bash
set -euo pipefail

REPO="https://github.com/dotnet/maui-labs"
TARGET_BRANCH="main"

echo "=== Arcade Dependency Flow Setup for dotnet/maui-labs ==="
echo ""

# 1. Check darc
if ! command -v darc &>/dev/null && [ ! -f "$HOME/.dotnet/tools/darc" ]; then
  echo "Installing darc CLI..."
  ./eng/common/darc-init.sh
  export PATH="$HOME/.dotnet/tools:$PATH"
else
  echo "✓ darc is installed: $(which darc 2>/dev/null || echo "$HOME/.dotnet/tools/darc")"
fi

# 2. Verify authentication
echo ""
if ! darc get-channels >/dev/null 2>&1; then
  echo "darc is not authenticated. Running 'darc authenticate'..."
  echo "You will need a BAR token from https://maestro.dot.net/"
  darc authenticate
else
  echo "✓ darc is authenticated"
fi

# 3. Set default channel
echo ""
echo "Setting default channel..."
darc add-default-channel \
  --branch "refs/heads/$TARGET_BRANCH" \
  --repo "$REPO" \
  --channel ".NET 11 Dev" \
  || echo "(may already exist)"

# 4. Subscribe to arcade updates
echo ""
echo "Creating arcade subscription..."
darc add-subscription \
  --channel ".NET Eng - Latest" \
  --source-repo https://github.com/dotnet/arcade \
  --target-repo "$REPO" \
  --target-branch "$TARGET_BRANCH" \
  --update-frequency everyDay \
  --standard-automerge \
  || echo "(may already exist)"

# 5. Subscribe to MAUI updates
echo ""
echo "Creating MAUI subscription..."
darc add-subscription \
  --channel ".NET 11.0.1xx SDK" \
  --source-repo https://github.com/dotnet/maui \
  --target-repo "$REPO" \
  --target-branch "$TARGET_BRANCH" \
  --update-frequency everyDay \
  --standard-automerge \
  || echo "(may already exist)"

# 6. Subscribe to Runtime updates
echo ""
echo "Creating Runtime subscription..."
darc add-subscription \
  --channel ".NET 11" \
  --source-repo https://github.com/dotnet/runtime \
  --target-repo "$REPO" \
  --target-branch "$TARGET_BRANCH" \
  --update-frequency everyDay \
  --standard-automerge \
  || echo "(may already exist)"

# 7. Verify
echo ""
echo "=== Subscriptions ==="
darc get-subscriptions --target-repo "$REPO"
echo ""
echo "=== Default Channels ==="
darc get-default-channels --source-repo "$REPO"
echo ""
echo "✓ Done! Dependency flow is configured."
