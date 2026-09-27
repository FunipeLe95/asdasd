"""Create stable metadata only for new source assets; never overwrite existing GUIDs."""

from __future__ import annotations

import argparse
import re
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NAMESPACE = uuid.uuid5(uuid.NAMESPACE_URL, "urn:voron:character-prototype:source-assets")


def metadata(path: Path) -> str:
    relative = path.relative_to(ROOT).as_posix()
    guid = uuid.uuid5(NAMESPACE, relative).hex
    text = f"fileFormatVersion: 2\nguid: {guid}\n"
    if path.is_dir():
        return text + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    if path.suffix == ".cs":
        text += ("MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
                 "  executionOrder: 0\n  icon: {instanceID: 0}\n")
    elif path.suffix == ".asmdef":
        text += "AssemblyDefinitionImporter:\n  externalObjects: {}\n"
    elif path.suffix == ".shader":
        text += ("ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n"
                 "  nonModifiableTextures: []\n")
    else:
        # Native importers populate their versioned defaults on the first Unity import.
        return text
    return text + "  userData:\n  assetBundleName:\n  assetBundleVariant:\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    assets = ROOT / "Assets"
    paths = sorted(path for path in assets.rglob("*") if path.suffix != ".meta" and not path.name.startswith("."))
    changed = 0
    guids: dict[str, Path] = {}
    failures = []
    for path in paths:
        meta = path.with_name(path.name + ".meta")
        if not meta.exists():
            if args.check:
                failures.append(f"Missing {meta.relative_to(ROOT)}")
                continue
            meta.write_text(metadata(path))
            changed += 1
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.MULTILINE)
        if not match:
            failures.append(f"Invalid GUID in {meta.relative_to(ROOT)}")
        elif match[1] in guids:
            failures.append(f"Duplicate GUID in {meta.relative_to(ROOT)} and {guids[match[1]].relative_to(ROOT)}")
        else:
            guids[match[1]] = meta
    for meta in assets.rglob("*.meta"):
        if not meta.with_suffix("").exists():
            failures.append(f"Orphaned {meta.relative_to(ROOT)}")
    if failures:
        raise SystemExit("\n".join(failures))
    print(f"Asset metadata valid: {len(guids)} unique GUIDs; {changed} files created.")


if __name__ == "__main__":
    main()
