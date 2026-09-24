#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sfs_root="${SFS_GAME_ROOT:-"$HOME/Library/Application Support/Steam/steamapps/common/Spaceflight Simulator"}"
mod_dir="$sfs_root/SpaceflightSimulatorGame.app/Mods/UITools"
disabled_dir="$sfs_root/SpaceflightSimulatorGame.app/Mods/UITools.disabled-macos26"

dotnet build "$repo_dir/UITools.csproj" \
  -c Release \
  "/p:SFSGameRoot=$sfs_root"

mkdir -p "$mod_dir"
cp "$repo_dir/bin/Release/UITools.dll" "$mod_dir/UITools.dll"
cp "$repo_dir/bin/Release/UITools.xml" "$mod_dir/UITools.xml" 2>/dev/null || true
rm -rf "$disabled_dir"

echo "Installed: $mod_dir/UITools.dll"
