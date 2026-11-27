#!/usr/bin/env bash
# pack.sh — NuGet packages of the libraries: dist/nupkg/<PackageId>.<version>.nupkg
#
#   bash build/pack.sh [version]        # default: 0.1.0-preview
#
# Nothing is published; a release (.github/workflows/release.yml) uploads what this makes.  A package that depends on another of this repository names it by id and version, so all of them
# carry the same version.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
source "$ROOT/build/env.sh"
OUT="$ROOT/dist/nupkg"
VERSION="${1:-0.1.0-preview}"
rm -rf "$OUT"; mkdir -p "$OUT"
for p in Core Data Ui Edit Icons Design Frame Graph Formula Live Telemetry Actors; do
  dotnet pack "$ROOT/FSharp.Wasm.$p/FSharp.Wasm.$p.fsproj" -c Release -p:Version="$VERSION" -o "$OUT" --nologo -v q
done
ls -1 "$OUT"
