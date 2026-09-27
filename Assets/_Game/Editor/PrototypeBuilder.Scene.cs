using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Voron.Characters;
using Voron.Core;
using Voron.Interaction;
using Voron.Inventory;
using Voron.Player;

namespace Voron.Editor
{
    public static partial class PrototypeBuilder
    {
        private static LightingProfile EnsureLightingProfile(bool horrorNight)
        {
            string fileName = horrorNight ? "LP_HorrorNight.asset" : "LP_NeutralPrototype.asset";
            string path = Root + "/Visuals/Lighting/" + fileName;
            LightingProfile existing = AssetDatabase.LoadAssetAtPath<LightingProfile>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            var profile = ScriptableObject.CreateInstance<LightingProfile>();
            profile.name = horrorNight ? "Horror Night" : "Neutral Prototype";
            if (horrorNight)
            {
                profile.Configure(
                    AmbientMode.Flat,
                    new Color(0.065f, 0.075f, 0.105f, 1f),
                    1f,
                    true,
                    FogMode.ExponentialSquared,
                    new Color(0.035f, 0.045f, 0.075f, 1f),
                    0.018f,
                    new Color(0.43f, 0.5f, 0.68f, 1f),
                    0.34f,
                    0.9f,
                    LightShadows.Hard,
                    0.015f);
            }
            else
            {
                profile.Configure(
                    AmbientMode.Flat,
                    new Color(0.24f, 0.26f, 0.29f, 1f),
                    1f,
                    true,
                    FogMode.ExponentialSquared,
                    new Color(0.16f, 0.18f, 0.21f, 1f),
                    0.006f,
                    new Color(0.8f, 0.82f, 0.83f, 1f),
                    0.88f,
                    0.68f,
                    LightShadows.Soft,
                    0.08f);
            }

            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void EnsurePrototypeScene(
            GameObject playerPrefab,
            IReadOnlyDictionary<string, GameObject> characterPrefabs,
            IReadOnlyDictionary<string, GameObject> pickupPrefabs,
            LightingProfile neutralProfile,
            PrototypeMaterials materials)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return;

            EnsurePathIsFree(ScenePath);
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Unity could not create CharacterPrototype.");

            try
            {
                SceneManager.SetActiveScene(scene);
                GameObject room = new GameObject("Gray Room");
                CreateRoom(room.transform, materials);

                CreateLighting(scene, neutralProfile);
                CreateActors(scene, characterPrefabs, pickupPrefabs, playerPrefab);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unity could not save the prototype scene at " + ScenePath);
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateRoom(Transform parent, PrototypeMaterials materials)
        {
            CreateRoomBox(parent, "Floor", new Vector3(0f, -0.12f, 0f), new Vector3(14f, 0.24f, 14f), materials.Floor);
            CreateRoomBox(parent, "Back Wall", new Vector3(0f, 2.45f, -7f), new Vector3(14f, 5f, 0.22f), materials.Wall);
            CreateRoomBox(parent, "Front Wall", new Vector3(0f, 2.45f, 7f), new Vector3(14f, 5f, 0.22f), materials.Wall);
            CreateRoomBox(parent, "Left Wall", new Vector3(-7f, 2.45f, 0f), new Vector3(0.22f, 5f, 14f), materials.Wall);
            CreateRoomBox(parent, "Right Wall", new Vector3(7f, 2.45f, 0f), new Vector3(0.22f, 5f, 14f), materials.Wall);
            CreateRoomBox(parent, "Ceiling", new Vector3(0f, 5f, 0f), new Vector3(14f, 0.2f, 14f), materials.Trim);

            CreateRoomBox(parent, "Back Baseboard", new Vector3(0f, 0.13f, -6.82f), new Vector3(13.6f, 0.26f, 0.12f), materials.Trim);
            CreateRoomBox(parent, "Front Baseboard", new Vector3(0f, 0.13f, 6.82f), new Vector3(13.6f, 0.26f, 0.12f), materials.Trim);
            CreateRoomBox(parent, "Left Baseboard", new Vector3(-6.82f, 0.13f, 0f), new Vector3(0.12f, 0.26f, 13.6f), materials.Trim);
            CreateRoomBox(parent, "Right Baseboard", new Vector3(6.82f, 0.13f, 0f), new Vector3(0.12f, 0.26f, 13.6f), materials.Trim);
        }

        private static void CreateRoomBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            box.isStatic = true;
        }

        private static Light CreateLighting(Scene scene, LightingProfile profile)
        {
            var lightingRoot = new GameObject("Prototype Lighting");
            SceneManager.MoveGameObjectToScene(lightingRoot, scene);
            var keyObject = new GameObject("Key Light");
            keyObject.transform.SetParent(lightingRoot.transform, false);
            keyObject.transform.localRotation = Quaternion.Euler(48f, -32f, 0f);
            Light keyLight = keyObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = profile.KeyLightColor;
            keyLight.intensity = profile.KeyLightIntensity;
            keyLight.shadows = profile.Shadows;
            keyLight.shadowStrength = profile.ShadowStrength;
            keyLight.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = keyLight;

            PrototypeLighting prototypeLighting = lightingRoot.AddComponent<PrototypeLighting>();
            prototypeLighting.Configure(profile, keyLight);
            RenderSettings.skybox = null;
            return keyLight;
        }

        private static void CreateActors(
            Scene scene,
            IReadOnlyDictionary<string, GameObject> characterPrefabs,
            IReadOnlyDictionary<string, GameObject> pickupPrefabs,
            GameObject playerPrefab)
        {
            string[] characterIds = { "alexey_voron", "elena_voron", "dr_ilya_morozov", "police_officer" };
            Vector3[] positions =
            {
                new Vector3(-3.15f, 0f, -1.15f),
                new Vector3(-1.05f, 0f, -1.45f),
                new Vector3(1.05f, 0f, -1.45f),
                new Vector3(3.15f, 0f, -1.15f)
            };

            for (int i = 0; i < characterIds.Length; i++)
            {
                if (!characterPrefabs.TryGetValue(characterIds[i], out GameObject prefab) || prefab == null)
                    throw new InvalidOperationException("No character prefab was generated for " + characterIds[i]);

                GameObject actor = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (actor == null)
                    throw new InvalidOperationException("Could not add a character prefab to the prototype scene: " + characterIds[i]);
                actor.transform.SetPositionAndRotation(positions[i], Quaternion.identity);
                actor.name = "Exhibit — " + actor.name;

                if (characterIds[i] == "police_officer")
                {
                    CharacterAnimation animation = actor.GetComponent<CharacterAnimation>();
                    if (animation != null)
                    {
                        CharacterWalkPreview walkPreview = actor.AddComponent<CharacterWalkPreview>();
                        walkPreview.Configure(animation, 0.65f);
                        EditorUtility.SetDirty(walkPreview);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(walkPreview);
                    }
                }
            }

            GameObject player = PrefabUtility.InstantiatePrefab(playerPrefab, scene) as GameObject;
            if (player == null)
                throw new InvalidOperationException("Could not add the Player prefab to the prototype scene.");
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4.8f), Quaternion.Euler(0f, 180f, 0f));
            player.name = "Player — Character Prototype";

            string[] itemIds = { "police_badge", "old_photograph", "apartment_key", "medical_file" };
            Vector3[] itemPositions =
            {
                new Vector3(-4.2f, 0f, 2.2f),
                new Vector3(-1.45f, 0f, 2.35f),
                new Vector3(1.45f, 0f, 2.35f),
                new Vector3(4.2f, 0f, 2.2f)
            };

            for (int i = 0; i < itemIds.Length; i++)
            {
                if (!pickupPrefabs.TryGetValue(itemIds[i], out GameObject prefab) || prefab == null)
                    throw new InvalidOperationException("No pickup prefab was generated for " + itemIds[i]);

                GameObject pickup = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (pickup == null)
                    throw new InvalidOperationException("Could not add an item pickup to the prototype scene: " + itemIds[i]);
                pickup.transform.SetPositionAndRotation(itemPositions[i], Quaternion.Euler(0f, (i - 1.5f) * 12f, 0f));
                pickup.name = "Pickup — " + pickup.name;
            }
        }

        private static void ValidatePrototypeScene()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("CharacterPrototype could not be loaded for validation.");

                GameObject[] roots = scene.GetRootGameObjects();
                CharacterIdentity[] identities = roots.SelectMany(root => root.GetComponentsInChildren<CharacterIdentity>(true)).ToArray();
                if (identities.Length != 4 || identities.Any(identity => identity.Data == null) ||
                    identities.Select(identity => identity.Data.Id).Distinct(StringComparer.Ordinal).Count() != 4)
                    throw new InvalidOperationException("CharacterPrototype must contain four distinct, configured character exhibits.");

                if (roots.SelectMany(root => root.GetComponentsInChildren<PlayerController>(true)).Count() != 1 ||
                    roots.SelectMany(root => root.GetComponentsInChildren<InventorySystem>(true)).Count() != 1 ||
                    roots.SelectMany(root => root.GetComponentsInChildren<InteractionSystem>(true)).Count() != 1 ||
                    roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Count() != 1)
                    throw new InvalidOperationException("CharacterPrototype must contain one configured player with a camera, inventory and interaction system.");

                if (roots.SelectMany(root => root.GetComponentsInChildren<ItemPickup>(true)).Count() != Items.Length)
                    throw new InvalidOperationException("CharacterPrototype must contain all four item pickup tests.");

                if (roots.SelectMany(root => root.GetComponentsInChildren<CharacterWalkPreview>(true)).Count() != 1)
                    throw new InvalidOperationException("CharacterPrototype must contain exactly one walk-cycle demonstrator.");

                PrototypeLighting lighting = roots.SelectMany(root => root.GetComponentsInChildren<PrototypeLighting>(true)).SingleOrDefault();
                LightingProfile neutralProfile = AssetDatabase.LoadAssetAtPath<LightingProfile>(Root + "/Visuals/Lighting/LP_NeutralPrototype.asset");
                if (lighting == null || lighting.Profile != neutralProfile)
                    throw new InvalidOperationException("CharacterPrototype must apply the Neutral Prototype lighting profile.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (openedForValidation && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
