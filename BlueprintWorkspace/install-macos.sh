#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
game_root="${SFS_GAME_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Spaceflight Simulator}"
app_dir="$game_root/SpaceflightSimulatorGame.app"
mod_dir="$app_dir/Mods/BlueprintWorkspace"

if [ ! -d "$app_dir/Saving/Settings" ]; then
  echo "Spaceflight Simulator Steam installation not found: $app_dir" >&2
  exit 1
fi
if pgrep -f '/SpaceflightSimulatorGame.app/Contents/MacOS/Spaceflight Simulator' >/dev/null; then
  echo 'Close Spaceflight Simulator after saving your progress, then run this installer again.' >&2
  exit 1
fi

dotnet build "$project_dir/client/BlueprintWorkspace.csproj" -c Release "/p:SFSGameRoot=$game_root"
mkdir -p "$mod_dir"
if [ -f "$mod_dir/BlueprintWorkspace.dll" ]; then
  mv "$mod_dir/BlueprintWorkspace.dll" "$mod_dir/BlueprintWorkspace.dll.backup.$(date +%Y%m%d-%H%M%S)"
fi
cp "$project_dir/client/bin/Release/BlueprintWorkspace.dll" "$mod_dir/BlueprintWorkspace.dll"
echo "Installed: $mod_dir/BlueprintWorkspace.dll"
echo 'Launch the game, enable Shared Blueprints in Mod Loader if needed, and open the build scene.'
