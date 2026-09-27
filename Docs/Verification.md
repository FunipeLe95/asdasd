# Verification record

This record distinguishes executable evidence from source-level preparation.

## Environment

- Target editor: **Unity 6000.4.4f1**, revision `360f97ecca93`.
- Official Linux editor installed under `.tools/unity/Editor`.
- Official Blender **4.5.3 LTS** installed and executed.
- .NET SDK **8.0.408** is declared in `mise.toml`.
- The fresh repository contained no Unity project; see [ProjectAudit](ProjectAudit.md).
- Unity/Blender MCP endpoints were not attached. No MCP execution is claimed.

## Confirmed checks

- `dotnet run --project Tools/DomainTests/DomainTests.csproj --configuration Release`:
  **17 passing tests**, including a seeded **10,000-transaction** add/remove exercise.
  Tests compile the same `InventoryStore.cs` used by the game, not a reimplementation.
  Covered: capacity, stack filling, atomic overflow/removal, slot reclamation,
  event count/state, invalid requests, duplicate IDs, ordinal identity, integer limits.
- The rebuilt art pipeline (Blender 4.5.3) generated five rigged FBXs, the source `.blend`,
  128×128 face maps, 256×256 clothing atlases and 32×32 item icons. All five passed the
  component, skeleton, UV, weight, triangle-budget (2,952–3,998) and height checks, the
  geometry checks (ground contact, no hidden torso skin, unobstructed faces, 108 scalp rays
  for crown hair) and the FBX round-trip import with a pose-deformation smoke test.
- Fresh front, three-quarter, profile, back, stride and face review renders were
  inspected; the published copies are in `Docs/Media`. This evidence does **not** establish
  Unity import, materials, animation or rendering correctness.
- Idle and Talk animations (Blender 4.5.3): all five characters passed the clip checks — Idle
  closes its 6.0 s loop and Talk starts and ends in Idle's first pose (within 0.1 mm and
  0.05°), feet stay within 1 mm of rest, every IK hold is reached within 4 mm, and Alexey's
  pocket hand stays behind the coat vertex by vertex. The exported FBXs contain takes named
  exactly `Idle` and `Talk` (checked with Blender's FBX parser); after re-import they match
  the source joints within **0.73 mm and 0.06°**.
- Fresh Idle and Talk preview frames, face close-ups with the talk/blink maps and hand-hold
  close-ups from three angles were inspected; `Docs/Media/CharacterIdle.gif` and
  `CharacterTalk.gif` are the published previews. They are Blender renders: Unity clip import,
  Animator transitions and the in-game mouth/blink swaps are **not** verified.
- `FacePerformance` (Unity) and the Blender preview timing were compared outside the repository
  on every 24 fps Talk frame and on 40,001 samples: no mismatches.

### Final integration checks — 2026-09-25

`bash Tools/check.sh` completed with exit code **0** after integration fixes.
The local transcript is `.artifacts/source-checks.log` (not committed).

| Check | Confirmed result |
| --- | --- |
| Asset metadata | 93 unique GUIDs; no missing, duplicate or orphaned sidecars |
| Inventory domain suite | 17 passing tests, including 10,000 seeded transactions |
| `Voron.Runtime` | 26 C# sources compiled; zero warnings |
| `Voron.Editor` | 6 C# sources compiled; zero warnings |
| `Voron.EditModeTests` | 3 C# sources compiled; zero warnings; tests **not executed** |
| `Voron.PlayModeTests` | 1 C# source compiled; zero warnings; tests **not executed** |
| Python / shell | Python syntax and `bash -n` checks passed |
| Source art paths | All five model, face (neutral/talk/blink) and clothing sets exist; models contain 2,952–3,998 triangles |
| Full repository diff | `git diff --check` passed, including the newly added files |

API compilation uses the actual installed editor assemblies and declared project
`.asmdef` references, with project warnings treated as errors. The isolated
Input System 1.19.0 package compilation emits **10 upstream warnings**; these are
not project warnings or results from Unity Console. A temporary-copy regression
check confirmed that the verifier rejects the former invalid `Unity.ugui` reference.

Integration fixes synchronized the shared rig path to `CharacterBase.fbx`, replaced
the deprecated mesh-ID API without changing triangle-budget coverage, corrected
the Editor's UI assembly reference and removed generated metadata whitespace
without changing GUIDs. Player fixes prevent Escape from being consumed twice
when closing inventory and include the rising capsule center in standing clearance.
The native suites now contain **25 Edit Mode tests and 10 Play Mode scenarios**,
including the player regressions and the animation, import-tolerance, override-controller,
face-map, mouth and blink checks; only their compilation is confirmed.

Five isolated test-double cases checked the native runner's control flow: failed
builder, missing fresh results, empty suite, failing suite and passing suite. It
rejects the first four and clears previous XML before starting the builder. These
are shell-runner checks, **not native Unity test results**.

## Blocking native check

Repeated the actual editor entry point on **2026-09-25**:

```sh
bash Tools/run_unity_checks.sh
```

**Exit code: 198**, before the builder or either test suite could run. Sanitized
diagnostic from `.artifacts/unity/build-prototype.log`:

```text
No valid Unity Editor license found. Please activate your license.
```

No activation bypass, borrowed license, account login or credential request was
attempted. The full local log is ignored because it contains machine/session
identifiers. Native editor compilation, asset generation, shader compilation,
Edit Mode tests, Play Mode tests and an executable build are **not verified**.
`CharacterPrototype.unity`, `EditMode.xml` and `PlayMode.xml` have not been generated.
The first playable milestone remains **open**.

## Native acceptance checklist — still pending

Run `Tools/run_unity_checks.sh` using an activated editor. It must stop if the
builder or either test suite fails; test result XML must contain actual passing
tests, not an empty run. Then verify in a normal rendered Game view:

| Check | Acceptance |
| --- | --- |
| Import / Console | No missing scripts, C# errors, failed asset imports or shader errors |
| Scene | Generated `CharacterPrototype.unity` opens and is included in Build Settings |
| Player | WASD and left stick move with collision; speed is diagonal-safe |
| Crouch | Low ceilings prevent standing; clearing the ceiling allows the full capsule to expand |
| Camera | Mouse and right stick look; pitch is clamped; opening inventory blocks look |
| Cursor / focus | Escape closes inventory and immediately restores movement; outside inventory it releases the cursor; focus loss cannot move player |
| NPCs | Four distinct figures, intact textures, floor-aligned scale, no T-pose |
| Animation | Idle breathes and shifts weight without foot sliding and exhibits are out of phase; police exhibit walks; Talk gestures, flaps the mouth and blends back to Idle; eyes blink |
| Interaction | First ray hit blocks targets behind a wall; prompt changes input family |
| Pickup | Each item can be collected once, disappears and increments inventory once |
| Capacity | Failed pickup leaves the object available; no partial additions |
| Inventory | Names/descriptions/icons visible; keyboard/gamepad navigation and close work |
| Flashlight | F / west gamepad button toggle without firing during inventory interaction |
| Rendering | Pixelated world, readable crisp UI, mild dithering/noise, no pink materials |
| Lighting | Neutral and Horror Night profiles preserve silhouettes and readable pickups |
| Persistence | Reopen scene and instantiate prefabs: all configured references survive |
| Builder rerun | No duplicate actors, duplicate systems or overwritten user-edited content |
| Standalone | Build and run a supported desktop target; hidden post shader is included |

Capture fresh Game view screenshots plus actual pickup/inventory/gamepad interaction
evidence before marking the milestone complete. Do not substitute the Blender render
for these pending checks.
