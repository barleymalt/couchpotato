#!/usr/bin/env bash
# Renders the README screenshots of the settings window (docs/screenshot-*.png) from
# tools/screenshots/window.html with headless Chrome, at 2x, on a transparent background.
#
# Usage: tools/make_screenshots.sh      (needs Google Chrome; CHROME=/path/to/chrome to use another)
set -euo pipefail
cd "$(dirname "$0")/.."

CHROME=${CHROME:-"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"}
PAGE="file://$PWD/tools/screenshots/window.html"

# tab and window height, as in window.html; the page adds 72 px of margin around the window for its shadow.
for shot in tv:1080 buttons:1000 pointer:1020; do
  tab=${shot%%:*}
  height=$(( ${shot##*:} + 72 ))
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars --allow-file-access-from-files \
    --force-device-scale-factor=2 --default-background-color=00000000 \
    --window-size=952,$height --screenshot="$PWD/docs/screenshot-$tab.png" "$PAGE?tab=$tab" 2>/dev/null
  echo "wrote docs/screenshot-$tab.png"
done
