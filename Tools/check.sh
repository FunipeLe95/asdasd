#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
python3 Tools/sync_asset_meta.py --check
python3 -m py_compile Tools/Blender/pixel_art.py Tools/Blender/character_models.py \
  Tools/Blender/character_textures.py Tools/Blender/character_animations.py \
  Tools/Blender/generate_ps1_characters.py Tools/verify_unity_compile.py
mise exec -- dotnet run --project Tools/DomainTests/DomainTests.csproj --configuration Release
python3 Tools/verify_unity_compile.py
git diff --check
echo 'Static/source checks passed. Run Tools/run_unity_checks.sh with an activated editor for native verification.'
