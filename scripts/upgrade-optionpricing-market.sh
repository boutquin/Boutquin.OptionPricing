#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 <market-version>"
  echo "Example: $0 0.3.0"
}

if [[ ${1:-} == "" || ${1:-} == "-h" || ${1:-} == "--help" ]]; then
  usage
  exit 1
fi

VERSION="$1"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Error: version must match MAJOR.MINOR.PATCH (for example: 0.3.0)"
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PACKAGES_FILE="$REPO_ROOT/Directory.Packages.props"
NUGET_FILE="$REPO_ROOT/NuGet.config"

if [[ ! -f "$PACKAGES_FILE" ]]; then
  echo "Error: cannot find $PACKAGES_FILE"
  exit 1
fi

if [[ ! -f "$NUGET_FILE" ]]; then
  echo "Error: cannot find $NUGET_FILE"
  exit 1
fi

cd "$REPO_ROOT"

echo "==> Upgrading OptionPricing Market package versions to $VERSION"
TMP_FILE="$(mktemp)"
awk -v v="$VERSION" '
  {
    if ($0 ~ /PackageVersion Include="Boutquin\.Market\.Abstractions"/) {
      gsub(/Version="[^"]+"/, "Version=\"" v "\"")
    }
    if ($0 ~ /PackageVersion Include="Boutquin\.Market\.Curves"/) {
      gsub(/Version="[^"]+"/, "Version=\"" v "\"")
    }
    print
  }
' "$PACKAGES_FILE" > "$TMP_FILE"
mv "$TMP_FILE" "$PACKAGES_FILE"

echo "==> Running restore/build/test gates"
dotnet restore Boutquin.OptionPricing.sln
dotnet build Boutquin.OptionPricing.sln
dotnet test Boutquin.OptionPricing.sln

echo "==> Verifying format"
dotnet format Boutquin.OptionPricing.sln --verify-no-changes

echo "==> Upgrade complete: OptionPricing now targets Market $VERSION"
