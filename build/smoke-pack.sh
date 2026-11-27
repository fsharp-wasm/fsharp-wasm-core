#!/usr/bin/env bash
# smoke-pack.sh — the packages as a consumer sees them: a new project that restores them from dist/nupkg (and nothing else of this repository) and runs.
#
#   bash build/smoke-pack.sh <version>      # after `bash build/pack.sh <version>`
#
# It checks what a unit test cannot: that the package restores with its dependencies (FSharp.Wasm.Live brings Core, Frame and Graph), that the dll loads, and that
# the sources are in the package where the compiler looks for them (fable/).  The packages cache is the temporary folder's own, so a package of the same
# version from an earlier run cannot be what is tested.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:?usage: smoke-pack.sh <version>}"
DIR="$(mktemp -d)"; trap 'rm -rf "$DIR"' EXIT
export NUGET_PACKAGES="$DIR/packages"
cd "$DIR"
cat > nuget.config <<XML
<configuration><packageSources><clear/><add key="local" value="$ROOT/dist/nupkg"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
XML
cat > Smoke.fsproj <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.fs" />
    <PackageReference Include="FSharp.Wasm.Data" Version="$VERSION" />
    <PackageReference Include="FSharp.Wasm.Live" Version="$VERSION" />
  </ItemGroup>
</Project>
XML
cat > Program.fs <<'FS'
open FSharp.Wasm.Data
match Json.parse "{\"a\":[1,2,3]}" with
| Ok v -> printfn "%s" (Json.stringify v)
| Error e -> eprintfn "%s" e; exit 1
printfn "%s" (System.Reflection.Assembly.Load("FSharp.Wasm.Live").GetName().Version.ToString())
FS
dotnet run -c Release --nologo -v q
for id in FSharp.Wasm.Core FSharp.Wasm.Frame FSharp.Wasm.Graph FSharp.Wasm.Live; do
  lower="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
  ls "$NUGET_PACKAGES/$lower/$VERSION/fable/"*.fsproj > /dev/null || { echo "$id: no sources under fable/" >&2; exit 1; }
done
echo "smoke: the packages restore, load and carry their sources"
