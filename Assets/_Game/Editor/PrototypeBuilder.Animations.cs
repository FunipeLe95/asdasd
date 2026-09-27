using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Voron.Editor
{
    public static partial class PrototypeBuilder
    {
        private static readonly string[] CharacterStateNames = { "Idle", "Walk", "Run", "Turn", "Inspect", "Talk", "Death" };
        // Idle and Talk are Blender takes copied out of the character FBX; the other states are generated here.
        private static readonly string[] AuthoredClipNames = { "Idle", "Talk" };

        private static AnimationArtifacts EnsureAnimationArtifacts(GameObject rig)
        {
            RigBoneMap boneMap = DiscoverBones(rig);
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (string clipName in CharacterStateNames)
            {
                string path = CharacterClipPath(clipName);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    EnsurePathIsFree(path);
                    clip = AuthoredClipNames.Contains(clipName)
                        ? CopyModelTake(RigModelPath, clipName)
                        : CreateCharacterClip(clipName, boneMap);
                    AssetDatabase.CreateAsset(clip, path);
                }
                clips.Add(clipName, clip);
            }

            AnimatorController controller = EnsureAnimatorController(CharacterControllerPath, clips);
            return new AnimationArtifacts(controller);
        }

        private static AnimatorOverrideController EnsureCharacterAnimatorOverride(
            CharacterSpec character,
            GameObject model,
            AnimatorController controller)
        {
            EnsureFolder(CharacterAnimationPath(character));
            string modelPath = AssetDatabase.GetAssetPath(model);
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (string clipName in AuthoredClipNames)
            {
                string path = CharacterClipPath(character, clipName);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    EnsurePathIsFree(path);
                    clip = CopyModelTake(modelPath, clipName);
                    AssetDatabase.CreateAsset(clip, path);
                }
                clips.Add(clipName, clip);
            }

            string overridePath = CharacterOverrideControllerPath(character);
            AnimatorOverrideController existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
            if (existing != null)
                return existing;

            EnsurePathIsFree(overridePath);
            var overrideController = new AnimatorOverrideController(controller)
            {
                name = "AOC_" + character.PrefabName
            };
            foreach (string clipName in AuthoredClipNames)
                overrideController[RequireStateClip(controller, clipName)] = clips[clipName];
            AssetDatabase.CreateAsset(overrideController, overridePath);
            return overrideController;
        }

        private static AnimationClip CopyModelTake(string modelPath, string takeName)
        {
            // Blender may name takes "Armature|Idle"; Unity also adds hidden __preview__ clips.
            AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal) && TakeName(clip.name) == takeName)
                .OrderBy(clip => clip.name.Length)
                .ThenBy(clip => clip.name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (source == null)
                throw new InvalidOperationException(modelPath + " has no " + takeName +
                                                    " animation take. Re-export the character art so it contains the Idle and Talk takes.");

            var clip = new AnimationClip();
            EditorUtility.CopySerialized(source, clip);
            clip.name = takeName;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = takeName == "Idle";
            // Idle is authored as a seamless loop; Unity's loop pose blending would only distort it.
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static string TakeName(string clipName)
        {
            int separator = clipName.LastIndexOf('|');
            return separator < 0 ? clipName : clipName.Substring(separator + 1);
        }

        private static AnimationClip RequireStateClip(AnimatorController controller, string stateName)
        {
            AnimatorState state = controller.layers[0].stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(candidate => candidate != null && candidate.name == stateName);
            AnimationClip clip = state != null ? state.motion as AnimationClip : null;
            if (clip == null)
                throw new InvalidOperationException(CharacterControllerPath + " has no " + stateName + " state with an animation clip.");
            return clip;
        }

        private static AnimatorController EnsureAnimatorController(
            string path,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
                return existing;

            EnsurePathIsFree(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            if (controller == null)
                throw new InvalidOperationException("Unity could not create the Animator Controller at " + path);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Inspect", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Talk", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            foreach (string stateName in CharacterStateNames)
            {
                AnimatorState state = stateMachine.AddState(stateName);
                state.motion = clips[stateName];
                state.writeDefaultValues = true;
                states.Add(stateName, state);
            }
            stateMachine.defaultState = states["Idle"];

            AddConditionalTransition(states["Idle"], states["Walk"], "Speed", AnimatorConditionMode.Greater, 0.08f);
            AddConditionalTransition(states["Idle"], states["Run"], "Speed", AnimatorConditionMode.Greater, 1.25f);
            AddConditionalTransition(states["Walk"], states["Idle"], "Speed", AnimatorConditionMode.Less, 0.08f);
            AddConditionalTransition(states["Walk"], states["Run"], "Speed", AnimatorConditionMode.Greater, 1.25f);
            AddConditionalTransition(states["Run"], states["Idle"], "Speed", AnimatorConditionMode.Less, 0.08f);
            AddConditionalTransition(states["Run"], states["Walk"], "Speed", AnimatorConditionMode.Less, 1.25f);

            AddTriggerTransition(stateMachine, states["Death"], "Death");
            AddTriggerTransition(stateMachine, states["Turn"], "Turn");
            AddTriggerTransition(stateMachine, states["Inspect"], "Inspect");
            // Talk starts from Idle's frame-0 pose; a longer blend hides Idle glances without delaying speech (0.4 s in).
            AddTriggerTransition(stateMachine, states["Talk"], "Talk", 0.25f);
            AddExitToIdle(states["Turn"], states["Idle"]);
            AddExitToIdle(states["Inspect"], states["Idle"]);
            AddExitToIdle(states["Talk"], states["Idle"]);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorStateTransition AddConditionalTransition(
            AnimatorState from,
            AnimatorState to,
            string parameter,
            AnimatorConditionMode mode,
            float threshold)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
            transition.AddCondition(mode, threshold, parameter);
            return transition;
        }

        private static void AddTriggerTransition(AnimatorStateMachine stateMachine, AnimatorState state, string trigger, float duration = 0.08f)
        {
            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(state);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddExitToIdle(AnimatorState from, AnimatorState idle)
        {
            AnimatorStateTransition transition = from.AddTransition(idle);
            transition.hasExitTime = true;
            transition.exitTime = 0.95f;
            transition.hasFixedDuration = true;
            transition.duration = 0.08f;
        }

        private static AnimationClip CreateCharacterClip(string clipName, RigBoneMap boneMap)
        {
            float duration = ClipDuration(clipName);
            bool loops = clipName == "Walk" || clipName == "Run";
            int sampleCount = Mathf.Max(5, Mathf.CeilToInt(duration * 12f) + 1);
            var clip = new AnimationClip
            {
                name = clipName,
                frameRate = 12f
            };

            string[] properties = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            foreach (BoneTarget bone in boneMap.Targets)
            {
                var components = new List<Keyframe>[4];
                for (int component = 0; component < components.Length; component++)
                    components[component] = new List<Keyframe>(sampleCount);

                Quaternion toModel = bone.ModelRotation;
                Quaternion fromModel = Quaternion.Inverse(toModel);
                Quaternion previous = bone.BindRotation;
                for (int frame = 0; frame < sampleCount; frame++)
                {
                    float normalized = frame / (float)(sampleCount - 1);
                    float time = duration * normalized;
                    Vector3 offset = GetMotionOffset(clipName, bone.Role, bone.Side, normalized);
                    // Conjugating by the bind model rotation applies the offset about model axes, so bone roll
                    // and the FBX axis conversion cannot change which way a limb swings.
                    Quaternion rotation = bone.BindRotation * (fromModel * Quaternion.Euler(offset) * toModel);
                    if (frame > 0 && Quaternion.Dot(previous, rotation) < 0f)
                        rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                    previous = rotation;

                    components[0].Add(new Keyframe(time, rotation.x));
                    components[1].Add(new Keyframe(time, rotation.y));
                    components[2].Add(new Keyframe(time, rotation.z));
                    components[3].Add(new Keyframe(time, rotation.w));
                }

                for (int component = 0; component < properties.Length; component++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(bone.Path, typeof(Transform), properties[component]));
                    curves.Add(MakeLinearCurve(components[component]));
                }
            }

            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loops;
            settings.loopBlend = loops;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static AnimationCurve MakeLinearCurve(IReadOnlyList<Keyframe> sourceKeys)
        {
            var keys = sourceKeys.ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                float slope;
                if (i == 0)
                    slope = (keys[1].value - keys[0].value) / (keys[1].time - keys[0].time);
                else if (i == keys.Length - 1)
                    slope = (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time);
                else
                    slope = (keys[i + 1].value - keys[i - 1].value) / (keys[i + 1].time - keys[i - 1].time);

                keys[i].inTangent = slope;
                keys[i].outTangent = slope;
            }

            return new AnimationCurve(keys);
        }

        private static float ClipDuration(string clipName)
        {
            switch (clipName)
            {
                case "Walk": return 0.85f;
                case "Run": return 0.58f;
                case "Turn": return 0.72f;
                case "Inspect": return 1.2f;
                case "Death": return 1.6f;
                default: throw new ArgumentOutOfRangeException(nameof(clipName), clipName, "Unknown character motion.");
            }
        }

        // Euler offsets in model space (+X right, +Y up, +Z forward). Positive X tips upright bones forward,
        // swings hanging limbs backward and points feet down; positive Y turns toward +X; positive Z swings
        // hanging limbs toward +X. side is -1 for bones on the -X half of the body and +1 on the +X half.
        private static Vector3 GetMotionOffset(string clipName, string role, float side, float t)
        {
            float phase = t * Mathf.PI * 2f;
            float wave = Mathf.Sin(phase);
            float limbPhase = phase + (side < 0f ? 0f : Mathf.PI);
            float sideWave = Mathf.Sin(limbPhase);
            // Knees flex while the leg swings forward under the body, not at full reach.
            float legSwing = Mathf.Max(0f, -Mathf.Cos(limbPhase));
            float smooth = t * t * (3f - 2f * t);

            switch (clipName)
            {
                case "Walk":
                    if (role == "Hips") return new Vector3(0f, wave * 1.8f, Mathf.Sin(phase * 2f) * 2f);
                    if (role == "Spine" || role == "Chest") return new Vector3(Mathf.Abs(wave) * 1.3f, -wave * 1.5f, 0f);
                    if (role == "Head") return new Vector3(-Mathf.Abs(wave) * 0.8f, wave * 1.2f, 0f);
                    if (role.EndsWith("UpperLeg", StringComparison.Ordinal)) return new Vector3(sideWave * 24f, 0f, 0f);
                    if (role.EndsWith("LowerLeg", StringComparison.Ordinal)) return new Vector3(legSwing * 28f, 0f, 0f);
                    if (role.EndsWith("Foot", StringComparison.Ordinal)) return new Vector3(-sideWave * 8f, 0f, 0f);
                    if (role.EndsWith("UpperArm", StringComparison.Ordinal)) return new Vector3(-sideWave * 18f, 0f, 0f);
                    if (role.EndsWith("LowerArm", StringComparison.Ordinal)) return new Vector3(-Mathf.Max(0f, sideWave) * 9f, 0f, 0f);
                    return Vector3.zero;

                case "Run":
                    if (role == "Hips") return new Vector3(1.5f, wave * 2.5f, Mathf.Sin(phase * 2f) * 3f);
                    if (role == "Spine" || role == "Chest") return new Vector3(5f - Mathf.Abs(wave) * 2f, -wave * 2f, 0f);
                    if (role == "Head") return new Vector3(-5f + Mathf.Abs(wave) * 2f, wave * 1.5f, 0f);
                    if (role.EndsWith("UpperLeg", StringComparison.Ordinal)) return new Vector3(sideWave * 36f, 0f, 0f);
                    if (role.EndsWith("LowerLeg", StringComparison.Ordinal)) return new Vector3(legSwing * 40f, 0f, 0f);
                    if (role.EndsWith("Foot", StringComparison.Ordinal)) return new Vector3(-sideWave * 14f, 0f, 0f);
                    if (role.EndsWith("UpperArm", StringComparison.Ordinal)) return new Vector3(-sideWave * 29f, 0f, 0f);
                    if (role.EndsWith("LowerArm", StringComparison.Ordinal)) return new Vector3(-Mathf.Max(0f, sideWave) * 16f, 0f, 0f);
                    return Vector3.zero;

                case "Turn":
                    // A look over the shoulder; the hips turn little because the legs and planted feet follow them.
                    float turn = Mathf.Sin(Mathf.PI * t);
                    if (role == "Hips") return new Vector3(0f, turn * 15f, 0f);
                    if (role == "Spine" || role == "Chest") return new Vector3(0f, turn * 20f, 0f);
                    if (role == "Head") return new Vector3(0f, turn * 15f, 0f);
                    return Vector3.zero;

                case "Inspect":
                    float inspect = Mathf.Sin(Mathf.PI * t);
                    if (role == "Spine" || role == "Chest") return new Vector3(8f * inspect, 0f, 0f);
                    if (role == "Head") return new Vector3(14f * inspect, 0f, 0f);
                    if (role == "RightUpperArm") return new Vector3(-12f * inspect, 0f, side * 8f * inspect);
                    if (role == "RightLowerArm") return new Vector3(-24f * inspect, 0f, 0f);
                    return Vector3.zero;

                case "Death":
                    if (role == "Hips") return new Vector3(10f * smooth, 0f, 7f * smooth);
                    if (role == "Spine" || role == "Chest") return new Vector3(30f * smooth, 0f, 0f);
                    if (role == "Head") return new Vector3(20f * smooth, 0f, 4f * smooth);
                    if (role.EndsWith("UpperLeg", StringComparison.Ordinal)) return new Vector3(-22f * smooth, 0f, side * 8f * smooth);
                    if (role.EndsWith("LowerLeg", StringComparison.Ordinal)) return new Vector3(28f * smooth, 0f, 0f);
                    if (role.EndsWith("UpperArm", StringComparison.Ordinal)) return new Vector3(18f * smooth, 0f, side * 35f * smooth);
                    return Vector3.zero;

                default:
                    return Vector3.zero;
            }
        }

        private static RigBoneMap DiscoverBones(GameObject rig)
        {
            Transform[] transforms = rig.GetComponentsInChildren<Transform>(true);
            var skinned = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh != null)
                .ToArray();
            var boneSet = new HashSet<Transform>(skinned.SelectMany(renderer => renderer.bones).Where(bone => bone != null));
            Transform[] skeleton = boneSet.Count > 0 ? boneSet.ToArray() : transforms;

            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            AddBone(map, "Hips", FindNamedBone(skeleton, "hips", "pelvis", "hip"));
            AddBone(map, "Spine", FindNamedBone(skeleton, "spine", "spine1", "lowerback", "torso"));
            AddBone(map, "Chest", FindNamedBone(skeleton, "chest", "spine2", "upperchest", "thorax"));
            AddBone(map, "Neck", FindNamedBone(skeleton, "neck"));
            AddBone(map, "Head", FindNamedBone(skeleton, "head"));
            AddBone(map, "LeftUpperLeg", FindNamedBone(skeleton, "leftupperleg", "leftupleg", "leftthigh", "thighl", "upperlegl", "thighleft"));
            AddBone(map, "RightUpperLeg", FindNamedBone(skeleton, "rightupperleg", "rightupleg", "rightthigh", "thighr", "upperlegr", "thighright"));
            AddBone(map, "LeftLowerLeg", FindNamedBone(skeleton, "leftlowerleg", "leftleg", "leftcalf", "leftshin", "calfl", "lowerlegl", "shinl"));
            AddBone(map, "RightLowerLeg", FindNamedBone(skeleton, "rightlowerleg", "rightleg", "rightcalf", "rightshin", "calfr", "lowerlegr", "shinr"));
            AddBone(map, "LeftFoot", FindNamedBone(skeleton, "leftfoot", "footl", "leftankle"));
            AddBone(map, "RightFoot", FindNamedBone(skeleton, "rightfoot", "footr", "rightankle"));
            AddBone(map, "LeftUpperArm", FindNamedBone(skeleton, "leftupperarm", "leftarm", "upperarml", "arml", "upperarmleft"));
            AddBone(map, "RightUpperArm", FindNamedBone(skeleton, "rightupperarm", "rightarm", "upperarmr", "armr", "upperarmright"));
            AddBone(map, "LeftLowerArm", FindNamedBone(skeleton, "leftlowerarm", "leftforearm", "forearml", "lowerarml"));
            AddBone(map, "RightLowerArm", FindNamedBone(skeleton, "rightlowerarm", "rightforearm", "forearmr", "lowerarmr"));

            if (!map.ContainsKey("Hips"))
            {
                Transform rootBone = skinned.Select(renderer => renderer.rootBone).FirstOrDefault(bone => bone != null);
                if (rootBone == null && skeleton.Length > 0)
                    rootBone = skeleton.OrderBy(bone => GetRelativePath(rig.transform, bone).Count(character => character == '/')).FirstOrDefault();
                AddBone(map, "Hips", rootBone);
            }

            if (!map.ContainsKey("Spine"))
                AddBone(map, "Spine", FindCentralBone(skeleton, rig.transform, map.TryGetValue("Hips", out Transform hips) ? hips : null, true));
            if (!map.ContainsKey("Head"))
                AddBone(map, "Head", skeleton.OrderByDescending(bone => rig.transform.InverseTransformPoint(bone.position).y).FirstOrDefault());
            if (!map.ContainsKey("Chest") && map.TryGetValue("Spine", out Transform spine))
                AddBone(map, "Chest", FindCentralBone(skeleton, rig.transform, spine, true));

            if (map.Count < 2)
                throw new InvalidOperationException(RigModelPath + " contains too few named or bound bones to generate skeletal clips.");

            var targets = map
                .Where(pair => pair.Value != null)
                .Select(pair => new BoneTarget(pair.Key, pair.Value, rig.transform, GetRelativePath(rig.transform, pair.Value)))
                .GroupBy(target => target.Path, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            if (targets.Length == 0)
                throw new InvalidOperationException("No transform paths could be read from " + RigModelPath + ".");

            return new RigBoneMap(targets);
        }

        private static Transform FindNamedBone(IEnumerable<Transform> bones, params string[] aliases)
        {
            Transform[] all = bones.Where(bone => bone != null).ToArray();
            foreach (string alias in aliases)
            {
                string target = NormalizeBoneName(alias);
                Transform exact = all.FirstOrDefault(bone => NormalizeBoneName(bone.name) == target);
                if (exact != null)
                    return exact;

                Transform suffix = all.FirstOrDefault(bone => NormalizeBoneName(bone.name).EndsWith(target, StringComparison.Ordinal));
                if (suffix != null)
                    return suffix;
            }
            return null;
        }

        private static Transform FindCentralBone(Transform[] skeleton, Transform modelRoot, Transform parent, bool aboveParent)
        {
            float parentY = parent != null ? modelRoot.InverseTransformPoint(parent.position).y : 0f;
            return skeleton
                .Where(bone => bone != null && bone != parent)
                .Where(bone => parent == null || bone.IsChildOf(parent))
                .Where(bone =>
                {
                    Vector3 local = modelRoot.InverseTransformPoint(bone.position);
                    return Mathf.Abs(local.x) < 0.18f && (!aboveParent || local.y > parentY + 0.08f);
                })
                .OrderBy(bone => Mathf.Abs(modelRoot.InverseTransformPoint(bone.position).x))
                .ThenBy(bone => modelRoot.InverseTransformPoint(bone.position).y)
                .FirstOrDefault();
        }

        private static void AddBone(IDictionary<string, Transform> map, string role, Transform bone)
        {
            if (bone != null && !map.Values.Contains(bone))
                map[role] = bone;
        }

        private static string NormalizeBoneName(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return string.Empty;

            var segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            if (current != root)
                throw new InvalidOperationException("A character bone is outside the imported model root.");

            return string.Join("/", segments.ToArray());
        }

        private static bool HasCompatibleAnimationPaths(GameObject model, RuntimeAnimatorController controller)
        {
            if (model == null || controller == null)
                return false;

            AnimationClip[] clips = controller.animationClips;
            return clips.Length > 0 && clips.All(clip => clip != null &&
                                                         TransformBindings(clip).All(binding => ResolveBindingTarget(model.transform, binding.path) != null));
        }

        private static IEnumerable<EditorCurveBinding> TransformBindings(AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip).Where(binding => binding.type == typeof(Transform));
        }

        private static Transform ResolveBindingTarget(Transform model, string path)
        {
            return string.IsNullOrEmpty(path) ? model : model.Find(path);
        }

        private sealed class RigBoneMap
        {
            public readonly BoneTarget[] Targets;

            public RigBoneMap(BoneTarget[] targets)
            {
                Targets = targets;
            }
        }

        private sealed class BoneTarget
        {
            public readonly string Role;
            public readonly Transform Transform;
            public readonly string Path;
            public readonly Quaternion BindRotation;
            public readonly Quaternion ModelRotation;
            public readonly float Side;

            public BoneTarget(string role, Transform transform, Transform modelRoot, string path)
            {
                Role = role;
                Transform = transform;
                Path = path;
                BindRotation = transform.localRotation;
                ModelRotation = Quaternion.Inverse(modelRoot.rotation) * transform.rotation;
                float x = modelRoot.InverseTransformPoint(transform.position).x;
                Side = Mathf.Abs(x) < 0.02f ? 0f : Mathf.Sign(x);
            }
        }
    }
}
