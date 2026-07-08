#!/usr/bin/env bash
# build.sh - Builds, packages, and updates release manifests for JellyNotify.
# Usage: ./build.sh [--version 1.0.2.0]

set -euo pipefail

# ── Variables ─────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/JellyNotify.Plugin"
OUTPUT_DIR="$SCRIPT_DIR/dist"
RELEASES_DIR="$SCRIPT_DIR/releases"
VERSION="0.1.0.4"
if [[ -x "/home/alvaro/.dotnet/dotnet" ]]; then
    export PATH="$PATH:/home/alvaro/.dotnet"
fi
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/nuget-packages}"
if [[ "${1:-}" == "--version" && -n "${2:-}" ]]; then
    VERSION="$2"
fi

echo "╔══════════════════════════════════════════╗"
echo "║         JellyNotify Build Script         ║"
echo "╚══════════════════════════════════════════╝"
echo "Version: $VERSION"
echo ""

# ── Limpieza ──────────────────────────────────────────────────────────
echo "→ Cleaning previous build output..."
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
mkdir -p "$RELEASES_DIR"

# ── Compilación ───────────────────────────────────────────────────────
echo "→ Building plugin..."
dotnet publish "$PROJECT_DIR/JellyNotify.Plugin.csproj" \
    --configuration Release \
    --output "$OUTPUT_DIR" \
    --disable-build-servers \
    --no-self-contained \
    -maxcpucount:1 \
    -p:Version="$VERSION"

# ── Verificar artefacto principal ─────────────────────────────────────
MAIN_DLL="$OUTPUT_DIR/JellyNotify.Plugin.dll"
if [[ ! -f "$MAIN_DLL" ]]; then
    echo "✗ ERROR: $MAIN_DLL was not generated"
    exit 1
fi
echo "✓ DLL generated: $MAIN_DLL"

# Copy meta.json to output.
if [[ -f "$PROJECT_DIR/meta.json" ]]; then
    cp "$PROJECT_DIR/meta.json" "$OUTPUT_DIR/meta.json"
    echo "✓ meta.json copied to output"
fi

# ── Empaquetar en ZIP ─────────────────────────────────────────────────
ZIP_NAME="jellynotify_${VERSION}.zip"
ZIP_PATH="$RELEASES_DIR/$ZIP_NAME"

echo "→ Packaging $ZIP_NAME..."
# Use Python zipfile for portability.
(
    cd "$OUTPUT_DIR"
    python3 -c "
import zipfile, os, sys
output_dir = os.getcwd()
zip_path = sys.argv[1]
candidates = ['JellyNotify.Plugin.dll', 'JellyNotify.Plugin.xml', 'meta.json']
files = [f for f in candidates if os.path.exists(os.path.join(output_dir, f))]
if not files:
    print('ERROR: No files found to package', file=sys.stderr)
    sys.exit(1)
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zf:
    for f in files:
        zf.write(f)
        print(f'  + {f}')
" "$ZIP_PATH"
)

echo "✓ ZIP created: $ZIP_PATH"

# ── Calcular MD5 ──────────────────────────────────────────────────────
CHECKSUM=$(md5sum "$ZIP_PATH" | awk '{print $1}')
echo ""
echo "✓ MD5 checksum: $CHECKSUM"
echo ""

# ── Update manifest.json and repository/manifest.json ──────────────────
MANIFEST_PATH="$SCRIPT_DIR/manifest.json"
for manifest_file in "$MANIFEST_PATH" "$SCRIPT_DIR/repository/manifest.json"; do
    if [[ -f "$manifest_file" ]]; then
        echo "→ Updating $(basename "$manifest_file")..."
        python3 -c "
import sys, json
from datetime import datetime, timezone
version = sys.argv[1]
checksum = sys.argv[2]
filepath = sys.argv[3]
timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
version_tag = version[:-2] if version.endswith('.0') else version
source_url = f'https://github.com/Rovaal-code/JellyNotify/releases/download/v{version_tag}/jellynotify_{version}.zip'
changelog = '''JellyNotify v0.1.0.4 - smarter download notifications and richer availability cards

Compatible with Jellyfin 10.11.11 (the version this build targets and was verified against), Seerr 3.3.0, Radarr 6.1.1.10360, Sonarr 4.0.17.2952, and Jellyfin Enhanced 11.12.0.0.

- Download notifications now reflect the real transfer instead of the bare grab: the instant, empty Download started that used to fire the moment Radarr/Sonarr picked a release (no progress, ETA unknown) is gone.
- Download started now fires from the queue poll, once a download is genuinely transferring - real progress with an ETA.
- New Downloading notification for the mid-download update, sent once a download reaches a configurable percentage (default 50%). Adjust it in the notification settings.
- Contenido disponible notifications driven by Seerr status now include Quality, Audio and Subtitles, looked up from the imported file in Sonarr/Radarr - previously only the webhook path carried that detail.'''
with open(filepath, 'r', encoding='utf-8') as f:
    data = json.load(f)
for plugin in data:
    if plugin.get('name') == 'JellyNotify':
        plugin['description'] = 'Personal notification plugin for Jellyfin with Overseerr/Jellyseerr, Sonarr, and Radarr integration.'
        plugin['overview'] = 'JellyNotify adds per-user Jellyfin notifications with a bell, unread badge, notification panel, and toast alerts. Requests and download events are mapped to the Jellyfin user who requested the content; unmapped events are skipped for privacy.'
        plugin['owner'] = 'Rovaal-code'
        versions = plugin.setdefault('versions', [])
        entry = next((v for v in versions if v.get('version') == version), None)
        if entry is None:
            entry = {}
            versions.insert(0, entry)
        entry.update({
            'version': version,
            'changelog': changelog,
            'targetAbi': '10.11.0.0',
            'sourceUrl': source_url,
            'checksum': checksum,
            'timestamp': timestamp
        })
        def version_key(v):
            parts = v.get('version', '0').split('.')
            return tuple(int(p) if p.isdigit() else 0 for p in parts)
        versions.sort(key=version_key, reverse=True)
        print(f'  ✓ {version} -> {source_url}')
with open(filepath, 'w', encoding='utf-8') as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write('\n')
" "$VERSION" "$CHECKSUM" "$manifest_file"
        echo "✓ $(basename "$manifest_file") updated"
    fi
done

# ── Resumen ───────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════╗"
echo "║             Build completado             ║"
echo "╚══════════════════════════════════════════╝"
echo ""
echo "  Archivo:   $ZIP_PATH"
echo "  MD5:       $CHECKSUM"
echo "  Version:   $VERSION"
echo ""
echo "Manual Jellyfin installation:"
echo "  1. Copy $ZIP_PATH to the Jellyfin plugins directory:"
echo "     /var/lib/jellyfin/plugins/ (Linux)"
echo "     or your server data path"
echo "  2. Restart Jellyfin"
echo ""
echo "Repository manifest URL:"
echo "  https://raw.githubusercontent.com/Rovaal-code/JellyNotify/main/repository/manifest.json"
echo ""
