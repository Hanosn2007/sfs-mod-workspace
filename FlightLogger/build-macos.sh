#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sfs_root="${SFS_GAME_ROOT:-"$HOME/Library/Application Support/Steam/steamapps/common/Spaceflight Simulator"}"
mod_dir="$sfs_root/SpaceflightSimulatorGame.app/Mods/FlightLogger"

dotnet build "$repo_dir/FlightLogger.csproj" \
  -c Release \
  "/p:SFSGameRoot=$sfs_root"

mkdir -p "$mod_dir"
cp "$repo_dir/bin/Release/FlightLogger.dll" "$mod_dir/FlightLogger.dll"

echo "Installed: $mod_dir/FlightLogger.dll"
