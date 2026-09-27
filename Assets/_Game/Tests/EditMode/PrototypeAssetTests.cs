using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Voron.Characters;
using Voron.Interaction;
using Voron.Inventory;

namespace Voron.Tests
{
    public sealed class PrototypeAssetTests
    {
        private const string Root = "Assets/_Game";
        private const string CharacterPrefabFolder = Root + "/Prefabs/Characters";
        private const string CharacterDataFolder = Root + "/Data/Characters";
        private const string ItemDataFolder = Root + "/Data/Items";
        private const string AnimationFolder = Root + "/Animations/Characters";
        private const string CharacterArtFolder = Root + "/Art/Characters";
        private const string FaceArtFolder = Root + "/Art/Textures/Faces";
        private const string BasePrefabPath = CharacterPrefabFolder + "/Character_Base.prefab";
        private const string ControllerPath = AnimationFolder + "/AC_Character.controller";
        private const string RigPath = CharacterArtFolder + "/CharacterBase.fbx";
        private const string ScenePath = Root + "/Scenes/Prototype/CharacterPrototype.unity";
        private const string BuildHint = "Run Unity menu Tools/Voron/Build Character Prototype first.";
        private const float FrameSeconds = 1f / 24f;
        private const float MaxTakeCompressionError = 0.05f;

        private static readonly CharacterExpectation[] Characters =
        {
            new CharacterExpectation("AlexeyVoron", "alexey_voron", "Alexey Voron"),
            new CharacterExpectation("ElenaVoron", "elena_voron", "Elena Voron"),
            new CharacterExpectation("DrIlyaMorozov", "dr_ilya_morozov", "Dr. Ilya Morozov"),
            new CharacterExpectation("PoliceOfficer", "police_officer", "Police Officer")
        };

        private static readonly ItemExpectation[] Items =
        {
            new ItemExpectation("PoliceBadge", "police_badge", "Police Badge", "Identification badge belonging to Alexey.", ItemType.QuestItem, "PoliceBadge_32.png", "Pickup_PoliceBadge"),
            new ItemExpectation("OldPhotograph", "old_photograph", "Old Photograph", "A photograph showing a group of people. One face has been scratched out.", ItemType.Photograph, "OldPhotograph_32.png", "Pickup_OldPhotograph"),
            new ItemExpectation("ApartmentKey", "apartment_key", "Apartment Key", "A rusty key found at the crime scene.", ItemType.Key, "ApartmentKey_32.png", "Pickup_ApartmentKey"),
            new ItemExpectation("MedicalFile", "medical_file", "Medical File", "An old medical record with several pages missing.", ItemType.Document, "MedicalFile_32.png", "Pickup_MedicalFile")
        };

        [Test]
        public void CharacterPrefabsAreVariantsOfTheSharedBase()
        {
            GameObject basePrefab = RequireAsset<GameObject>(BasePrefabPath);
            Assert.That(PrefabUtility.GetPrefabAssetType(basePrefab), Is.EqualTo(PrefabAssetType.Regular));
            Animator baseAnimator = basePrefab.GetComponentInChildren<Animator>(true);
            Assert.That(baseAnimator, Is.Not.Null, BasePrefabPath);
            Assert.That(AssetDatabase.GetAssetPath(baseAnimator.runtimeAnimatorController), Is.EqualTo(ControllerPath), BasePrefabPath);

            AnimatorController controller = RequireAsset<AnimatorController>(ControllerPath);
            AnimationClip baseIdle = RequireStateClip(controller, "Idle");
            AnimationClip baseTalk = RequireStateClip(controller, "Talk");

            foreach (CharacterExpectation character in Characters)
            {
                string prefabPath = CharacterPrefabFolder + "/Character_" + character.PrefabName + ".prefab";
                GameObject prefab = RequireAsset<GameObject>(prefabPath);
                Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant), prefabPath);

                GameObject source = (GameObject)PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                Assert.That(source, Is.Not.Null, prefabPath + " has no base prefab source.");
                Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(BasePrefabPath), prefabPath);

                CharacterIdentity identity = prefab.GetComponent<CharacterIdentity>();
                Assert.That(identity, Is.Not.Null, prefabPath);
                Assert.That(identity.Data, Is.Not.Null, prefabPath);
                Assert.That(identity.Data.Id, Is.EqualTo(character.Id), prefabPath);
                Assert.That(identity.Data.DisplayName, Is.EqualTo(character.DisplayName), prefabPath);
                Assert.That(identity.Data.FaceTexture, Is.Not.Null, prefabPath);
                Assert.That(AssetDatabase.GetAssetPath(identity.Data), Is.EqualTo(
                    CharacterDataFolder + "/CharacterData_" + character.PrefabName + ".asset"));

                Assert.That(prefab.GetComponent<CharacterAppearance>(), Is.Not.Null, prefabPath);
                string faceMaterialPath = Root + "/Materials/Characters/M_Face_" + character.PrefabName + ".mat";
                foreach (string part in new[] { "Face", "Eyes" })
                {
                    Renderer renderer = prefab.GetComponentsInChildren<Renderer>(true)
                        .SingleOrDefault(candidate => candidate.name == part);
                    Assert.That(renderer, Is.Not.Null, prefabPath + " has no " + part + " renderer.");
                    Assert.That(renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath),
                        Is.All.EqualTo(faceMaterialPath), prefabPath + " " + part + " must use the swappable face map.");
                }
                Assert.That(prefab.GetComponent<CharacterAnimation>(), Is.Not.Null, prefabPath);
                Assert.That(prefab.GetComponent<CharacterInteractable>(), Is.Not.Null, prefabPath);

                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null, prefabPath);
                AnimatorOverrideController overrideController = animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.That(overrideController, Is.Not.Null, prefabPath + " must use its per-character Animator Override Controller.");
                Assert.That(AssetDatabase.GetAssetPath(overrideController), Is.EqualTo(character.OverrideControllerPath), prefabPath);
                Assert.That(AssetDatabase.GetAssetPath(overrideController.runtimeAnimatorController), Is.EqualTo(ControllerPath),
                    character.OverrideControllerPath);
                Assert.That(AssetDatabase.GetAssetPath(overrideController[baseIdle]), Is.EqualTo(character.Clips.IdlePath),
                    character.OverrideControllerPath);
                Assert.That(AssetDatabase.GetAssetPath(overrideController[baseTalk]), Is.EqualTo(character.Clips.TalkPath),
                    character.OverrideControllerPath);

                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(overrides);
                Assert.That(overrides.Where(pair => pair.Value != null).Select(pair => pair.Key.name),
                    Is.EquivalentTo(new[] { "Idle", "Talk" }), character.OverrideControllerPath + " must override only Idle and Talk.");
            }
        }

        [Test]
        public void CharacterAndItemDataUseOriginalProjectTextures()
        {
            foreach (CharacterExpectation character in Characters)
            {
                string path = CharacterDataFolder + "/CharacterData_" + character.PrefabName + ".asset";
                CharacterData data = RequireAsset<CharacterData>(path);
                Assert.That(data.Id, Is.EqualTo(character.Id), path);
                Assert.That(data.DisplayName, Is.EqualTo(character.DisplayName), path);
                Assert.That(data.FaceTexture, Is.Not.Null, path);

                string facePath = AssetDatabase.GetAssetPath(data.FaceTexture);
                Assert.That(facePath, Does.StartWith(Root + "/Art/Textures/Faces/"), path + " must use original project face art.");
                Assert.That(data.FaceTexture.width, Is.EqualTo(128), facePath);
                Assert.That(data.FaceTexture.height, Is.EqualTo(128), facePath);
            }

            foreach (ItemExpectation item in Items)
            {
                string path = ItemDataFolder + "/ItemData_" + item.Name + ".asset";
                ItemData data = RequireAsset<ItemData>(path);
                Assert.That(data.Id, Is.EqualTo(item.Id), path);
                Assert.That(data.DisplayName, Is.EqualTo(item.DisplayName), path);
                Assert.That(data.Description, Is.EqualTo(item.Description), path);
                Assert.That(data.Type, Is.EqualTo(item.Type), path);
                Assert.That(data.Stackable, Is.False, path);
                Assert.That(data.MaxStack, Is.EqualTo(1), path);
                Assert.That(data.Icon, Is.Not.Null, path);
                Assert.That(data.WorldPrefab, Is.Not.Null, path);

                string iconPath = AssetDatabase.GetAssetPath(data.Icon);
                Assert.That(iconPath, Is.EqualTo(Root + "/Art/UI/Icons/" + item.IconFile), path + " must use the original project icon.");
                Assert.That(AssetDatabase.GetAssetPath(data.WorldPrefab), Is.EqualTo(
                    Root + "/Prefabs/Interactables/" + item.PickupPrefab + ".prefab"), path);

                TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, iconPath);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), iconPath);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), iconPath);
                Assert.That(importer.mipmapEnabled, Is.False, iconPath);
            }
        }

        [Test]
        public void CharacterTalkAndBlinkFaceMapsArePointFilteredProjectArt()
        {
            foreach (CharacterExpectation character in Characters)
            {
                string path = CharacterDataFolder + "/CharacterData_" + character.PrefabName + ".asset";
                CharacterData data = RequireAsset<CharacterData>(path);
                AssertFaceMap(data.FaceTexture, "_Face.png", path);
                AssertFaceMap(data.TalkFaceTexture, "_FaceTalk.png", path);
                AssertFaceMap(data.BlinkFaceTexture, "_FaceBlink.png", path);
            }
        }

        [Test]
        public void CharacterModelsImportTheirTakesWithTightKeyReduction()
        {
            foreach (ClipSet set in AuthoredClipSets())
            {
                ModelImporter importer = AssetImporter.GetAtPath(set.ModelPath) as ModelImporter;
                Assert.That(importer, Is.Not.Null, set.ModelPath + " must be imported as a model. " + BuildHint);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic), set.ModelPath);
                Assert.That(importer.importAnimation, Is.True, set.ModelPath);
                Assert.That(importer.preserveHierarchy, Is.True, set.ModelPath);
                Assert.That(importer.animationCompression, Is.EqualTo(ModelImporterAnimationCompression.KeyframeReduction),
                    set.ModelPath);
                Assert.That(importer.animationRotationError, Is.LessThanOrEqualTo(MaxTakeCompressionError), set.ModelPath);
                Assert.That(importer.animationPositionError, Is.LessThanOrEqualTo(MaxTakeCompressionError), set.ModelPath);
                Assert.That(importer.animationScaleError, Is.LessThanOrEqualTo(MaxTakeCompressionError), set.ModelPath);
            }
        }

        [Test]
        public void IdleAndTalkClipsAreTheModelTakesWithContractTiming()
        {
            foreach (ClipSet set in AuthoredClipSets())
            {
                GameObject model = RequireAsset<GameObject>(set.ModelPath);
                AnimationClip idle = RequireAsset<AnimationClip>(set.IdlePath);
                AnimationClip talk = RequireAsset<AnimationClip>(set.TalkPath);

                Assert.That(AnimationUtility.GetAnimationClipSettings(idle).loopTime, Is.True, set.IdlePath + " must loop.");
                Assert.That(AnimationUtility.GetAnimationClipSettings(talk).loopTime, Is.False, set.TalkPath + " must play once.");
                Assert.That(idle.length, Is.EqualTo(6f).Within(FrameSeconds * 0.5f), set.IdlePath);
                Assert.That(talk.length, Is.EqualTo(4f).Within(FrameSeconds * 0.5f), set.TalkPath);
                AssertBindingsResolve(idle, model, set.IdlePath);
                AssertBindingsResolve(talk, model, set.TalkPath);
            }
        }

        [Test]
        public void IdleLoopsSeamlesslyAndTalkStartsAndEndsInTheIdlePose()
        {
            foreach (ClipSet set in AuthoredClipSets())
            {
                AnimationClip idle = RequireAsset<AnimationClip>(set.IdlePath);
                AnimationClip talk = RequireAsset<AnimationClip>(set.TalkPath);
                GameObject instance = Object.Instantiate(RequireAsset<GameObject>(set.ModelPath));
                try
                {
                    string[] paths = TransformBindings(idle).Concat(TransformBindings(talk))
                        .Select(binding => binding.path)
                        .Distinct()
                        .ToArray();
                    Dictionary<string, Quaternion> idleStart = SamplePose(idle, instance, 0f, paths);
                    AssertSamePose(idleStart, SamplePose(idle, instance, EndTime(idle), paths), 0.5f, set.IdlePath + " loop seam");
                    AssertSamePose(idleStart, SamplePose(talk, instance, 0f, paths), 3f, set.TalkPath + " first frame vs Idle frame 0");
                    AssertSamePose(idleStart, SamplePose(talk, instance, EndTime(talk), paths), 3f, set.TalkPath + " last frame vs Idle frame 0");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void SevenAnimatorStatesHaveChangingCurvesBoundToRigBones()
        {
            AnimatorController controller = RequireAsset<AnimatorController>(ControllerPath);
            GameObject rig = RequireAsset<GameObject>(RigPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            string[] stateNames = { "Idle", "Walk", "Run", "Turn", "Inspect", "Talk", "Death" };

            Assert.That(stateMachine.states, Has.Length.EqualTo(stateNames.Length));
            Assert.That(stateMachine.defaultState, Is.Not.Null);
            Assert.That(stateMachine.defaultState.name, Is.EqualTo("Idle"));

            foreach (string stateName in stateNames)
            {
                AnimatorState state = stateMachine.states.Select(entry => entry.state)
                    .SingleOrDefault(candidate => candidate.name == stateName);
                Assert.That(state, Is.Not.Null, "Missing Animator state " + stateName + ".");

                AnimationClip clip = state.motion as AnimationClip;
                Assert.That(clip, Is.Not.Null, stateName + " must reference a real animation clip.");
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip)
                    .Where(binding => binding.type == typeof(Transform))
                    .ToArray();
                Assert.That(bindings, Is.Not.Empty, stateName + " has no transform curves.");

                bool hasRigBoneBinding = false;
                bool hasChangingCurve = false;
                foreach (EditorCurveBinding binding in bindings)
                {
                    Transform bone = string.IsNullOrEmpty(binding.path) ? null : rig.transform.Find(binding.path);
                    Assert.That(bone, Is.Not.Null, stateName + " references a missing rig bone path: " + binding.path);
                    hasRigBoneBinding = true;

                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    Assert.That(curve, Is.Not.Null, stateName + " has a missing curve for " + binding.path);
                    Assert.That(curve.length, Is.GreaterThanOrEqualTo(2), stateName + " has no animated keys for " + binding.path);
                    if (curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) > 0.00001f)
                        hasChangingCurve = true;
                }

                Assert.That(hasRigBoneBinding, Is.True, stateName + " does not target the shared rig.");
                Assert.That(hasChangingCurve, Is.True, stateName + " contains only static curves.");
            }
        }

        [Test]
        public void PrototypeSceneIsPresentAndEnabledInBuildSettings()
        {
            RequireAsset<SceneAsset>(ScenePath);
            EditorBuildSettingsScene[] entries = EditorBuildSettings.scenes
                .Where(scene => scene.path == ScenePath)
                .ToArray();

            Assert.That(entries, Has.Length.EqualTo(1), "CharacterPrototype must have one Build Settings entry.");
            Assert.That(entries[0].enabled, Is.True, "CharacterPrototype must be enabled in Build Settings.");
        }

        [Test]
        public void SourceCharacterModelsStayWithinThePs1TriangleBudget()
        {
            string[] paths =
            {
                RigPath,
                Root + "/Art/Characters/AlexeyVoron.fbx",
                Root + "/Art/Characters/ElenaVoron.fbx",
                Root + "/Art/Characters/DrIlyaMorozov.fbx",
                Root + "/Art/Characters/PoliceOfficer.fbx"
            };

            foreach (string path in paths)
            {
                GameObject model = RequireAsset<GameObject>(path);
                var meshes = new HashSet<Mesh>();
                long triangles = 0;

                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    CountTriangles(filter.sharedMesh, meshes, ref triangles, path);
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    CountTriangles(renderer.sharedMesh, meshes, ref triangles, path);

                Assert.That(triangles, Is.InRange(1500L, 5000L), path + " exceeds the 1,500–5,000 triangle character budget.");
            }
        }

        private static void CountTriangles(Mesh mesh, HashSet<Mesh> seenMeshes, ref long total, string modelPath)
        {
            if (mesh == null || !seenMeshes.Add(mesh))
                return;

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                Assert.That(mesh.GetTopology(subMesh), Is.EqualTo(MeshTopology.Triangles), modelPath + " contains non-triangle geometry.");
                total += (long)mesh.GetIndexCount(subMesh) / 3;
            }
        }

        private static IEnumerable<ClipSet> AuthoredClipSets()
        {
            yield return new ClipSet(RigPath, AnimationFolder);
            foreach (CharacterExpectation character in Characters)
                yield return character.Clips;
        }

        private static AnimationClip RequireStateClip(AnimatorController controller, string stateName)
        {
            AnimatorState state = controller.layers[0].stateMachine.states.Select(entry => entry.state)
                .SingleOrDefault(candidate => candidate.name == stateName);
            Assert.That(state, Is.Not.Null, "Missing Animator state " + stateName + ".");
            AnimationClip clip = state.motion as AnimationClip;
            Assert.That(clip, Is.Not.Null, stateName + " must reference a real animation clip.");
            return clip;
        }

        private static IEnumerable<EditorCurveBinding> TransformBindings(AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip).Where(binding => binding.type == typeof(Transform));
        }

        private static void AssertBindingsResolve(AnimationClip clip, GameObject model, string clipPath)
        {
            EditorCurveBinding[] bindings = TransformBindings(clip).ToArray();
            Assert.That(bindings, Is.Not.Empty, clipPath + " has no transform curves.");
            foreach (EditorCurveBinding binding in bindings)
            {
                Transform target = string.IsNullOrEmpty(binding.path) ? model.transform : model.transform.Find(binding.path);
                Assert.That(target, Is.Not.Null, clipPath + " references a path missing from " + model.name + ": " + binding.path);
            }
        }

        private static Dictionary<string, Quaternion> SamplePose(AnimationClip clip, GameObject instance, float time, IEnumerable<string> paths)
        {
            clip.SampleAnimation(instance, time);
            var pose = new Dictionary<string, Quaternion>();
            foreach (string path in paths)
            {
                Transform bone = string.IsNullOrEmpty(path) ? instance.transform : instance.transform.Find(path);
                Assert.That(bone, Is.Not.Null, clip.name + " references a path missing from " + instance.name + ": " + path);
                pose.Add(path, bone.localRotation);
            }
            return pose;
        }

        private static void AssertSamePose(IReadOnlyDictionary<string, Quaternion> expected, IReadOnlyDictionary<string, Quaternion> actual,
            float toleranceDegrees, string context)
        {
            string worstPath = null;
            float worstAngle = 0f;
            foreach (KeyValuePair<string, Quaternion> entry in expected)
            {
                float angle = Quaternion.Angle(entry.Value, actual[entry.Key]);
                if (angle > worstAngle)
                {
                    worstAngle = angle;
                    worstPath = entry.Key;
                }
            }
            Assert.That(worstAngle, Is.LessThan(toleranceDegrees), context + " is off by " + worstAngle.ToString("0.00") + " degrees at " + worstPath);
        }

        private static float EndTime(AnimationClip clip)
        {
            // Sample just before the end so a looping clip cannot wrap back to its first frame.
            return Mathf.Max(0f, clip.length - 0.0001f);
        }

        private static void AssertFaceMap(Texture2D texture, string fileSuffix, string owner)
        {
            Assert.That(texture, Is.Not.Null, owner + " has no *" + fileSuffix + " face map.");
            string texturePath = AssetDatabase.GetAssetPath(texture);
            Assert.That(texturePath, Does.StartWith(FaceArtFolder + "/"), owner + " must use original project face art.");
            Assert.That(texturePath, Does.EndWith(fileSuffix), owner);
            Assert.That(texture.width, Is.EqualTo(128), texturePath);
            Assert.That(texture.height, Is.EqualTo(128), texturePath);

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, texturePath);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), texturePath);
            Assert.That(importer.mipmapEnabled, Is.False, texturePath);
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, "Missing generated asset " + path + ". " + BuildHint);
            return asset;
        }

        private sealed class CharacterExpectation
        {
            public readonly string PrefabName;
            public readonly string Id;
            public readonly string DisplayName;
            public readonly ClipSet Clips;
            public readonly string OverrideControllerPath;

            public CharacterExpectation(string prefabName, string id, string displayName)
            {
                PrefabName = prefabName;
                Id = id;
                DisplayName = displayName;
                Clips = new ClipSet(CharacterArtFolder + "/" + prefabName + ".fbx", AnimationFolder + "/" + prefabName);
                OverrideControllerPath = AnimationFolder + "/" + prefabName + "/AOC_" + prefabName + ".overrideController";
            }
        }

        private sealed class ClipSet
        {
            public readonly string ModelPath;
            public readonly string IdlePath;
            public readonly string TalkPath;

            public ClipSet(string modelPath, string clipFolder)
            {
                ModelPath = modelPath;
                IdlePath = clipFolder + "/Idle.anim";
                TalkPath = clipFolder + "/Talk.anim";
            }
        }

        private sealed class ItemExpectation
        {
            public readonly string Name;
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string Description;
            public readonly ItemType Type;
            public readonly string IconFile;
            public readonly string PickupPrefab;

            public ItemExpectation(string name, string id, string displayName, string description,
                ItemType type, string iconFile, string pickupPrefab)
            {
                Name = name;
                Id = id;
                DisplayName = displayName;
                Description = description;
                Type = type;
                IconFile = iconFile;
                PickupPrefab = pickupPrefab;
            }
        }
    }
}
