#!/usr/bin/env bash
# Tests and builds the current version into distros/<version>/: the PC app as a zip (Couchpotato.exe with
# its .config and DLLs, in a Couchpotato folder), the TV app .ipk, their SHA-256 sums and RELEASE-NOTES.md
# (to paste into the GitHub release).
# Then, if every change is committed, tags the commit v<version> so its source can be found again.
#
# Usage: tools/release.sh [--force]   (--force replaces an existing distros/<version>)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
TV_VERSION=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' tvapp/app/appinfo.json)
OUT="distros/$VERSION"

if [[ -e "$OUT" && "${1:-}" != "--force" ]]; then
  echo "$OUT already exists. Bump <Version> in Directory.Build.props, or pass --force to rebuild it." >&2
  exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT"
dotnet run --project tests/Couchpotato.Tests -c Release
MR_IPK_OUT="$PWD/$OUT/couchpotato-tv-$TV_VERSION.ipk" dotnet run --project tests/Couchpotato.Tests -c Release
rm -rf src/Couchpotato/bin/Release # no leftovers from older builds in the zip
dotnet build src/Couchpotato -c Release
BIN=src/Couchpotato/bin/Release/net48
STAGE=$(mktemp -d)
mkdir "$STAGE/Couchpotato"
cp "$BIN/Couchpotato.exe" "$BIN/Couchpotato.exe.config" "$STAGE/Couchpotato/"
while read -r dll; do
  case "$dll" in *.dll) cp "$BIN/$dll" "$STAGE/Couchpotato/" ;; esac
done < src/Couchpotato/obj/Release/net48/app-files.txt
cp LICENSE "$STAGE/Couchpotato/LICENSE.txt"
(cd "$STAGE" && zip -qrX "$PWD/Couchpotato-$VERSION.zip" Couchpotato)
mv "$STAGE/Couchpotato-$VERSION.zip" "$OUT/"
rm -rf "$STAGE"
(cd "$OUT" && shasum -a 256 -- Couchpotato-*.zip couchpotato-tv-*.ipk > SHA256SUMS.txt)

# Release notes: this version's CHANGELOG entry, then what's in the release and the usual warning.
{
  awk -v v="## $VERSION " 'index($0, v) == 1 { on = 1; next } on && /^## / { exit } on' CHANGELOG.md
  cat <<NOTES

### Download

- **Couchpotato-$VERSION.zip**: the PC app. Right-click it → **Extract All**, then run **Couchpotato.exe** from the extracted folder and choose **Install** (or **Update**).
- **couchpotato-tv-$TV_VERSION.ipk**: the TV app, only for other install tools. Couchpotato installs it on the TV by itself (TV tab → **Install on TV**).
- **SHA256SUMS.txt**: to check the downloads.

> **Windows or your antivirus may flag Couchpotato.** It isn't code-signed yet, and it does things antivirus software watches closely (it moves the mouse and types for the remote, changes the pointer, can start itself with administrator rights). If Windows says "Windows protected your PC", click **More info → Run anyway**. The source is all here, and you can build it yourself.

Couchpotato is free and open source. If it's useful to you, you can [support it on Ko-fi](https://ko-fi.com/barleymalt) ♥
NOTES
} > "$OUT/RELEASE-NOTES.md"
echo "Built $OUT:"
ls -l "$OUT"

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Uncommitted changes: commit them, then run: git tag v$VERSION"
elif git rev-parse -q --verify "refs/tags/v$VERSION" >/dev/null; then
  echo "Tag v$VERSION already exists."
else
  git tag -a "v$VERSION" -m "Couchpotato $VERSION (TV app $TV_VERSION)"
  echo "Tagged v$VERSION."
fi
