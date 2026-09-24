#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sfs_root="${SFS_GAME_ROOT:-"$HOME/Library/Application Support/Steam/steamapps/common/Spaceflight Simulator"}"
mods_dir="$sfs_root/SpaceflightSimulatorGame.app/Mods"
mod_dir="$mods_dir/BuildTools"

dotnet build "$repo_dir/BuildTools.csproj" \
  -c Release \
  "/p:SFSGameRoot=$sfs_root"

mkdir -p "$mod_dir"
cp "$repo_dir/bin/Release/BuildTools.dll" "$mod_dir/BuildTools.dll"

for old in PartEditor PartText BuildSettings; do
  if [ -d "$mods_dir/$old" ]; then
    mv "$mods_dir/$old" "$mods_dir/$old.disabled-by-buildtools"
  fi
done

echo "Installed: $mod_dir/BuildTools.dll"
