# Initial project audit — 2026-09-25

The starting revision contained only `licenses.json` (one newline). There were
no `Assets`, `Packages` or `ProjectSettings` directories, scenes, prefabs, C#
scripts, materials, cameras, lighting, tags, layers, input configuration or render
pipeline. No existing system was removed or replaced. The prototype left `licenses.json`
untouched; the repository owner later removed it from `main`.

## Decisions

| Area | Baseline | Milestone decision |
| --- | --- | --- |
| Unity | Not installed; no project version | Pin **6000.4.4f1**, revision `360f97ecca93` |
| Pipeline | None | Built-in RP; simple PS1 shaders, no URP migration |
| Input | None | Input System **1.19.0**, keyboard/mouse and gamepad |
| UI | None | Unity UI **2.0.0**, original bitmap-style font |
| TextMeshPro | Not installed | Bundled with Unity UI; not needed for this milestone |
| Cinemachine | Not installed | Not needed for a minimal first-person camera |
| Scene | None | `CharacterPrototype`, generated with Unity Editor APIs |
| Assets | None | Original low-poly art and original face textures, authored in Blender |
| Licensing | No local Unity activation or licensed editor session | Native Unity verification is blocked until authorized activation |
| MCP | No Unity MCP or Blender MCP tools attached | Use checked-in scripts and Blender CLI; no claim of MCP execution |

Only populated directories are created. The investigation, dialogue, save,
memory and monster systems are deliberately deferred.

## Authoritative references

- Unity release/version and Linux download:
  https://unity.com/releases/editor/whats-new/6000.4.4f1
- Input System compatibility with this editor:
  https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.inputsystem.html
- Unity Test Framework is distributed with this editor:
  https://docs.unity3d.com/6000.4/Documentation/Manual/test-framework/test-framework-introduction.html
- Blender portable Linux installation:
  https://docs.blender.org/manual/en/4.5/getting_started/installing/linux.html

See `Docs/Verification.md` for measured results, separate from planned checks.
