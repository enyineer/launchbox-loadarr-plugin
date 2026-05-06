#!/usr/bin/env bash
# Build Loadarr on macOS/Linux against a stub of the LaunchBox plugin API.
# Output is a single src/Loadarr/bin/Release/Loadarr.dll (third-party deps
# embedded via Costura.Fody) — copy that file to <LaunchBoxInstall>\Plugins\Loadarr
# on your Windows PC.
#
# Usage:
#   build/build-mac.sh                  # build plugin (builds stub if missing)
#   build/build-mac.sh --rebuild-stub   # force rebuild the LaunchBox API stub
#   build/build-mac.sh --clean          # clean bin/obj first
#   build/build-mac.sh --config Debug   # build Debug instead of Release

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

config="Release"
rebuild_stub=false
clean=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --rebuild-stub) rebuild_stub=true; shift ;;
        --clean)        clean=true; shift ;;
        --config)       config="$2"; shift 2 ;;
        -h|--help)      sed -n '2,11p' "$0"; exit 0 ;;
        *) echo "unknown arg: $1" >&2; exit 2 ;;
    esac
done

stub_proj="build/stubs/LaunchBoxStub/LaunchBoxStub.csproj"
stub_out_dir="build/stubs/launchbox-fake/Core"
stub_dll="$stub_out_dir/Unbroken.LaunchBox.Plugins.dll"
main_proj="src/Loadarr/Loadarr.csproj"
main_out_dir="src/Loadarr/bin/$config"

if $clean; then
    echo "==> Cleaning bin/obj"
    find src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
fi

if $rebuild_stub || [[ ! -f "$stub_dll" ]]; then
    echo "==> Building LaunchBox API stub"
    dotnet build "$stub_proj" -c Release --nologo -v quiet
    mkdir -p "$stub_out_dir"
    cp "build/stubs/LaunchBoxStub/bin/Release/Unbroken.LaunchBox.Plugins.dll" "$stub_dll"
fi

echo "==> Building Loadarr ($config)"
dotnet build "$main_proj" \
    -c "$config" \
    --nologo \
    -p:LaunchBoxPath="$repo_root/build/stubs/launchbox-fake"

echo
echo "Build OK."
echo "Output: $main_out_dir/Loadarr.dll"
echo "Deploy: copy '$main_out_dir/Loadarr.dll' to <LaunchBox>\\Plugins\\Loadarr\\ on Windows."
