#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  echo 'Use Blender 4.5 LTS and Unity Hub 6000.4.4f1 on this platform.' >&2
  exit 1
fi

mkdir -p .tools/downloads
blender_version=4.5.3
blender_archive="blender-${blender_version}-linux-x64.tar.xz"
blender_base=https://download.blender.org/release/Blender4.5
if [[ ! -x .tools/blender/blender ]]; then
  curl --fail --location --retry 3 --silent --show-error \
    "$blender_base/$blender_archive" -o ".tools/downloads/$blender_archive"
  curl --fail --location --retry 3 --silent --show-error \
    "$blender_base/blender-${blender_version}.sha256" -o .tools/downloads/blender.sha256
  (cd .tools/downloads && grep " $blender_archive\$" blender.sha256 | sha256sum --check --strict -)
  mkdir -p .tools/blender
  tar -xJf ".tools/downloads/$blender_archive" --strip-components=1 -C .tools/blender
  rm ".tools/downloads/$blender_archive"
fi
if ldd .tools/blender/blender | grep -qE 'lib(SM|ICE)\.so.*not found'; then
  if ! command -v sudo >/dev/null || ! sudo -n true 2>/dev/null; then
    echo 'Blender requires libsm6 and libice6; this Linux host must provide them.' >&2
    exit 1
  fi
  sudo apt-get update -qq
  sudo apt-get install -y --no-install-recommends libsm6 libice6
fi
.tools/blender/blender --version | sed -n '1,2p'

if [[ "${1:-}" == --with-unity && ! -x .tools/unity/Editor/Unity ]]; then
  unity_archive=.tools/downloads/Unity-6000.4.4f1.tar.xz
  unity_url=https://download.unity3d.com/download_unity/360f97ecca93/LinuxEditorInstaller/Unity-6000.4.4f1.tar.xz
  curl --fail --location --retry 3 --silent --show-error "$unity_url" -o "$unity_archive"
  # Publisher integrity from Unity's release API, not a license or activation key.
  python3 - "$unity_archive" <<'PY'
import base64
import hashlib
import sys
with open(sys.argv[1], 'rb') as stream:
    digest = hashlib.file_digest(stream, 'md5').digest()
if base64.b64encode(digest).decode() != '3VB1YY2sPK1WHI6HAgcJTA==':
    raise SystemExit('Unity archive integrity check failed')
PY
  mkdir -p .tools/unity
  tar -xJf "$unity_archive" -C .tools/unity
  rm "$unity_archive"
fi

echo 'Tools ready. Unity activation is never automated by this script.'
