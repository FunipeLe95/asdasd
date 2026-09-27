#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mise install
bash Tools/setup.sh --with-unity
