#!/bin/sh
# Menuki installer: downloads a prebuilt, self-contained binary from GitHub Releases.
#
#   curl -fsSL https://raw.githubusercontent.com/MPilukov/menuki/main/install.sh | sh
#
# Environment overrides:
#   PREFIX=/usr/local/bin   install location (default ~/.local/bin; may need sudo)
#   MENUKI_VERSION=v0.1.0   install a specific tag (default: latest release)
#
# macOS and Linux only. On Windows, download menuki-win-x64.zip from the Releases page.
set -eu

REPO="MPilukov/menuki"
BIN="menuki"
PREFIX="${PREFIX:-$HOME/.local/bin}"

err() { printf '\033[31merror:\033[0m %s\n' "$1" >&2; exit 1; }
info() { printf '\033[36m==>\033[0m %s\n' "$1"; }

# --- detect platform -------------------------------------------------------
os=$(uname -s)
case "$os" in
  Linux)  os_tag="linux" ;;
  Darwin) os_tag="osx" ;;
  *) err "unsupported OS '$os'. Windows users: download menuki-win-x64.zip from https://github.com/$REPO/releases" ;;
esac

arch=$(uname -m)
case "$arch" in
  x86_64|amd64)  arch_tag="x64" ;;
  arm64|aarch64) arch_tag="arm64" ;;
  *) err "unsupported architecture '$arch'." ;;
esac

asset="${BIN}-${os_tag}-${arch_tag}.tar.gz"

# --- resolve version -------------------------------------------------------
version="${MENUKI_VERSION:-}"
if [ -z "$version" ]; then
  info "Resolving latest release..."
  version=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
    | grep '"tag_name"' | head -n1 | cut -d'"' -f4)
  [ -n "$version" ] || err "could not determine the latest version. Set MENUKI_VERSION=vX.Y.Z and retry."
fi

url="https://github.com/$REPO/releases/download/$version/$asset"

# --- download & install ----------------------------------------------------
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

info "Downloading $asset ($version)..."
curl -fSL --progress-bar -o "$tmp/$asset" "$url" \
  || err "download failed: $url"

info "Extracting..."
tar -xzf "$tmp/$asset" -C "$tmp" || err "could not extract $asset"
[ -f "$tmp/$BIN" ] || err "archive did not contain '$BIN'"

mkdir -p "$PREFIX"
chmod +x "$tmp/$BIN"
mv "$tmp/$BIN" "$PREFIX/$BIN" || err "could not install to $PREFIX (try: PREFIX=/usr/local/bin, or run with sudo)"

info "Installed $BIN $version to $PREFIX/$BIN"

# --- PATH hint -------------------------------------------------------------
case ":$PATH:" in
  *":$PREFIX:"*) : ;;
  *)
    printf '\n\033[33mNote:\033[0m %s is not on your PATH. Add this to your shell profile:\n' "$PREFIX"
    printf '  export PATH="%s:$PATH"\n' "$PREFIX"
    ;;
esac

printf '\nRun \033[36mmenuki\033[0m to get started, or \033[36mmenuki tour\033[0m for the guided tour.\n'
