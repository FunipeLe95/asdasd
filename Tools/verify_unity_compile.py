"""Compile against the installed editor's real APIs; never a substitute for Play Mode."""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import tarfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".artifacts" / "api-compile"


def fetch_input_system() -> Path:
    version = json.loads((ROOT / "Packages/manifest.json").read_text())["dependencies"]["com.unity.inputsystem"]
    cache = ROOT / ".tools" / "packages" / f"com.unity.inputsystem@{version}"
    if (cache / "package.json").exists():
        if json.loads((cache / "package.json").read_text())["version"] != version:
            raise RuntimeError("Cached Input System version mismatch")
        return cache

    with urllib.request.urlopen("https://packages.unity.com/com.unity.inputsystem", timeout=60) as response:
        dist = json.load(response)["versions"][version]["dist"]
    archive = ROOT / ".tools" / "downloads" / f"inputsystem-{version}.tgz"
    archive.parent.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve(dist["tarball"], archive)
    with archive.open("rb") as stream:
        if hashlib.file_digest(stream, "sha1").hexdigest() != dist["shasum"]:
            raise RuntimeError("Input System package integrity mismatch")
    cache.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive, "r:gz") as package:
        members = package.getmembers()
        for member in members:
            path = Path(member.name)
            if not path.parts or path.parts[0] != "package" or ".." in path.parts:
                raise RuntimeError("Unexpected package archive path")
            member.name = str(Path(*path.parts[1:]))
        package.extractall(cache, members=members, filter="data")
    return cache


def sources(directory: Path) -> list[Path]:
    nested = {path.parent for path in directory.rglob("*.asmdef") if path.parent != directory}
    return sorted(path for path in directory.rglob("*.cs") if not any(parent in path.parents for parent in nested))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", type=Path, default=ROOT / ".tools/unity/Editor")
    args = parser.parse_args()
    data = args.editor.resolve() / "Data"
    compiler = data / "DotNetSdkRoslyn/csc.dll"
    dotnet = data / "NetCoreRuntime/dotnet"
    if not compiler.exists() or not dotnet.exists():
        raise SystemExit("Install Unity 6000.4.4f1 or pass --editor /path/to/Editor.")

    OUT.mkdir(parents=True, exist_ok=True)
    builtins = data / "Resources/PackageManager/BuiltInPackages"
    input_system = fetch_input_system()
    manifest = json.loads((ROOT / "Packages/manifest.json").read_text())["dependencies"]
    for package in ("com.unity.ugui", "com.unity.test-framework"):
        installed = json.loads((builtins / package / "package.json").read_text())["version"]
        if installed != manifest[package]:
            raise SystemExit(f"{package} version mismatch: editor has {installed}, project requires {manifest[package]}")
    references = [data / "NetStandard/ref/2.1.0/netstandard.dll"]
    references += sorted((data / "NetStandard/compat/2.1.0/shims/netstandard").glob("*.dll"))
    references += sorted((data / "NetStandard/compat/2.1.0/shims/netfx").glob("*.dll"))
    references += sorted((data / "Managed/UnityEngine").glob("*.dll"))
    references += sorted(path for path in (data / "Managed").glob("UnityEditor*.dll") if path.name != "UnityEditor.dll")
    references = [path for path in references if path.exists()]
    assemblies = {path.stem: path for path in references}
    defines = ["UNITY_5_3_OR_NEWER", "UNITY_64", "UNITY_STANDALONE", "UNITY_STANDALONE_LINUX",
               "ENABLE_INPUT_SYSTEM", "NET_STANDARD_2_1", "NET_STANDARD", "ENABLE_MONO"]
    for year in range(2017, 2024):
        defines += [f"UNITY_{year}_{minor}_OR_NEWER" for minor in range(1, 5)]
    defines += ["UNITY_6000_4", "UNITY_6000", "UNITY_6000_OR_NEWER"]
    defines += [f"UNITY_6000_{minor}_OR_NEWER" for minor in range(5)]

    def compile_assembly(name: str, files: list[Path], dependencies=(), extra_defines=(), project=False) -> Path:
        if not files:
            raise RuntimeError(f"No sources found for {name}")
        output = OUT / f"{name}.dll"
        rsp = OUT / f"{name}.rsp"
        flags = ["-nologo", "-target:library", "-nostdlib+", "-langversion:9.0", "-unsafe+",
                 f'-out:"{output}"', f"-define:{';'.join(defines + list(extra_defines))}"]
        if project:
            flags.append("-warnaserror+")
        flags += [f'-reference:"{path}"' for path in references + list(dependencies)]
        flags += [f'"{path}"' for path in files]
        rsp.write_text("\n".join(flags) + "\n")
        result = subprocess.run([str(dotnet), str(compiler), f"@{rsp}"], text=True, capture_output=True)
        log = OUT / f"{name}.log"
        log.write_text(result.stdout + result.stderr)
        if result.returncode:
            diagnostics = (result.stdout + result.stderr).splitlines()
            print("\n".join(diagnostics[:30]))
            if len(diagnostics) > 30:
                print(f"{len(diagnostics) - 30} further diagnostic lines retained in {log.relative_to(ROOT)}")
            raise SystemExit(f"FAIL {name}; details: {log.relative_to(ROOT)}")
        warnings = sum("warning " in line for line in result.stdout.splitlines())
        print(f"PASS {name}: {len(files)} C# sources ({warnings} upstream warnings)")
        assemblies[name] = output
        return output

    def compile_project(directory: str, extra_dependencies=(), extra_defines=()) -> None:
        folder = ROOT / "Assets/_Game" / directory
        definitions = list(folder.glob("*.asmdef"))
        if len(definitions) != 1:
            raise SystemExit(f"Expected one assembly definition in {folder.relative_to(ROOT)}")
        definition = json.loads(definitions[0].read_text())
        declared_references = definition.get("references", [])
        unresolved = [name for name in declared_references if name not in assemblies]
        if unresolved:
            raise SystemExit(f"Unresolved references in {definitions[0].relative_to(ROOT)}: {', '.join(unresolved)}")
        dependencies = [assemblies[name] for name in declared_references] + list(extra_dependencies)
        compile_assembly(definition["name"], sources(folder), dependencies, extra_defines, project=True)

    bridge = compile_assembly("Unity.InternalAPIEngineBridge.004",
                              sources(builtins / "com.unity.ugui/Runtime/InternalBridge"))
    ui = compile_assembly("UnityEngine.UI", sources(builtins / "com.unity.ugui/Runtime/UGUI"),
                          [bridge], ["PACKAGE_PHYSICS", "PACKAGE_ANIMATION", "PACKAGE_UITOOLKIT", "PACKAGE_INPUTSYSTEM"])
    input_defines = ["UNITY_INPUT_SYSTEM_ENABLE_UI", "UNITY_INPUT_SYSTEM_ENABLE_PHYSICS",
                     "UNITY_INPUT_SYSTEM_PLATFORM_SCROLL_DELTA", "UNITY_INPUT_SYSTEM_INPUT_MODULE_SCROLL_DELTA",
                     "UNITY_INPUT_SYSTEM_INPUT_MODULE_NAVIGATION_DEVICE_TYPE",
                     "UNITY_INPUT_SYSTEM_SENDPOINTERHOVERTOPARENT", "UNITY_INPUT_SYSTEM_PLATFORM_POLLING_FREQUENCY",
                     "UNITY_INPUTSYSTEM_SUPPORTS_MOUSE_SCRIPT_EVENTS"]
    compile_assembly("Unity.InputSystem", sources(input_system / "InputSystem"), [ui], input_defines)
    compile_project("Scripts")
    compile_project("Editor", extra_defines=["UNITY_EDITOR", "UNITY_EDITOR_LINUX"])
    nunit = builtins / "com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll"
    test_runner = compile_assembly("UnityEngine.TestRunner",
                                   sources(builtins / "com.unity.test-framework/UnityEngine.TestRunner"),
                                   [nunit], ["UNITY_TESTS_FRAMEWORK", "UNITY_INCLUDE_TESTS"])
    compile_project("Tests/EditMode", [nunit, test_runner],
                    ["UNITY_EDITOR", "UNITY_EDITOR_LINUX", "UNITY_INCLUDE_TESTS"])
    compile_project("Tests/PlayMode", [nunit, test_runner], ["UNITY_INCLUDE_TESTS"])
    print("API compilation passed. Unity asset import, shader compilation and Play Mode were NOT executed.")


if __name__ == "__main__":
    main()
