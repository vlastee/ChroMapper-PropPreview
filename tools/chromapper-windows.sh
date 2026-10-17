#!/usr/bin/env bash
# Windows ChroMapper under Proton, for previewing and editing published Vivify maps on Linux.
#
# Published maps ship only the game's bundle (bundleWindows2021.vivify), whose shaders are
# Direct3D 11 only: the native Linux ChroMapper (Vulkan) loads it but draws nothing. Windows
# ChroMapper under Proton draws it, run with Direct3D 11 (DXVK) like the game: under its default
# Direct3D 12 (vkd3d) tessellated grass with a geometry stage (43a25) draws nothing and can stall it.
#
#   tools/chromapper-windows.sh install [BUILD]   Windows ChroMapper BUILD (default: the Linux
#                                                 install's build, from ~/Applications/cm-version)
#                                                 + PropPreview + PropEdit from the Linux install
#   tools/chromapper-windows.sh fetch CODE        a BeatSaver map (e.g. 4968d) into the WIP levels
#   tools/chromapper-windows.sh [run]             ChroMapper in a window (the default once installed)
#
# Settings (environment):
#   CM_WIN_DIR     install folder              (default ~/Applications/chromapper-win)
#   CM_WIN_PREFIX  Proton prefix folder        (default ~/.local/share/bsmap/compatdata-chromapper)
#   CM_PROTON      Proton folder               (default .../steamapps/common/Proton Hotfix)
#   CM_BEATSABER   Beat Saber folder for maps  (default ~/.local/share/bsmap/game-1.40.8)
#
# The install patches the newest full Windows build at or below BUILD up to BUILD with ChroMapper's
# own xdelta patches from cm.topc.at (the launcher's update server); needs xdelta3, curl, unzip.
set -euo pipefail

DIR="${CM_WIN_DIR:-$HOME/Applications/chromapper-win}"
PREFIX="${CM_WIN_PREFIX:-$HOME/.local/share/bsmap/compatdata-chromapper}"
PROTON="${CM_PROTON:-/mnt/media3/SteamLibrary/steamapps/common/Proton Hotfix}"
BEATSABER="${CM_BEATSABER:-$HOME/.local/share/bsmap/game-1.40.8}"
LINUX_CM="$HOME/Applications/chromapper"
SERVER=https://cm.topc.at
SETTINGS="$PREFIX/pfx/drive_c/users/steamuser/AppData/LocalLow/BinaryElement/ChroMapper/ChroMapperSettings.json"

keys() { curl -sf "$SERVER/?prefix=$1" | grep -o '<Key>[^<]*</Key>' | sed 's/<[^>]*>//g'; }

install() {
    local build="${1:-$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$HOME/Applications/cm-version")}"
    [ -e "$DIR" ] && { echo "$DIR exists; remove it first to reinstall"; exit 1; }
    # The patch to BUILD from the newest base that has a full archive.
    local base="" b
    for b in $(keys "win/$build/" | sed -n 's|^win/[0-9]*/\([0-9]*\)\.patch$|\1|p' | sort -rn); do
        if keys "win/$b/" | grep -qx "win/$b/Win64.zip"; then base=$b; break; fi
    done
    [ -n "$base" ] || { echo "no full Windows archive to patch $build from"; exit 1; }
    local tmp; tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT
    echo "ChroMapper $base (Win64.zip) + patch to $build"
    curl -sSf -o "$tmp/base.zip" "$SERVER/win/$base/Win64.zip"
    curl -sSf "$SERVER/win/$build/$base.patch" | gzip -dc > "$tmp/patch.tar"
    mkdir "$tmp/base" "$tmp/patch"
    unzip -q "$tmp/base.zip" -d "$tmp/base"
    tar xf "$tmp/patch.tar" -C "$tmp/patch"
    (cd "$tmp/patch/xdelta" && find . -type f) | while read -r f; do
        local src="$tmp/base/chromapper/$f"
        mkdir -p "$(dirname "$src")"
        if [ -f "$src" ]; then xdelta3 -d -f -s "$src" "$tmp/patch/xdelta/$f" "$src.new"
        else xdelta3 -d -f "$tmp/patch/xdelta/$f" "$src.new"; fi
        mv "$src.new" "$src"
    done
    mv "$tmp/base/chromapper" "$DIR"
    mkdir -p "$DIR/Plugins"
    cp "$LINUX_CM"/Plugins/*.dll "$DIR/Plugins/" 2>/dev/null || true
    echo "installed $DIR (plugins: $(ls "$DIR/Plugins" | tr '\n' ' '))"
    echo "first run: tools/chromapper-windows.sh run (then set the Beat Saber folder if asked)"
}

fetch() {
    local code="$1"
    local url name
    read -r url name < <(curl -sf "https://api.beatsaver.com/maps/id/$code" | python3 -I -c '
import json, re, sys
m = json.load(sys.stdin)
print(m["versions"][0]["downloadURL"], re.sub(r"\s+", " ", re.sub(r"[^\w .()-]", "", m["name"], flags=re.ASCII))[:60].strip())')
    local dest="$BEATSABER/Beat Saber_Data/CustomWIPLevels/$code ($name)"
    [ -e "$dest" ] && { echo "already there: $dest"; return; }
    mkdir -p "$dest"
    curl -sSf -o "$dest.zip" "$url"
    unzip -q "$dest.zip" -d "$dest"
    rm "$dest.zip"
    # Some maps (Extra Sensory II) ship no bundle: Vivify downloads it by checksum from its repository.
    local crc
    crc=$(python3 -I -c '
import json, sys
print(json.load(open(sys.argv[1]))["_customData"]["_assetBundle"].get("_windows2021", ""))' "$dest/Info.dat" 2>/dev/null || true)
    if [ -n "$crc" ] && [ ! -e "$dest/bundleWindows2021.vivify" ]; then
        url=$(curl -sSf "https://repo.totalbs.dev/api/v1/bundles/$crc" | python3 -I -c 'import json, sys; print(json.load(sys.stdin)["downloadUrl"])')
        curl -sSf -o "$dest/bundleWindows2021.vivify" "$url"
        echo "bundle $crc from the Vivify repository"
    fi
    echo "$dest"
}

run() {
    mkdir -p "$PREFIX"
    if [ -f "$SETTINGS" ]; then
        # Point ChroMapper at the Beat Saber folder (Windows path through Wine's Z: drive).
        python3 -I - "$SETTINGS" "Z:${BEATSABER//\//\\}" <<'EOF'
import json, sys
path, beatsaber = sys.argv[1], sys.argv[2]
d = json.load(open(path))
if d.get("BeatSaberInstallation") != beatsaber:
    d["BeatSaberInstallation"] = beatsaber
    open(path, "w").write(json.dumps(d, indent=2).replace('": ', '" : '))
EOF
    fi
    cd "$DIR"
    STEAM_COMPAT_DATA_PATH="$PREFIX" STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.steam/steam" \
        exec "$PROTON/proton" run ./ChroMapper.exe -force-d3d11
}

# The comment block at the top, without its "# ".
usage() { awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 1; }

case "${1:-}" in
    install) shift; install "$@" ;;
    fetch) shift; fetch "$@" ;;
    run) run ;;
    "") [ -e "$DIR/ChroMapper.exe" ] && run || usage ;;
    *) usage ;;
esac
