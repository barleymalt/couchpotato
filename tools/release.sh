#!/usr/bin/env bash
# Tests and builds the current version into distros/<version>/: the PC exe and the TV app .ipk.
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
dotnet run --project tests/MagicRemote.Tests -c Release
MR_IPK_OUT="$PWD/$OUT/magicremote-tv-$TV_VERSION.ipk" dotnet run --project tests/MagicRemote.Tests -c Release
dotnet build src/MagicRemote -c Release
cp src/MagicRemote/bin/Release/net48/MagicRemote.exe "$OUT/MagicRemote-$VERSION.exe"
(cd "$OUT" && shasum -a 256 -- * > SHA256SUMS.txt)
echo "Built $OUT:"
ls -l "$OUT"

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Uncommitted changes: commit them, then run: git tag v$VERSION"
elif git rev-parse -q --verify "refs/tags/v$VERSION" >/dev/null; then
  echo "Tag v$VERSION already exists."
else
  git tag -a "v$VERSION" -m "MagicRemote $VERSION (TV app $TV_VERSION)"
  echo "Tagged v$VERSION."
fi
