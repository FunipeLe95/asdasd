using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Voron.Characters;
using Voron.Interaction;
using Voron.Inventory;
using Voron.Player;
using Voron.UI;

namespace Voron.Tests.PlayMode
{
    public sealed class CharacterPrototypePlayModeTests
    {
        private const string ScenePath = "Assets/_Game/Scenes/Prototype/CharacterPrototype.unity";
        private const string BuildHint = "Run Unity menu Tools/Voron/Build Character Prototype before PlayMode tests.";
        private static readonly int MainTexture = Shader.PropertyToID("_MainTex");

        private readonly List<ItemData> temporaryItems = new List<ItemData>();
        private readonly List<GameObject> temporaryObjects = new List<GameObject>();
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;
        private PlayerController player;
        private PlayerInputReader input;
        private Camera playerCamera;
        private InventorySystem inventory;
        private InventoryUI inventoryUI;
        private InteractionSystem interaction;

        [UnitySetUp]
        public IEnumerator LoadPrototypeScene()
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(ScenePath);
            Assert.That(buildIndex, Is.GreaterThanOrEqualTo(0), BuildHint);

            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            gamepad = InputSystem.AddDevice<Gamepad>();

            AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "Unity could not load CharacterPrototype from Build Settings.");
            yield return load;
            yield return null;

            PlayerController[] players = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PlayerController>(true))
                .ToArray();
            Assert.That(players, Has.Length.EqualTo(1), "CharacterPrototype must contain exactly one configured Player.");

            player = players[0];
            input = player.GetComponent<PlayerInputReader>();
            playerCamera = player.GetComponentInChildren<Camera>(true);
            inventory = player.GetComponent<InventorySystem>();
            inventoryUI = player.GetComponentInChildren<InventoryUI>(true);
            interaction = player.GetComponent<InteractionSystem>();

            Assert.That(input, Is.Not.Null);
            Assert.That(playerCamera, Is.Not.Null);
            Assert.That(inventory, Is.Not.Null);
            Assert.That(inventoryUI, Is.Not.Null);
            Assert.That(interaction, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator CleanupTestObjectsAndDevices()
        {
            foreach (GameObject instance in temporaryObjects)
            {
                if (instance != null)
                    Object.Destroy(instance);
            }

            if (inventory != null)
            {
                foreach (ItemData item in temporaryItems)
                {
                    if (item != null)
                    {
                        int quantity = inventory.Count(item.Id);
                        if (quantity > 0)
                            inventory.TryRemove(item.Id, quantity);
                    }
                }
            }

            foreach (ItemData item in temporaryItems)
            {
                if (item != null)
                    Object.Destroy(item);
            }

            RemoveDevice(gamepad);
            RemoveDevice(mouse);
            RemoveDevice(keyboard);
            yield return null;

            temporaryItems.Clear();
            temporaryObjects.Clear();
            gamepad = null;
            mouse = null;
            keyboard = null;
        }

        [UnityTest]
        public IEnumerator SceneShowsFourIdentifiedAnimatedCharacters()
        {
            CharacterIdentity[] identities = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterIdentity>(true))
                .ToArray();
            Assert.That(identities, Has.Length.EqualTo(4));
            Assert.That(identities.All(identity => identity.Data != null), Is.True);
            CollectionAssert.AreEquivalent(
                new[] { "alexey_voron", "elena_voron", "dr_ilya_morozov", "police_officer" },
                identities.Select(identity => identity.Data.Id));

            Animator[] animators = identities
                .Select(identity => identity.GetComponentInChildren<Animator>(true))
                .ToArray();
            Assert.That(animators.All(animator => animator != null && animator.isActiveAndEnabled), Is.True);
            Assert.That(animators.All(animator => animator.runtimeAnimatorController != null && animator.isInitialized), Is.True);

            yield return new WaitForSeconds(0.35f);
            for (int i = 0; i < identities.Length; i++)
            {
                Animator animator = animators[i];
                string expectedState = identities[i].Data.Id == "police_officer" ? "Walk" : "Idle";
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(expectedState), Is.True,
                    identities[i].Data.DisplayName + " should be in its expected " + expectedState + " state.");
                Assert.That(animator.GetCurrentAnimatorClipInfo(0).Length, Is.GreaterThan(0),
                    identities[i].Data.DisplayName + " has no playing animation clip.");

                Dictionary<Transform, Quaternion> pose = CaptureLocalRotations(animator);
                yield return new WaitForSeconds(0.18f);
                bool movedBone = pose.Any(entry => entry.Key != null && Quaternion.Angle(entry.Key.localRotation, entry.Value) > 0.02f);
                Assert.That(movedBone, Is.True, identities[i].Data.DisplayName + " rig stayed in a static/T-pose.");
            }
        }

        [UnityTest]
        public IEnumerator TalkingNpcAlternatesOpenAndClosedMouthFaceMaps()
        {
            CharacterIdentity identity = FindCharacter("elena_voron");
            CharacterAppearance appearance = identity.GetComponent<CharacterAppearance>();
            CharacterAnimation animation = identity.GetComponent<CharacterAnimation>();
            Renderer face = FindRenderer(identity, "Face");
            CharacterData data = identity.Data;
            Assert.That(appearance, Is.Not.Null);
            Assert.That(animation, Is.Not.Null);
            Assert.That(data.TalkFaceTexture, Is.Not.Null, data.DisplayName + " needs a talk face map.");

            var properties = new MaterialPropertyBlock();
            bool reachedTalk = false;
            bool sawOpen = false;
            bool sawClosedAfterOpen = false;
            animation.PlayTalk();
            float deadline = Time.time + 2.5f;
            while (Time.time < deadline && !(sawOpen && sawClosedAfterOpen))
            {
                yield return null;
                if (animation.TryGetTalkProgress(out _, out float length))
                {
                    reachedTalk = true;
                    Assert.That(length, Is.EqualTo(4f).Within(0.05f), "Talk should play the 4 s Blender take.");
                }

                Texture shown = ShownTexture(face, properties);
                if (appearance.IsMouthOpen)
                {
                    sawOpen = true;
                    Assert.That(shown, Is.EqualTo(data.TalkFaceTexture), "An open mouth must show the talk face map.");
                }
                else
                {
                    sawClosedAfterOpen |= sawOpen;
                    Assert.That(shown, Is.EqualTo(data.FaceTexture), "A closed mouth must show the neutral face map.");
                }
            }

            Assert.That(reachedTalk, Is.True, "PlayTalk should enter the Talk state.");
            Assert.That(sawOpen, Is.True, "The mouth never opened while talking.");
            Assert.That(sawClosedAfterOpen, Is.True, "The mouth never closed again while talking.");
        }

        [UnityTest]
        public IEnumerator BlinkSwapsTheEyesToTheBlinkFaceMapAndBack()
        {
            CharacterIdentity identity = FindCharacter("dr_ilya_morozov");
            CharacterAppearance appearance = identity.GetComponent<CharacterAppearance>();
            Renderer eyes = FindRenderer(identity, "Eyes");
            CharacterData data = identity.Data;
            Assert.That(appearance, Is.Not.Null);
            Assert.That(data.BlinkFaceTexture, Is.Not.Null, data.DisplayName + " needs a blink face map.");

            var properties = new MaterialPropertyBlock();
            yield return null;
            appearance.Blink();
            Assert.That(appearance.IsBlinking, Is.True);
            Assert.That(ShownTexture(eyes, properties), Is.EqualTo(data.BlinkFaceTexture), "Closed eyes must show the blink face map.");

            float deadline = Time.time + 0.5f;
            while (appearance.IsBlinking && Time.time < deadline)
                yield return null;

            Assert.That(appearance.IsBlinking, Is.False, "A blink should reopen the eyes within half a second.");
            Assert.That(ShownTexture(eyes, properties), Is.EqualTo(data.FaceTexture), "Open eyes must show the neutral face map.");
        }

        [UnityTest]
        public IEnumerator KeyboardMovementAndLookWorkButAreGatedByInventory()
        {
            Vector3 startPosition = player.transform.position;
            Quaternion startYaw = player.transform.rotation;
            Quaternion startPitch = playerCamera.transform.localRotation;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            for (int frame = 0; frame < 8; frame++)
                yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;

            Vector3 planarMovement = Vector3.ProjectOnPlane(player.transform.position - startPosition, Vector3.up);
            Assert.That(planarMovement.magnitude, Is.GreaterThan(0.05f), "W should move the Player forward.");

            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(20f, 10f) });
            yield return null;
            Assert.That(Quaternion.Angle(startYaw, player.transform.rotation), Is.GreaterThan(0.2f), "Mouse X should rotate the Player.");
            Assert.That(Quaternion.Angle(startPitch, playerCamera.transform.localRotation), Is.GreaterThan(0.2f), "Mouse Y should pitch the Camera.");

            inventoryUI.SetOpen(true);
            Assert.That(player.IsMovementSuppressed, Is.True);
            Vector3 modalPosition = player.transform.position;
            Quaternion modalYaw = player.transform.rotation;
            Quaternion modalPitch = playerCamera.transform.localRotation;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(20f, 10f) });
            for (int frame = 0; frame < 4; frame++)
                yield return null;

            Assert.That(input.Move.y, Is.GreaterThan(0.5f), "The test must keep real movement input active while the modal is open.");
            Assert.That(Vector3.Distance(modalPosition, player.transform.position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(modalYaw, player.transform.rotation), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(modalPitch, playerCamera.transform.localRotation), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator EscapeClosesInventoryAndImmediatelyRestoresMovement()
        {
            inventoryUI.SetOpen(true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return null;

            Assert.That(inventoryUI.IsOpen, Is.False);
            Assert.That(player.IsCursorReleased, Is.False, "Closing the inventory must not also release the gameplay cursor.");
            Assert.That(player.IsMovementSuppressed, Is.False);

            Vector3 startPosition = player.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(0.15f);
            Assert.That(Vector3.ProjectOnPlane(player.transform.position - startPosition, Vector3.up).magnitude,
                Is.GreaterThan(0.05f), "Movement should resume without an extra mouse click.");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return null;
            Assert.That(player.IsCursorReleased, Is.True, "Escape outside the inventory should still release the cursor.");
            Assert.That(player.IsMovementSuppressed, Is.True);
        }

        [UnityTest]
        public IEnumerator LowCeilingBlocksStandingUntilTheFullCapsuleHasRoom()
        {
            CharacterController capsule = player.GetComponent<CharacterController>();
            float standingHeight = capsule.height;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return new WaitForSeconds(0.3f);

            float crouchedHeight = capsule.height;
            Assert.That(crouchedHeight, Is.LessThan(standingHeight - 0.2f));
            Vector3 bottom = player.transform.TransformPoint(capsule.center - Vector3.up * (crouchedHeight * 0.5f));
            float ceilingHeight = Mathf.Lerp(crouchedHeight, standingHeight, 0.75f);
            GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Standing Clearance Test Ceiling";
            ceiling.transform.position = bottom + Vector3.up * (ceilingHeight + 0.1f);
            ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
            temporaryObjects.Add(ceiling);
            Physics.SyncTransforms();

            Assert.That(player.CanStandUp(), Is.False, "Clearance must account for the capsule center rising as it expands.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.3f);
            Assert.That(capsule.height, Is.EqualTo(crouchedHeight).Within(0.01f));

            ceiling.GetComponent<Collider>().enabled = false;
            Physics.SyncTransforms();
            Assert.That(player.CanStandUp(), Is.True);
            yield return new WaitForSeconds(0.3f);
            Assert.That(capsule.height, Is.EqualTo(standingHeight).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator GamepadOpensInventoryAndMovesSelection()
        {
            ItemData[] items = new ItemData[5];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = CreateItem("playmode_navigation_" + i, "Navigation Item " + i);
                Assert.That(inventory.TryAdd(items[i]), Is.True);
            }

            Assert.That(inventoryUI.IsOpen, Is.False);
            InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.North));
            yield return null;
            Assert.That(inventoryUI.IsOpen, Is.True, "Gamepad North should toggle the inventory open.");
            Assert.That(inventoryUI.SelectedItem, Is.SameAs(items[0]));

            InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.DpadDown));
            yield return null;
            Assert.That(input.UsingGamepad, Is.True);
            Assert.That(input.Navigate.y, Is.LessThan(-0.5f), "The open inventory should receive D-pad navigation through the public input seam.");
            Assert.That(inventoryUI.SelectedIndex, Is.EqualTo(4));
            Assert.That(inventoryUI.SelectedItem, Is.SameAs(items[4]));
        }

        [UnityTest]
        public IEnumerator RaycastPickupAddsToInventoryOnce()
        {
            inventory.Configure(2);
            ItemData evidence = CreateItem("playmode_pickup", "Test Evidence");
            ItemPickup pickup = CreatePickup(evidence);

            interaction.ScanForInteractable();
            Assert.That(interaction.CurrentTarget, Is.SameAs(pickup));
            Assert.That(interaction.TryInteractCurrent(), Is.True);
            pickup.Interact(interaction.Context);

            Assert.That(inventory.Count(evidence.Id), Is.EqualTo(1));
            Assert.That(inventory.Items.Count, Is.EqualTo(1));
            yield return null;
            Assert.That(pickup == null, Is.True, "A successful pickup should be destroyed.");
        }

        [Test]
        public void FullInventoryKeepsPickupAndExistingContents()
        {
            inventory.Configure(1);
            ItemData existing = CreateItem("playmode_existing", "Existing Item");
            ItemData offered = CreateItem("playmode_full", "Unstored Evidence");
            Assert.That(inventory.TryAdd(existing), Is.True);
            ItemPickup pickup = CreatePickup(offered);

            interaction.ScanForInteractable();
            Assert.That(interaction.CurrentTarget, Is.SameAs(pickup));
            Assert.That(interaction.TryInteractCurrent(), Is.True);
            Assert.That(interaction.TryInteractCurrent(), Is.True, "A failed add should leave the pickup interactable for retry.");

            Assert.That(inventory.Count(existing.Id), Is.EqualTo(1));
            Assert.That(inventory.Count(offered.Id), Is.Zero);
            Assert.That(inventory.Items.Count, Is.EqualTo(1));
            Assert.That(pickup != null, Is.True, "A failed add must not destroy the pickup.");
        }

        [UnityTest]
        public IEnumerator BlockingColliderOccludesInteractableBehindIt()
        {
            Vector3 direction = playerCamera.transform.forward;
            Vector3 origin = playerCamera.transform.position;

            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "Interaction Test Target";
            target.transform.SetPositionAndRotation(origin + direction * 1.8f, Quaternion.LookRotation(direction, Vector3.up));
            target.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            PrototypeInteractableProbe probe = target.AddComponent<PrototypeInteractableProbe>();
            temporaryObjects.Add(target);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Interaction Test Occluder";
            wall.transform.SetPositionAndRotation(origin + direction * 0.9f, Quaternion.LookRotation(direction, Vector3.up));
            wall.transform.localScale = new Vector3(0.8f, 0.8f, 0.12f);
            Collider wallCollider = wall.GetComponent<Collider>();
            temporaryObjects.Add(wall);

            Physics.SyncTransforms();
            interaction.ScanForInteractable();
            Assert.That(interaction.CurrentTarget, Is.Null, "The first non-interactable ray hit must block objects behind it.");

            wallCollider.enabled = false;
            Physics.SyncTransforms();
            interaction.ScanForInteractable();
            Assert.That(interaction.CurrentTarget, Is.SameAs(probe));
            Assert.That(interaction.TryInteractCurrent(), Is.True);
            Assert.That(probe.InteractionCount, Is.EqualTo(1));
            yield return null;
        }

        private ItemData CreateItem(string id, string displayName)
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.Configure(id, displayName, "Temporary PlayMode test item.", ItemType.Evidence);
            temporaryItems.Add(item);
            return item;
        }

        private ItemPickup CreatePickup(ItemData item)
        {
            GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = "Interaction Test Pickup";
            instance.transform.SetPositionAndRotation(
                playerCamera.transform.position + playerCamera.transform.forward * 1.25f,
                Quaternion.LookRotation(playerCamera.transform.forward, Vector3.up));
            instance.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
            ItemPickup pickup = instance.AddComponent<ItemPickup>();
            pickup.Configure(item);
            temporaryObjects.Add(instance);
            Physics.SyncTransforms();
            return pickup;
        }

        private static CharacterIdentity FindCharacter(string id)
        {
            CharacterIdentity identity = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterIdentity>(true))
                .SingleOrDefault(candidate => candidate.Data != null && candidate.Data.Id == id);
            Assert.That(identity, Is.Not.Null, "CharacterPrototype has no " + id + " exhibit.");
            return identity;
        }

        private static Renderer FindRenderer(Component character, string rendererName)
        {
            Renderer renderer = character.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(candidate => candidate.name == rendererName);
            Assert.That(renderer, Is.Not.Null, character.name + " has no " + rendererName + " renderer.");
            return renderer;
        }

        private static Texture ShownTexture(Renderer renderer, MaterialPropertyBlock properties)
        {
            renderer.GetPropertyBlock(properties);
            return properties.GetTexture(MainTexture);
        }

        private static Dictionary<Transform, Quaternion> CaptureLocalRotations(Animator animator)
        {
            return animator.GetComponentsInChildren<Transform>(true)
                .ToDictionary(transform => transform, transform => transform.localRotation);
        }

        private static void RemoveDevice(InputDevice device)
        {
            if (device != null && device.added)
                InputSystem.RemoveDevice(device);
        }
    }

    public sealed class PrototypeInteractableProbe : MonoBehaviour, IInteractable
    {
        public string Prompt => "TEST INTERACTION";
        public int InteractionCount { get; private set; }

        public bool CanInteract(InteractionContext context)
        {
            return context != null;
        }

        public void Interact(InteractionContext context)
        {
            InteractionCount++;
        }
    }
}
