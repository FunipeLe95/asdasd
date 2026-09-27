using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using Voron.Characters;
using Voron.Core;
using Voron.Interaction;
using Voron.Inventory;
using Voron.Player;
using Voron.UI;

namespace Voron.Editor
{
    [InitializeOnLoad]
    public static partial class PrototypeBuilder
    {
        private const string Root = "Assets/_Game";
        private const string CharacterArtFolder = Root + "/Art/Characters";
        private const string FaceArtFolder = Root + "/Art/Textures/Faces";
        private const string RigModelPath = CharacterArtFolder + "/CharacterBase.fbx";
        private const string CharacterPrefabFolder = Root + "/Prefabs/Characters";
        private const string CharacterDataFolder = Root + "/Data/Characters";
        private const string ItemDataFolder = Root + "/Data/Items";
        private const string ScenePath = Root + "/Scenes/Prototype/CharacterPrototype.unity";
        private const string BaseCharacterPrefabPath = CharacterPrefabFolder + "/Character_Base.prefab";
        private const string CharacterAnimationFolder = Root + "/Animations/Characters";
        private const string CharacterControllerPath = CharacterAnimationFolder + "/AC_Character.controller";

        private static bool building;
        private static bool automaticBuildQueued;

        private static readonly CharacterSpec[] Characters =
        {
            new CharacterSpec(
                "alexey_voron", "Alexey Voron", "Detective",
                "I should review the case file.", new Color(0.55f, 0.56f, 0.57f),
                "AlexeyVoron", "Alexey", "Voron"),
            new CharacterSpec(
                "elena_voron", "Elena Voron", "Person of Interest",
                "Is there something you need?", new Color(0.58f, 0.39f, 0.4f),
                "ElenaVoron", "Elena"),
            new CharacterSpec(
                "dr_ilya_morozov", "Dr. Ilya Morozov", "Physician",
                "I have no comment.", new Color(0.48f, 0.52f, 0.5f),
                "DrIlyaMorozov", "IlyaMorozov", "Morozov"),
            new CharacterSpec(
                "police_officer", "Police Officer", "Police Officer",
                "Officer on duty.", new Color(0.27f, 0.34f, 0.43f),
                "PoliceOfficer", "Officer")
        };

        private static readonly ItemSpec[] Items =
        {
            new ItemSpec(
                "police_badge", "Police Badge",
                "Identification badge belonging to Alexey.", ItemType.QuestItem,
                "Icon_PoliceBadge", "Pickup_PoliceBadge", new Color(0.62f, 0.46f, 0.2f)),
            new ItemSpec(
                "old_photograph", "Old Photograph",
                "A photograph showing a group of people. One face has been scratched out.", ItemType.Photograph,
                "Icon_OldPhotograph", "Pickup_OldPhotograph", new Color(0.67f, 0.64f, 0.56f)),
            new ItemSpec(
                "apartment_key", "Apartment Key",
                "A rusty key found at the crime scene.", ItemType.Key,
                "Icon_ApartmentKey", "Pickup_ApartmentKey", new Color(0.53f, 0.34f, 0.21f)),
            new ItemSpec(
                "medical_file", "Medical File",
                "An old medical record with several pages missing.", ItemType.Document,
                "Icon_MedicalFile", "Pickup_MedicalFile", new Color(0.55f, 0.52f, 0.43f))
        };

        static PrototypeBuilder()
        {
            ScheduleAutomaticBuild();
        }

        [MenuItem("Tools/Voron/Build Character Prototype")]
        public static void Build()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for Unity compilation and asset import to finish before building the prototype.");

            if (!TryGetReadyRig(out _, out string reason))
                throw new InvalidOperationException(reason);

            BuildInternal();
        }

        [MenuItem("Tools/Voron/Build Character Prototype", true)]
        private static bool CanBuildFromMenu()
        {
            return !building && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                   TryGetReadyRig(out _, out _);
        }

        [MenuItem("Tools/Voron/Validate Character Prototype")]
        public static void Validate()
        {
            ValidateGeneratedContent();
            Debug.Log("Voron Character Prototype assets passed editor validation.");
        }

        internal static void ScheduleAutomaticBuild()
        {
            if (building || automaticBuildQueued)
                return;

            automaticBuildQueued = true;
            EditorApplication.delayCall += TryAutomaticBuild;
        }

        private static void TryAutomaticBuild()
        {
            automaticBuildQueued = false;
            if (building)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleAutomaticBuild();
                return;
            }

            if (!HasMissingPrototypeContent() || !TryGetReadyRig(out _, out _))
                return;

            try
            {
                BuildInternal();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void BuildInternal()
        {
            if (building)
                throw new InvalidOperationException("The character prototype builder is already running.");

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity is compiling or importing assets; retry the prototype build when it is idle.");

            building = true;
            try
            {
                EnsureFolders();
                GameObject rig = PrepareRigModel();
                PreparePS1ArtTextureImports();

                var faceTextures = Characters.ToDictionary(character => character.Id, EnsureFaceTexture);
                var talkFaceTextures = Characters.ToDictionary(
                    character => character.Id,
                    character => FindFaceVariantTexture(character, TalkFaceSuffix));
                var blinkFaceTextures = Characters.ToDictionary(
                    character => character.Id,
                    character => FindFaceVariantTexture(character, BlinkFaceSuffix));
                var faceMaterials = Characters.ToDictionary(
                    character => character.Id,
                    character => EnsureFaceMaterial(character, faceTextures[character.Id]));

                var prototypeMaterials = EnsurePrototypeMaterials();
                var itemMaterials = Items.ToDictionary(item => item.Id, EnsureItemMaterial);
                AnimationArtifacts animations = EnsureAnimationArtifacts(rig);

                var characterData = Characters.ToDictionary(
                    character => character.Id,
                    character => EnsureCharacterData(
                        character,
                        faceTextures[character.Id],
                        talkFaceTextures[character.Id],
                        blinkFaceTextures[character.Id]));

                GameObject basePrefab = EnsureCharacterBasePrefab(rig, animations.Controller);

                var characterPrefabs = Characters.ToDictionary(
                    character => character.Id,
                    character =>
                    {
                        // Characters without a compatible model of their own fall back to the base rig and its clips.
                        GameObject model = FindCompatibleCharacterModel(character, animations.Controller);
                        RuntimeAnimatorController controller = model != null
                            ? EnsureCharacterAnimatorOverride(character, model, animations.Controller)
                            : animations.Controller;
                        return EnsureCharacterVariant(
                            basePrefab,
                            character,
                            characterData[character.Id],
                            faceMaterials[character.Id],
                            model,
                            controller);
                    });

                var icons = Items.ToDictionary(item => item.Id, EnsureItemIcon);
                var pickupPrefabs = Items.ToDictionary(
                    item => item.Id,
                    item => EnsurePickupVisualPrefab(item, itemMaterials[item.Id]));
                var itemData = Items.ToDictionary(
                    item => item.Id,
                    item => EnsureItemData(item, icons[item.Id], pickupPrefabs[item.Id]));

                foreach (ItemSpec item in Items)
                    EnsurePickupBehaviour(pickupPrefabs[item.Id], itemData[item.Id]);

                LightingProfile neutralProfile = EnsureLightingProfile(false);
                LightingProfile horrorProfile = EnsureLightingProfile(true);
                Font font = GetBuiltinFont();
                GameObject playerPrefab = EnsurePlayerPrefab(font);

                EnsurePrototypeScene(
                    playerPrefab,
                    characterPrefabs,
                    pickupPrefabs,
                    neutralProfile,
                    prototypeMaterials);

                AddSceneToBuildSettings(ScenePath);
                bool inputSettingsChanged = EnableNewInputSystem();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ValidateGeneratedContent();

                string inputMessage = inputSettingsChanged
                    ? " Active Input Handling was enabled for the Input System package."
                    : string.Empty;
                Debug.Log("Built the missing Voron Character Prototype assets without replacing existing content." + inputMessage);
            }
            finally
            {
                building = false;
            }
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                Root + "/Animations/Characters",
                Root + "/Art/Materials",
                Root + "/Data/Characters",
                Root + "/Data/Items",
                Root + "/Generated/Characters/Faces",
                Root + "/Materials/Characters",
                Root + "/Materials/Items",
                Root + "/Materials/Prototype",
                Root + "/Prefabs/Characters",
                Root + "/Prefabs/Interactables",
                Root + "/Prefabs/Player",
                Root + "/Scenes/Prototype",
                Root + "/Scripts/Core",
                Root + "/UI/Icons",
                Root + "/Visuals/Lighting"
            };

            foreach (string folder in folders)
                EnsureFolder(folder);
        }

        private static GameObject PrepareRigModel()
        {
            ModelImporter importer = AssetImporter.GetAtPath(RigModelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("Expected a Unity ModelImporter at " + RigModelPath + ".");

            if (ApplyCharacterModelImportSettings(importer))
                importer.SaveAndReimport();

            if (!TryGetReadyRig(out GameObject rig, out string reason))
                throw new InvalidOperationException(reason);

            return rig;
        }

        // Returns true when the importer changed and the model needs a reimport.
        private static bool ApplyCharacterModelImportSettings(ModelImporter importer)
        {
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }

            if (importer.optimizeGameObjects)
            {
                importer.optimizeGameObjects = false;
                changed = true;
            }

            // Without this Unity folds the lone exported Armature root into the model root: its axis-conversion
            // rotation would then be wiped by the builder's identity reset and clip paths would lose "Armature/".
            if (!importer.preserveHierarchy)
            {
                importer.preserveHierarchy = true;
                changed = true;
            }

            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            if (importer.animationCompression != ModelImporterAnimationCompression.KeyframeReduction)
            {
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                changed = true;
            }

            if (importer.animationRotationError > CharacterTakeRotationError)
            {
                importer.animationRotationError = CharacterTakeRotationError;
                changed = true;
            }

            if (importer.animationPositionError > CharacterTakePositionError)
            {
                importer.animationPositionError = CharacterTakePositionError;
                changed = true;
            }

            if (importer.animationScaleError > CharacterTakeScaleError)
            {
                importer.animationScaleError = CharacterTakeScaleError;
                changed = true;
            }

            return changed;
        }

        // Unity's default key reduction (0.5 degrees, 0.5 %) lets the baked Idle feet drift by up to about a
        // centimetre; these tolerances keep the Idle and Talk takes within about a millimetre of the Blender source.
        private const float CharacterTakeRotationError = 0.05f;
        private const float CharacterTakePositionError = 0.05f;
        private const float CharacterTakeScaleError = 0.05f;

        private static bool TryGetReadyRig(out GameObject rig, out string reason)
        {
            rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigModelPath);
            if (rig == null)
            {
                reason = "The character source model is not imported yet: " + RigModelPath;
                return false;
            }

            SkinnedMeshRenderer[] skinnedMeshes = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMeshes.Length == 0 || !skinnedMeshes.Any(renderer => renderer.sharedMesh != null && renderer.bones.Length > 0))
            {
                reason = RigModelPath + " must contain a skinned mesh with bound bones before prototype generation.";
                return false;
            }

            if (FindFaceRenderer(rig.transform) == null)
            {
                reason = RigModelPath + " must include a rendered mesh named Face (or a name containing Face).";
                return false;
            }

            reason = null;
            return true;
        }

        private static CharacterData EnsureCharacterData(
            CharacterSpec character,
            Texture2D faceTexture,
            Texture2D talkFaceTexture,
            Texture2D blinkFaceTexture)
        {
            string path = CharacterDataPath(character);
            CharacterData data = AssetDatabase.LoadAssetAtPath<CharacterData>(path);
            if (data != null)
            {
                if (data.Id != character.Id)
                    throw new InvalidOperationException(path + " has an unexpected character ID; the edited asset was left unchanged.");

                // Only empty face slots are filled, so edited assignments survive rebuilds.
                Texture2D face = data.FaceTexture != null ? data.FaceTexture : faceTexture;
                Texture2D talk = data.TalkFaceTexture != null ? data.TalkFaceTexture : talkFaceTexture;
                Texture2D blink = data.BlinkFaceTexture != null ? data.BlinkFaceTexture : blinkFaceTexture;
                if (face != data.FaceTexture || talk != data.TalkFaceTexture || blink != data.BlinkFaceTexture)
                {
                    data.Configure(data.Id, data.DisplayName, data.Role, data.Greeting, face, data.BodyTint, talk, blink);
                    EditorUtility.SetDirty(data);
                }
                return data;
            }

            EnsurePathIsFree(path);
            data = ScriptableObject.CreateInstance<CharacterData>();
            data.Configure(character.Id, character.DisplayName, character.Role, character.Greeting, faceTexture,
                character.BodyTint, talkFaceTexture, blinkFaceTexture);
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        private static ItemData EnsureItemData(ItemSpec item, Sprite icon, GameObject worldPrefab)
        {
            string path = ItemDataPath(item);
            ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (data == null)
            {
                EnsurePathIsFree(path);
                data = ScriptableObject.CreateInstance<ItemData>();
                data.Configure(item.Id, item.DisplayName, item.Description, item.Type, false, 1, icon, worldPrefab);
                AssetDatabase.CreateAsset(data, path);
                return data;
            }

            if (data.Id != item.Id)
                throw new InvalidOperationException(path + " has an unexpected item ID; the edited asset was left unchanged.");

            if (data.Icon == null || data.WorldPrefab == null)
            {
                data.Configure(
                    data.Id,
                    data.DisplayName,
                    data.Description,
                    data.Type,
                    data.Stackable,
                    data.MaxStack,
                    data.Icon != null ? data.Icon : icon,
                    data.WorldPrefab != null ? data.WorldPrefab : worldPrefab);
                EditorUtility.SetDirty(data);
            }

            return data;
        }

        private static string CharacterDataPath(CharacterSpec character)
        {
            return CharacterDataFolder + "/CharacterData_" + character.PrefabName + ".asset";
        }

        private static string ItemDataPath(ItemSpec item)
        {
            return ItemDataFolder + "/ItemData_" + ToPascalCase(item.Id) + ".asset";
        }

        private static string CharacterPrefabPath(CharacterSpec character)
        {
            return CharacterPrefabFolder + "/Character_" + character.PrefabName + ".prefab";
        }

        private static string CharacterClipPath(string clipName)
        {
            return CharacterAnimationFolder + "/" + clipName + ".anim";
        }

        private static string CharacterAnimationPath(CharacterSpec character)
        {
            return CharacterAnimationFolder + "/" + character.PrefabName;
        }

        private static string CharacterClipPath(CharacterSpec character, string clipName)
        {
            return CharacterAnimationPath(character) + "/" + clipName + ".anim";
        }

        private static string CharacterOverrideControllerPath(CharacterSpec character)
        {
            return CharacterAnimationPath(character) + "/AOC_" + character.PrefabName + ".overrideController";
        }

        private static string PickupPrefabPath(ItemSpec item)
        {
            return Root + "/Prefabs/Interactables/" + item.PrefabName + ".prefab";
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int index = Array.FindIndex(scenes, scene => scene.path == scenePath);
            if (index < 0)
            {
                EditorBuildSettings.scenes = scenes
                    .Concat(new[] { new EditorBuildSettingsScene(scenePath, true) })
                    .ToArray();
                return;
            }

            if (!scenes[index].enabled)
            {
                scenes[index] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes;
            }
        }

        private static bool EnableNewInputSystem()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            Type helper = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEngine.InputSystem.Editor.EditorPlayerSettingHelpers", false))
                .FirstOrDefault(type => type != null);

            PropertyInfo newInputProperty = helper != null
                ? helper.GetProperty("newSystemBackendsEnabled", flags)
                : null;

            if (newInputProperty != null && newInputProperty.CanRead && newInputProperty.CanWrite)
            {
                bool wasEnabled = (bool)newInputProperty.GetValue(null, null);
                if (!wasEnabled)
                    newInputProperty.SetValue(null, true, null);
                return !wasEnabled;
            }

            PropertyInfo activeInputHandler = typeof(PlayerSettings).GetProperty("activeInputHandler", flags);
            if (activeInputHandler != null && activeInputHandler.CanRead && activeInputHandler.CanWrite)
            {
                int current = Convert.ToInt32(activeInputHandler.GetValue(null, null));
                if (current == 0)
                {
                    activeInputHandler.SetValue(null, Enum.ToObject(activeInputHandler.PropertyType, 2), null);
                    return true;
                }
                return false;
            }

            Debug.LogWarning("Could not locate the Input System Player Settings API; Active Input Handling was not changed.");
            return false;
        }

        private static bool HasMissingPrototypeContent()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BaseCharacterPrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Player/Player.prefab") == null ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null ||
                AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterControllerPath) == null ||
                AssetDatabase.LoadAssetAtPath<LightingProfile>(Root + "/Visuals/Lighting/LP_NeutralPrototype.asset") == null ||
                AssetDatabase.LoadAssetAtPath<LightingProfile>(Root + "/Visuals/Lighting/LP_HorrorNight.asset") == null)
                return true;

            foreach (string clipName in CharacterStateNames)
            {
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(CharacterClipPath(clipName)) == null)
                    return true;
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterControllerPath);
            foreach (CharacterSpec character in Characters)
            {
                CharacterData data = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath(character));
                if (AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath(character)) == null ||
                    data == null || data.FaceTexture == null ||
                    IsFaceVariantUnassigned(data.TalkFaceTexture, character, TalkFaceSuffix) ||
                    IsFaceVariantUnassigned(data.BlinkFaceTexture, character, BlinkFaceSuffix) ||
                    AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Characters/M_Face_" + character.PrefabName + ".mat") == null ||
                    IsCharacterAnimationContentMissing(character, controller))
                    return true;
            }

            foreach (ItemSpec item in Items)
            {
                if (AssetDatabase.LoadAssetAtPath<ItemData>(ItemDataPath(item)) == null ||
                    AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath(item)) == null ||
                    AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Items/M_" + ToPascalCase(item.Id) + ".mat") == null ||
                    IsItemIconMissing(item))
                    return true;
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Prototype/M_Prototype_Floor.mat") == null ||
                AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Prototype/M_Prototype_Wall.mat") == null ||
                AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Prototype/M_Prototype_Trim.mat") == null)
                return true;

            return false;
        }

        // Mirrors BuildInternal: per-character clips are only generated for a compatible model of that character.
        private static bool IsCharacterAnimationContentMissing(CharacterSpec character, AnimatorController controller)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CharacterOverrideControllerPath(character)) != null &&
                AuthoredClipNames.All(clipName => AssetDatabase.LoadAssetAtPath<AnimationClip>(CharacterClipPath(character, clipName)) != null))
                return false;

            string modelPath = FindCharacterModelPath(character);
            GameObject model = modelPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) : null;
            return HasCompatibleAnimationPaths(model, controller);
        }

        private static void ValidateGeneratedContent()
        {
            RequireAsset<GameObject>(BaseCharacterPrefabPath);
            RequireAsset<GameObject>(Root + "/Prefabs/Player/Player.prefab");
            RequireAsset<LightingProfile>(Root + "/Visuals/Lighting/LP_NeutralPrototype.asset");
            RequireAsset<LightingProfile>(Root + "/Visuals/Lighting/LP_HorrorNight.asset");
            RequireAsset<AnimatorController>(CharacterControllerPath);
            RequireAsset<SceneAsset>(ScenePath);

            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseCharacterPrefabPath);
            if (PrefabUtility.GetPrefabAssetType(basePrefab) != PrefabAssetType.Regular)
                throw new InvalidOperationException("Character_Base must be a regular common prefab.");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterControllerPath);
            Animator baseAnimator = basePrefab.GetComponentInChildren<Animator>(true);
            if (baseAnimator == null || baseAnimator.runtimeAnimatorController != controller)
                throw new InvalidOperationException("Character_Base must animate its model with " + CharacterControllerPath + ".");

            string[] stateNames = controller.layers[0].stateMachine.states.Select(state => state.state.name).ToArray();
            foreach (string state in CharacterStateNames)
            {
                if (!stateNames.Contains(state))
                    throw new InvalidOperationException("The character Animator Controller is missing its " + state + " state.");
            }

            string[] requiredParameters = { "Speed", "Inspect", "Talk", "Turn", "Death" };
            string[] parameterNames = controller.parameters.Select(parameter => parameter.name).ToArray();
            foreach (string parameter in requiredParameters)
            {
                if (!parameterNames.Contains(parameter))
                    throw new InvalidOperationException("The character Animator Controller is missing its " + parameter + " parameter.");
            }

            GameObject rig = RequireAsset<GameObject>(RigModelPath);
            foreach (string clipName in CharacterStateNames)
            {
                string clipPath = CharacterClipPath(clipName);
                AnimationClip clip = RequireAsset<AnimationClip>(clipPath);
                RequireResolvedTransformBindings(clip, rig.transform, clipPath);
                if (AuthoredClipNames.Contains(clipName))
                    RequireAuthoredLoopSetting(clip, clipName, clipPath);
            }

            foreach (CharacterSpec character in Characters)
            {
                CharacterData data = RequireAsset<CharacterData>(CharacterDataPath(character));
                if (data.Id != character.Id || data.FaceTexture == null)
                    throw new InvalidOperationException("Character data is missing its ID or face texture at " + CharacterDataPath(character));

                GameObject prefab = RequireAsset<GameObject>(CharacterPrefabPath(character));
                if (prefab.GetComponent<CharacterIdentity>() == null ||
                    prefab.GetComponent<CharacterAppearance>() == null ||
                    prefab.GetComponent<CharacterAnimation>() == null ||
                    prefab.GetComponent<CharacterInteractable>() == null ||
                    prefab.GetComponentInChildren<Animator>(true) == null)
                    throw new InvalidOperationException("Character prefab is missing a required component: " + CharacterPrefabPath(character));
                if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
                    throw new InvalidOperationException(CharacterPrefabPath(character) + " must be a prefab variant of Character_Base.");
                ValidateCharacterAnimator(character, prefab.GetComponentInChildren<Animator>(true), controller);
            }

            string playerPath = Root + "/Prefabs/Player/Player.prefab";
            GameObject player = RequireAsset<GameObject>(playerPath);
            if (player.GetComponent<PlayerInputReader>() == null ||
                player.GetComponent<PlayerController>() == null ||
                player.GetComponent<InventorySystem>() == null ||
                player.GetComponent<InteractionSystem>() == null ||
                player.GetComponentInChildren<Camera>(true) == null ||
                player.GetComponentInChildren<InventoryUI>(true) == null ||
                player.GetComponentInChildren<PrototypeHUD>(true) == null)
                throw new InvalidOperationException("Player prefab is missing one or more prototype systems.");

            foreach (ItemSpec item in Items)
            {
                ItemData data = RequireAsset<ItemData>(ItemDataPath(item));
                if (data.Id != item.Id || data.DisplayName != item.DisplayName || data.Description != item.Description ||
                    data.Icon == null || data.WorldPrefab == null)
                    throw new InvalidOperationException("Item data is incomplete or has an unexpected description: " + ItemDataPath(item));

                GameObject pickup = RequireAsset<GameObject>(PickupPrefabPath(item));
                if (pickup.GetComponent<ItemPickup>() == null || pickup.GetComponent<Collider>() == null)
                    throw new InvalidOperationException("Pickup prefab is missing its interaction component or collider: " + PickupPrefabPath(item));
            }

            if (!EditorBuildSettings.scenes.Any(scene => scene.path == ScenePath && scene.enabled))
                throw new InvalidOperationException("CharacterPrototype must be enabled in the Build Settings scene list.");

            ValidatePrototypeScene();
        }

        private static void ValidateCharacterAnimator(CharacterSpec character, Animator animator, AnimatorController controller)
        {
            string prefabPath = CharacterPrefabPath(character);
            RuntimeAnimatorController runtimeController = animator.runtimeAnimatorController;
            if (runtimeController is AnimatorOverrideController overrideController)
            {
                string overridePath = CharacterOverrideControllerPath(character);
                if (AssetDatabase.GetAssetPath(overrideController) != overridePath)
                    throw new InvalidOperationException(prefabPath + " may only use its own override controller " + overridePath + ".");
                if (overrideController.runtimeAnimatorController != controller)
                    throw new InvalidOperationException(overridePath + " must override " + CharacterControllerPath + ".");

                foreach (string clipName in AuthoredClipNames)
                {
                    string clipPath = CharacterClipPath(character, clipName);
                    AnimationClip clip = overrideController[RequireStateClip(controller, clipName)];
                    if (clip == null || AssetDatabase.GetAssetPath(clip) != clipPath)
                        throw new InvalidOperationException(overridePath + " must override " + clipName + " with " + clipPath + ".");
                    RequireAuthoredLoopSetting(clip, clipName, clipPath);
                }
            }
            else if (runtimeController != controller)
            {
                throw new InvalidOperationException(prefabPath + " must use " + CharacterControllerPath + " or its per-character override controller.");
            }

            foreach (AnimationClip clip in runtimeController.animationClips.Distinct())
                RequireResolvedTransformBindings(clip, animator.transform, AssetDatabase.GetAssetPath(clip));
        }

        private static void RequireResolvedTransformBindings(AnimationClip clip, Transform model, string clipPath)
        {
            EditorCurveBinding[] bindings = TransformBindings(clip).ToArray();
            if (bindings.Length == 0)
                throw new InvalidOperationException(clipPath + " does not animate any rig transform paths.");

            foreach (EditorCurveBinding binding in bindings)
            {
                if (ResolveBindingTarget(model, binding.path) == null)
                    throw new InvalidOperationException(clipPath + " references a bone path missing from " + model.name + ": " + binding.path);
            }
        }

        private static void RequireAuthoredLoopSetting(AnimationClip clip, string clipName, string clipPath)
        {
            bool shouldLoop = clipName == "Idle";
            if (AnimationUtility.GetAnimationClipSettings(clip).loopTime != shouldLoop)
                throw new InvalidOperationException(clipPath + (shouldLoop ? " must loop." : " must play once without looping."));
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Required prototype asset is missing or has the wrong type: " + path);
            return asset;
        }

        private static void EnsurePathIsFree(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("An asset already exists at " + path + " but is not the expected type. It was left unchanged.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException("Cannot create Unity asset folder because its parent is missing: " + path);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
            if (!AssetDatabase.IsValidFolder(path))
                throw new InvalidOperationException("Unity could not create asset folder " + path);
        }

        private static string ToPascalCase(string value)
        {
            return string.Concat(value.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private sealed class CharacterSpec
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string Role;
            public readonly string Greeting;
            public readonly Color BodyTint;
            public readonly string PrefabName;
            public readonly string[] SearchTokens;

            public CharacterSpec(string id, string displayName, string role, string greeting, Color bodyTint, string prefabName, params string[] searchTokens)
            {
                Id = id;
                DisplayName = displayName;
                Role = role;
                Greeting = greeting;
                BodyTint = bodyTint;
                PrefabName = prefabName;
                SearchTokens = searchTokens;
            }
        }

        private sealed class ItemSpec
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string Description;
            public readonly ItemType Type;
            public readonly string IconName;
            public readonly string PrefabName;
            public readonly Color Color;

            public ItemSpec(string id, string displayName, string description, ItemType type, string iconName, string prefabName, Color color)
            {
                Id = id;
                DisplayName = displayName;
                Description = description;
                Type = type;
                IconName = iconName;
                PrefabName = prefabName;
                Color = color;
            }
        }

        private sealed class AnimationArtifacts
        {
            public readonly AnimatorController Controller;

            public AnimationArtifacts(AnimatorController controller)
            {
                Controller = controller;
            }
        }
    }
}
