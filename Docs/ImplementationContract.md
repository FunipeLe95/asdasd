# Character prototype — implementation contract

This is a new Unity 6000.4.4f1 project, not a migration. Use the Built-in Render
Pipeline, Input System 1.19.0 and Unity UI. No URP, Cinemachine or asset-store
dependencies. Unity/Blender MCP are not exposed in this workspace. Blender CLI is
the available authoring route; a licensed Unity Editor is required for import,
native compilation, scene generation and Play Mode.

## Scope and ownership

- `Tools/Blender`, `SourceArt`, `Assets/_Game/Art`: reproducible original art.
- `Scripts/Core`, `Scripts/Inventory`, `Scripts/Characters`: data and domain logic.
- `Scripts/Player`, `Scripts/Interaction`, `Scripts/UI`, `Scripts/Visuals`: runtime.
- `Editor`: idempotent, non-destructive asset/scene construction using Unity APIs.
- `Tests`: Edit Mode, Play Mode and engine-independent inventory checks.

All C# namespaces start with `Voron`. Runtime assembly: `Voron.Runtime`.
Editor assembly: `Voron.Editor`. No global managers or service locator.

## Shared runtime API

### Inventory (`Voron.Inventory`)

- `ItemType`: Key, Evidence, Document, Photograph, QuestItem, Consumable.
- `ItemData : ScriptableObject`: properties `Id`, `DisplayName`, `Description`,
  `Icon` (Sprite), `Type`, `Stackable`, `MaxStack`, `WorldPrefab` (GameObject).
  `Configure(string id, string displayName, string description, ItemType type,
  bool stackable = false, int maxStack = 1, Sprite icon = null,
  GameObject worldPrefab = null)` for editor construction/tests.
- `InventoryItem`: properties `Data` (ItemData), `Quantity` (int).
- `InventorySystem : MonoBehaviour`: `IReadOnlyList<InventoryItem> Items`,
  `int Capacity`, `event Action Changed`, `bool TryAdd(ItemData, int quantity = 1)`,
  `bool TryRemove(string id, int quantity = 1)`, `int Count(string id)`,
  `void Configure(int capacity)`. Adds/removes are atomic. No starting items.
- Pure C# domain `InventoryStore` with capacity and item identity, used by the
  MonoBehaviour and executable tests (no duplicate inventory implementation).

### Characters (`Voron.Characters`)

- `CharacterData : ScriptableObject`: `Id`, `DisplayName`, `Role`,
  `Greeting`, `FaceTexture` (Texture2D), optional `TalkFaceTexture` and
  `BlinkFaceTexture` (Texture2D), `BodyTint` (Color).
  `Configure(string id, string displayName, string role, string greeting,
  Texture2D faceTexture, Color bodyTint, Texture2D talkFaceTexture = null,
  Texture2D blinkFaceTexture = null)`.
- `CharacterIdentity : MonoBehaviour`: `Data`, `Configure(CharacterData)`.
- `CharacterAppearance : MonoBehaviour`: `Configure(CharacterData, Renderer face, Renderer eyes = null)`;
  use a MaterialPropertyBlock for swappable face textures, not cloned materials.
  Face performance runs in `Update`: the face renderer shows `TalkFaceTexture`
  while `FacePerformance.IsMouthOpen` is true for the current Talk progress,
  otherwise `FaceTexture`; the eyes renderer shows `BlinkFaceTexture` during
  blinks (random 2.2–5.5 s interval, ~0.11 s closed, occasional double blink,
  no spontaneous blinks after death), otherwise `FaceTexture`. A missing talk
  or blink map falls back to `FaceTexture`. Property blocks are only rewritten
  when a state changes. Exposes `Blink()`, `IsMouthOpen`, `IsBlinking`. Uses an
  assigned `CharacterAnimation` or the one on the same GameObject.
- `CharacterAnimation : MonoBehaviour`: `Configure(Animator)`, `SetSpeed(float)`,
  `PlayInspect()`, `PlayTalk()`, `PlayTurn()`, `PlayDeath()`, `IsDead`,
  `bool TryGetTalkProgress(out float normalizedTime, out float length)` (true
  only while the layer-0 state is `Talk`).
  Animator parameters: float `Speed`, triggers `Inspect`, `Talk`, `Turn`, `Death`.
  Required states: Idle, Walk, Run, Turn, Inspect, Talk, Death.
- `FacePerformance` (static): `bool IsMouthOpen(float normalizedTime, float clipLength)`.
  Speech windows are normalized Talk time [0.10, 0.44) and [0.56, 0.86). Inside a
  window, the seconds since the window start, modulo the cycle, walk the syllable
  durations {0.12, 0.07, 0.09, 0.06, 0.15, 0.08, 0.10, 0.05, 0.13, 0.09} s: even
  indices are open, odd closed. Closed outside the windows and when
  `clipLength <= 0`. The Blender previews in `Tools/Blender` use the same timing.

### Player/input (`Voron.Player`)

- `PlayerInputReader : MonoBehaviour` creates and disposes Input Actions, supports
  keyboard/mouse/gamepad. Exposes `Move`, `Look`, `LookIsPointer`, `RunHeld`,
  `CrouchHeld`, `InteractPressed`, `InventoryPressed`, `CancelPressed`,
  `FlashlightPressed`, `Navigate`, `SubmitPressed`, `UsingGamepad`.
- `PlayerController : MonoBehaviour`: `Configure(PlayerInputReader, Transform camera,
  Voron.UI.InventoryUI)`; CharacterController movement, modal input gating.
- Optional `PlayerFlashlight.Configure(PlayerInputReader, Light, InventoryUI)`.

### Interaction (`Voron.Interaction`)

- `InteractionContext`: InventorySystem `Inventory`, Action<string> `ShowMessage`.
- `IInteractable`: `string Prompt`, `bool CanInteract(InteractionContext)`,
  `void Interact(InteractionContext)`.
- `InteractionSystem : MonoBehaviour`: `Configure(Camera, PlayerInputReader,
  InventorySystem, Voron.UI.InventoryUI, Voron.UI.PrototypeHUD)`.
  First ray hit occludes objects behind it; do not raycast only interactables.
- `ItemPickup : MonoBehaviour`: `Configure(ItemData, int quantity = 1)`;
  destroy only after a successful add, guard duplicate interactions.
- `CharacterInteractable : MonoBehaviour`: `Configure(CharacterData, CharacterAnimation)`.

### UI/visuals

- `Voron.UI.InventoryUI : MonoBehaviour`: `bool IsOpen`, `event Action<bool> Toggled`,
  `Configure(InventorySystem, PlayerInputReader, Font)`. Canvas-based, can construct
  its child UI once, independent of Editor-only APIs. `SetOpen(bool)`.
- `Voron.UI.PrototypeHUD : MonoBehaviour`: `Configure(PlayerInputReader, Font)`,
  `SetPrompt(string)`, `ShowMessage(string)`; children created once if necessary.
- `Voron.Visuals.PS1CameraEffect`: Built-in RP low-res post effect with shader;
  graceful pass-through if missing, no hard dependency on URP.

## Art / editor contract

- `Assets/_Game/Art/Characters/CharacterBase.fbx`: shared generic skeleton, +Z
  forward in Unity, floor at Y=0, approximately 1.8 m tall. Separate named meshes:
  `Body`, `Head`, `Face`, `Eyes`, `Hair`, `Clothing`, `Accessories`.
- Distinct character clothing/hair may use one FBX per character with the same rig.
  Coordinate exact filenames and bone paths with the editor implementer.
- Every character FBX (`CharacterBase` plus `AlexeyVoron`, `ElenaVoron`,
  `DrIlyaMorozov`, `PoliceOfficer`) shares the same 19 deform bones under a
  single empty root `Armature` that carries the axis conversion; meshes and `Hips`
  are its children. The builder imports them as Generic with `preserveHierarchy`
  and `importAnimation` enabled, so clip paths start with `Armature/`, and with
  `KeyframeReduction` at no more than 0.05 rotation, position and scale error.
- Each character FBX contains two 24 fps takes: `Idle`, a seamless 6.0 s loop, and
  `Talk`, a 4.0 s one-shot that starts and ends in Idle's frame-0 pose. Unity names
  such as `Armature|Idle` are matched on the part after the last `|`; `__preview__`
  clips are ignored. `CharacterAnimation` starts Idle at a random phase so exhibits
  do not breathe in unison.
- Faces: original 128×128 PNGs under `Art/Textures/Faces`, no internet likenesses,
  one shared UV layout per character: `{id}_Face.png` (neutral), `{id}_FaceTalk.png`
  (mouth open) and `{id}_FaceBlink.png` (eyes closed), all point filtered. The
  `Face` renderer swaps neutral/talk; the `Eyes` renderer (the eye strip, using
  the same face material) swaps neutral/blink.
- Seven clips drive actual rig bone paths, never fake root motion.
  `Animations/Characters/Idle.anim` and `Talk.anim` are copies of the
  `CharacterBase.fbx` takes (Idle loops, Talk does not). Walk, Run, Turn, Inspect
  and Death are generated by the builder as model-space rotation offsets
  (+X right, +Y up, +Z forward), independent of bone roll.
- Per character: `Animations/Characters/{id}/Idle.anim` and `Talk.anim`, copied
  from that character's FBX takes, and `AOC_{id}.overrideController`, which
  overrides the Idle and Talk clips of `AC_Character`. Each variant's model
  Animator uses its override controller. `Character_Base`, and any variant that
  falls back to the base model, uses `AC_Character`.
- Generated assets live under normal `_Game` directories, not in Resources.
- Builder entry point: `Voron.Editor.PrototypeBuilder.Build` and menu
  `Tools/Voron/Build Character Prototype`. It creates only missing assets on first
  import. Existing edited content must not be silently overwritten.
- Scene path: `Assets/_Game/Scenes/Prototype/CharacterPrototype.unity`.
- Include `Character_Base.prefab`, four character prefab variants, item data and
  pickups, Player prefab, controller/clips, two lighting profile assets.
- Small gray-room test scene; all four NPCs are pipeline exhibits, not canonical
  story events. Never expose the story twist in in-game text.

## Verification honesty

Asset renders are Blender evidence, not Unity screenshots. A successful Roslyn
compile is not a Unity import/Play Mode pass. Do not mark the milestone complete
until a licensed Unity editor has imported the project and executed the tests.
