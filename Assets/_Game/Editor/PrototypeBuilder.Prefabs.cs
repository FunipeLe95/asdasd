using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Voron.Characters;
using Voron.Interaction;
using Voron.Inventory;
using Voron.Player;
using Voron.UI;

namespace Voron.Editor
{
    public static partial class PrototypeBuilder
    {
        private static GameObject EnsureCharacterBasePrefab(GameObject rig, AnimatorController controller)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BaseCharacterPrefabPath);
            if (existing != null)
                return existing;

            EnsurePathIsFree(BaseCharacterPrefabPath);
            return InTemporaryScene(scene =>
            {
                var root = new GameObject("Character_Base");
                GameObject model = InstantiateModel(rig, root.transform, scene, "CharacterModel", controller);
                AddCharacterRootComponents(root, model.GetComponent<Animator>());

                bool success;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BaseCharacterPrefabPath, out success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("Unity could not save " + BaseCharacterPrefabPath);
                return prefab;
            });
        }

        private static GameObject EnsureCharacterVariant(
            GameObject basePrefab,
            CharacterSpec character,
            CharacterData data,
            Material faceMaterial,
            GameObject replacementModel,
            RuntimeAnimatorController controller)
        {
            string path = CharacterPrefabPath(character);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            return InTemporaryScene(scene =>
            {
                GameObject root = PrefabUtility.InstantiatePrefab(basePrefab, scene) as GameObject;
                if (root == null)
                    throw new InvalidOperationException("Could not instantiate the common character prefab for " + character.DisplayName);

                root.name = "Character_" + character.PrefabName;
                Transform modelTransform = root.transform.Find("CharacterModel");
                if (modelTransform == null)
                    throw new InvalidOperationException(BaseCharacterPrefabPath + " has no CharacterModel child.");

                GameObject model = modelTransform.gameObject;
                if (replacementModel != null)
                {
                    UnityEngine.Object.DestroyImmediate(model);
                    model = InstantiateModel(replacementModel, root.transform, scene, "CharacterModel", controller);
                }
                else
                {
                    ConfigureAnimator(model, controller);
                }

                Renderer faceRenderer = FindFaceRenderer(model.transform);
                if (faceRenderer == null)
                    throw new InvalidOperationException("Character model has no rendered Face mesh: " + character.DisplayName);
                ApplyFaceMaterial(faceRenderer, faceMaterial);
                Renderer eyesRenderer = FindNamedRenderer(model.transform, "eyes");
                ApplyFaceMaterial(eyesRenderer, faceMaterial);

                Animator animator = model.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException("Character model has no Animator: " + character.DisplayName);

                CharacterIdentity identity = root.GetComponent<CharacterIdentity>();
                CharacterAppearance appearance = root.GetComponent<CharacterAppearance>();
                CharacterAnimation animation = root.GetComponent<CharacterAnimation>();
                if (identity == null || appearance == null || animation == null)
                    throw new InvalidOperationException(BaseCharacterPrefabPath + " is missing a character pipeline component.");

                identity.Configure(data);
                appearance.Configure(data, faceRenderer, eyesRenderer);
                animation.Configure(animator);

                CharacterInteractable interactable = root.GetComponent<CharacterInteractable>();
                if (interactable == null)
                    interactable = root.AddComponent<CharacterInteractable>();
                interactable.Configure(data, animation);

                EditorUtility.SetDirty(identity);
                EditorUtility.SetDirty(appearance);
                EditorUtility.SetDirty(animation);
                EditorUtility.SetDirty(interactable);
                PrefabUtility.RecordPrefabInstancePropertyModifications(identity);
                PrefabUtility.RecordPrefabInstancePropertyModifications(appearance);
                PrefabUtility.RecordPrefabInstancePropertyModifications(animation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(interactable);

                bool success;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("Unity could not save character prefab " + path);
                return prefab;
            });
        }

        private static void AddCharacterRootComponents(GameObject root, Animator animator)
        {
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.height = 1.8f;
            collider.radius = 0.27f;

            root.AddComponent<CharacterIdentity>();
            root.AddComponent<CharacterAppearance>();
            var animation = root.AddComponent<CharacterAnimation>();
            animation.Configure(animator);

            var interactionPoint = new GameObject("InteractionPoint");
            interactionPoint.transform.SetParent(root.transform, false);
            interactionPoint.transform.localPosition = new Vector3(0f, 1.32f, 0f);
        }

        private static GameObject InstantiateModel(GameObject source, Transform parent, Scene scene, string instanceName, RuntimeAnimatorController controller)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Avatar>().FirstOrDefault();
            GameObject model = PrefabUtility.InstantiatePrefab(source, scene) as GameObject;
            if (model == null)
                throw new InvalidOperationException("Could not instantiate character source model " + source.name);

            model.name = instanceName;
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            RecordInstanceOverrides(model);
            RecordInstanceOverrides(model.transform);
            ConfigureAnimator(model, controller, avatar);
            return model;
        }

        private static Animator ConfigureAnimator(GameObject model, RuntimeAnimatorController controller, Avatar sourceAvatar = null)
        {
            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
                animator = model.AddComponent<Animator>();

            if (animator.avatar == null && sourceAvatar != null)
                animator.avatar = sourceAvatar;

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            RecordInstanceOverrides(animator);
            return animator;
        }

        // Script edits to a nested model instance are only saved with the prefab once recorded as overrides.
        private static void RecordInstanceOverrides(UnityEngine.Object target)
        {
            if (target != null && PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static Renderer FindFaceRenderer(Transform root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform namedFace = transforms.FirstOrDefault(transform =>
                NormalizeToken(transform.name) == "face" || NormalizeToken(transform.name).StartsWith("face", StringComparison.Ordinal));

            Renderer renderer = namedFace != null ? namedFace.GetComponent<Renderer>() : null;
            if (renderer == null && namedFace != null)
                renderer = namedFace.GetComponentInChildren<Renderer>(true);
            if (renderer != null)
                return renderer;

            return root.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(candidate => NormalizeToken(candidate.gameObject.name).Contains("face"));
        }

        private static Renderer FindNamedRenderer(Transform root, string token)
        {
            return root.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(candidate => NormalizeToken(candidate.gameObject.name) == token);
        }

        private static void ApplyFaceMaterial(Renderer faceRenderer, Material material)
        {
            if (faceRenderer == null || material == null)
                return;

            Material[] materials = faceRenderer.sharedMaterials;
            if (materials.Length == 0)
                materials = new[] { material };
            else
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
            faceRenderer.sharedMaterials = materials;
            RecordInstanceOverrides(faceRenderer);
        }

        private static GameObject FindCompatibleCharacterModel(CharacterSpec character, AnimatorController controller)
        {
            GameObject model = FindCharacterModel(character);
            if (model == null || HasCompatibleAnimationPaths(model, controller))
                return model;

            Debug.LogWarning("Character model " + AssetDatabase.GetAssetPath(model) +
                             " does not use the shared animation bone paths; " + character.DisplayName +
                             " will use " + RigModelPath + " instead.");
            return null;
        }

        private static GameObject FindCharacterModel(CharacterSpec character)
        {
            string path = FindCharacterModelPath(character);
            if (path == null)
                return null;

            PrepareOptionalModelImporter(path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static string FindCharacterModelPath(CharacterSpec character)
        {
            if (!AssetDatabase.IsValidFolder(CharacterArtFolder))
                return null;

            string[] modelPaths = AssetDatabase.FindAssets("t:Model", new[] { CharacterArtFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(path, RigModelPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string token in new[] { character.PrefabName }.Concat(character.SearchTokens))
            {
                string normalizedToken = NormalizeToken(token);
                string match = modelPaths.FirstOrDefault(path => NormalizeToken(Path.GetFileNameWithoutExtension(path)).Contains(normalizedToken));
                if (match != null)
                    return match;
            }

            return null;
        }

        private static void PrepareOptionalModelImporter(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && ApplyCharacterModelImportSettings(importer))
                importer.SaveAndReimport();
        }

        private static GameObject EnsurePickupVisualPrefab(ItemSpec item, Material material)
        {
            string path = PickupPrefabPath(item);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            return InTemporaryScene(scene =>
            {
                var root = new GameObject(item.PrefabName);
                var pickupCollider = root.AddComponent<BoxCollider>();
                pickupCollider.center = new Vector3(0f, 0.09f, 0f);
                pickupCollider.size = new Vector3(0.62f, 0.36f, 0.58f);

                if (item.Id == "police_badge")
                {
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0f, 0.09f, 0f), new Vector3(0.29f, 0.045f, 0.24f), material);
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0f), new Vector3(0.12f, 0.025f, 0.12f), material);
                }
                else if (item.Id == "old_photograph")
                {
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0f, 0.035f, 0f), new Vector3(0.34f, 0.025f, 0.44f), material);
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0f, 0.052f, -0.025f), new Vector3(0.26f, 0.009f, 0.27f), EnsureFlatMaterial(
                        Root + "/Materials/Items/M_Photograph_Inset.mat", "M_Photograph_Inset", new Color(0.19f, 0.2f, 0.2f)));
                }
                else if (item.Id == "apartment_key")
                {
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cylinder, new Vector3(-0.13f, 0.055f, 0f), new Vector3(0.18f, 0.025f, 0.18f), material);
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0.11f, 0.055f, 0f), new Vector3(0.34f, 0.035f, 0.055f), material);
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0.24f, 0.045f, 0f), new Vector3(0.055f, 0.06f, 0.055f), material);
                }
                else
                {
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(0f, 0.055f, 0f), new Vector3(0.31f, 0.09f, 0.41f), material);
                    AddPrimitiveShape(root.transform, scene, PrimitiveType.Cube, new Vector3(-0.135f, 0.057f, 0f), new Vector3(0.035f, 0.094f, 0.41f),
                        EnsureFlatMaterial(Root + "/Materials/Items/M_MedicalFile_Spine.mat", "M_MedicalFile_Spine", new Color(0.35f, 0.19f, 0.18f)));
                }

                bool success;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("Unity could not save pickup prefab " + path);
                return prefab;
            });
        }

        private static void EnsurePickupBehaviour(GameObject prefabAsset, ItemData itemData)
        {
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<ItemPickup>() != null)
                    return;

                if (root.GetComponent<Collider>() == null)
                    root.AddComponent<BoxCollider>();
                ItemPickup pickup = root.AddComponent<ItemPickup>();
                pickup.Configure(itemData, 1);
                EditorUtility.SetDirty(pickup);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject EnsurePlayerPrefab(Font font)
        {
            const string path = Root + "/Prefabs/Player/Player.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            return InTemporaryScene(scene =>
            {
                var root = new GameObject("Player");
                root.tag = "Player";
                CharacterController characterController = root.AddComponent<CharacterController>();
                characterController.height = 1.8f;
                characterController.radius = 0.28f;
                characterController.center = new Vector3(0f, 0.9f, 0f);
                characterController.skinWidth = 0.035f;
                characterController.stepOffset = 0.28f;
                characterController.slopeLimit = 45f;

                PlayerInputReader input = root.AddComponent<PlayerInputReader>();
                InventorySystem inventory = root.AddComponent<InventorySystem>();
                inventory.Configure(16);

                var cameraObject = new GameObject("PlayerCamera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 64f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 55f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.032f, 0.04f, 1f);
                camera.allowHDR = false;
                cameraObject.AddComponent<AudioListener>();

                var flashlightObject = new GameObject("Flashlight");
                flashlightObject.transform.SetParent(cameraObject.transform, false);
                flashlightObject.transform.localPosition = new Vector3(0.04f, -0.06f, 0.12f);
                Light flashlight = flashlightObject.AddComponent<Light>();
                flashlight.type = LightType.Spot;
                flashlight.color = new Color(0.88f, 0.9f, 0.92f);
                flashlight.intensity = 3.1f;
                flashlight.range = 13f;
                flashlight.spotAngle = 54f;
                flashlight.shadows = LightShadows.Hard;

                var canvasObject = new GameObject("PrototypeUI");
                canvasObject.transform.SetParent(root.transform, false);

                InventoryUI inventoryUI = canvasObject.AddComponent<InventoryUI>();
                inventoryUI.Configure(inventory, input, font);
                PrototypeHUD hud = canvasObject.AddComponent<PrototypeHUD>();
                hud.Configure(input, font);

                PlayerController controller = root.AddComponent<PlayerController>();
                controller.Configure(input, camera.transform, inventoryUI);
                InteractionSystem interaction = root.AddComponent<InteractionSystem>();
                interaction.Configure(camera, input, inventory, inventoryUI, hud);
                PlayerFlashlight playerFlashlight = root.AddComponent<PlayerFlashlight>();
                playerFlashlight.Configure(input, flashlight, inventoryUI);

                bool success;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out success);
                if (!success || prefab == null)
                    throw new InvalidOperationException("Unity could not save the player prefab at " + path);
                return prefab;
            });
        }

        private static Font GetBuiltinFont()
        {
            foreach (string name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try
                {
                    Font font = Resources.GetBuiltinResource<Font>(name);
                    if (font != null)
                        return font;
                }
                catch (ArgumentException)
                {
                }
            }

            Debug.LogWarning("No built-in legacy UI font was found; inventory and HUD components will use their fallback font.");
            return null;
        }

        private static void AddPrimitiveShape(Transform parent, Scene scene, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            if (shape.scene != scene)
                SceneManager.MoveGameObjectToScene(shape, scene);
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = localPosition;
            shape.transform.localScale = localScale;
            Collider collider = shape.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            Renderer renderer = shape.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
        }

        private static T InTemporaryScene<T>(Func<Scene, T> create)
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (!temporaryScene.IsValid() || !temporaryScene.isLoaded)
                throw new InvalidOperationException("Unity could not create a temporary scene for prototype asset construction.");

            try
            {
                SceneManager.SetActiveScene(temporaryScene);
                return create(temporaryScene);
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (temporaryScene.IsValid() && temporaryScene.isLoaded)
                    EditorSceneManager.CloseScene(temporaryScene, true);
            }
        }
    }
}
