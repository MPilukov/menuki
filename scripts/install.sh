#!/usr/bin/env bash
#
# Install Menuki as a single self-contained `menuki` binary on your PATH.
#
# Usage:
#   ./scripts/install.sh                 # build for this machine, install to ~/.local/bin
#   PREFIX=/usr/local/bin ./scripts/install.sh   # install somewhere else (may need sudo)
#
# Requires the .NET 8 SDK to build. The produced binary is self-contained: it bundles
# the runtime, so end users do NOT need .NET installed to run it.
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
csproj="$repo_root/Menuki/Menuki.csproj"
prefix="${PREFIX:-$HOME/.local/bin}"
name="menuki"

# --- pick the runtime identifier for this machine ---------------------------
os="$(uname -s)"
arch="$(uname -m)"
case "$os" in
  Darwin) plat="osx" ;;
  Linux)  plat="linux" ;;
  *)      echo "Unsupported OS: $os (use 'dotnet publish' manually for Windows)." >&2; exit 1 ;;
esac
case "$arch" in
  arm64|aarch64) cpu="arm64" ;;
  x86_64|amd64)  cpu="x64" ;;
  *)             echo "Unsupported architecture: $arch" >&2; exit 1 ;;
esac
rid="$plat-$cpu"

echo "Building $name for $rid ..."
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

# Single-file, self-contained, compressed, NOT trimmed - the plugin loader relies on reflection.
dotnet publish "$csproj" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:AssemblyName="$name" \
  -o "$out" \
  --nologo

mkdir -p "$prefix"
install -m 0755 "$out/$name" "$prefix/$name"

echo "Installed: $prefix/$name"
if ! printf '%s' ":$PATH:" | grep -q ":$prefix:"; then
  echo
  echo "NOTE: $prefix is not on your PATH. Add this to your shell profile:"
  echo "  export PATH=\"$prefix:\$PATH\""
fi
echo
echo "Try it:"
echo "  $name --config \"$repo_root/Menuki/examples/dev-runbook.json\""
