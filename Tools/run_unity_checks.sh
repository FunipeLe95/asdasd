#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
unity="${UNITY_EDITOR:-$PWD/.tools/unity/Editor/Unity}"
if [[ ! -x "$unity" ]]; then
  echo 'Set UNITY_EDITOR to an activated Unity 6000.4.4f1 executable.' >&2
  exit 1
fi
mkdir -p .artifacts/unity
rm -f .artifacts/unity/EditMode.xml .artifacts/unity/PlayMode.xml

"$unity" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod Voron.Editor.PrototypeBuilder.Build \
  -logFile "$PWD/.artifacts/unity/build-prototype.log"

for mode in EditMode PlayMode; do
  # -quit would end the editor before Unity Test Framework finishes its run.
  "$unity" -batchmode -nographics -projectPath "$PWD" -runTests \
    -testPlatform "$mode" -testFilter 'Voron.Tests' \
    -testResults "$PWD/.artifacts/unity/$mode.xml" \
    -logFile "$PWD/.artifacts/unity/$mode.log"
  python3 - ".artifacts/unity/$mode.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
if int(root.get('total', '0')) == 0 or root.get('result') != 'Passed':
    raise SystemExit(f"Unity test run failed or contained no tests: {sys.argv[1]}")
print(f"PASS {sys.argv[1]}: {root.get('passed')} tests")
PY
done

echo 'Native tests passed. Also verify the rendered Game view with a graphics device.'
