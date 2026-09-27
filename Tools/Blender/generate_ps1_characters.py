"""Build the authored PS1 characters: textures, rigged FBX files, source blend and reviews.

Run from the repository root with Blender 4.5.3:
    .tools/blender/blender --background --factory-startup --python Tools/Blender/generate_ps1_characters.py
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import character_animations as anim  # noqa: E402
import character_models as models  # noqa: E402
from pixel_art import generate_pixel_art_assets  # noqa: E402

ROOT, ART, COMPONENTS, BONE_NAMES = models.ROOT, models.ART, models.COMPONENTS, models.BONE_NAMES
SOURCE = ROOT / "SourceArt" / "Characters"
ARTIFACTS = ROOT / ".artifacts" / "character-pipeline"
MEDIA = ROOT / "Docs" / "Media"
FACES = ART / "Textures" / "Faces"
MAX_FACE_SHELL_TRIANGLES = 500
OBSOLETE_BASE_ID = "CharacterRig"
BASE_ID = "CharacterBase"
SPACING = 1.0
STRIDE = (("LeftUpperLeg", -16), ("RightUpperLeg", 13), ("LeftLowerLeg", 12), ("RightLowerLeg", 4),
          ("LeftUpperArm", 9), ("RightUpperArm", -9), ("LeftLowerArm", -9), ("RightLowerArm", -12))
# Preview-only blink frames per character (the game blinks at random intervals).
PREVIEW_BLINKS = {"CharacterBase": (40, 118), "AlexeyVoron": (22, 96), "ElenaVoron": (60, 130),
                  "DrIlyaMorozov": (10, 84), "PoliceOfficer": (50, 104)}


def _clear_scene():
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for child in list(bpy.context.scene.collection.children):
        bpy.context.scene.collection.children.unlink(child)
        bpy.data.collections.remove(child)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def _scene_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def _mesh_metrics(obj):
    triangles = sum(max(0, polygon.loop_total - 2) for polygon in obj.data.polygons)
    influences = []
    for vertex in obj.data.vertices:
        vertex_weights = [membership.weight for membership in vertex.groups if membership.weight > 1e-5]
        influences.append(len(vertex_weights))
        total = sum(vertex_weights)
        if abs(total - 1.0) > 0.002:
            raise ValueError(f"{obj.name} vertex {vertex.index} has non-normalized weights: {total:.5f}")
        if len(vertex_weights) > 4:
            raise ValueError(f"{obj.name} vertex {vertex.index} has more than four influences")
    if len(obj.data.uv_layers) != 1 or len(obj.data.uv_layers[0].data) != len(obj.data.loops):
        raise ValueError(f"{obj.name} has a missing or incomplete UV map")
    uv_values = [tuple(loop.uv) for loop in obj.data.uv_layers[0].data]
    if not uv_values or any(not all(math.isfinite(axis) and -0.001 <= axis <= 1.001 for axis in uv)
                             for uv in uv_values):
        raise ValueError(f"{obj.name} has UV coordinates outside the 0..1 texture range")
    if not obj.modifiers or not any(mod.type == "ARMATURE" and mod.object for mod in obj.modifiers):
        raise ValueError(f"{obj.name} has no bound armature modifier")
    return {
        "vertices": len(obj.data.vertices), "triangles": triangles,
        "max_influences": max(influences, default=0), "uv_map": obj.data.uv_layers[0].name,
        "weights_normalized": True,
    }


def _validate_record(record):
    armature = record["armature"]
    if set(record["objects"]) != set(COMPONENTS):
        raise ValueError(f"{record['id']}: component set does not match the art contract")
    bones = armature.data.bones
    deform = {bone.name for bone in bones if bone.use_deform}
    if deform != set(BONE_NAMES) or any(not name.startswith("CTRL_") for name in set(bones.keys()) - deform):
        raise ValueError(f"{record['id']}: deform bones differ from the shared rig")
    for name, (_, _, parent) in models.BONE_DEFINITIONS.items():
        if (bones[name].parent.name if bones[name].parent else None) != parent:
            raise ValueError(f"{record['id']}: invalid bone parent for {name}")
    meshes, heights = {}, []
    for component in COMPONENTS:
        obj = record["objects"][component]
        if obj.parent != armature:
            raise ValueError(f"{record['id']}/{component}: mesh must be parented to the shared rig")
        meshes[component] = _mesh_metrics(obj)
        heights.extend(vertex.co.z for vertex in obj.data.vertices)
    triangle_count = sum(item["triangles"] for item in meshes.values())
    if meshes["Face"]["triangles"] > MAX_FACE_SHELL_TRIANGLES:
        raise ValueError(f"{record['id']}: face texture shell exceeds the low-poly limit")
    if not 1500 <= triangle_count <= 5000:
        raise ValueError(f"{record['id']}: {triangle_count} triangles is outside the 1,500-5,000 target")
    height = max(heights) - min(heights)
    if not 1.65 <= height <= 1.98:
        raise ValueError(f"{record['id']}: local model height {height:.3f} m is outside the expected range")
    return {"id": record["id"], "display_name": record["display_name"],
            "file": f"Assets/_Game/Art/Characters/{record['id']}.fbx",
            "total_triangles": triangle_count, "height_m": round(height, 4), "components": meshes,
            "bones": list(BONE_NAMES), "rest_pose": "Neutral standing; arms rest alongside the torso.",
            "rig_path": "Armature/Hips/Spine/Chest/Neck/Head"}


def _tree(objects):
    vertices, polygons = [], []
    for obj in objects:
        offset = len(vertices)
        vertices.extend(vertex.co.copy() for vertex in obj.data.vertices)
        polygons.extend(tuple(offset + index for index in polygon.vertices) for polygon in obj.data.polygons)
    return BVHTree.FromPolygons(vertices, polygons)


def _geometry_checks(record):
    """Guards against the failures seen in earlier art: floating feet, hidden skin, covered faces."""
    objects, name = record["objects"], record["id"]
    lowest = min(vertex.co.z for obj in objects.values() for vertex in obj.data.vertices)
    if not -1e-5 <= lowest <= 0.002:
        raise ValueError(f"{name}: soles must rest on the ground plane; lowest point is {lowest:.4f} m")
    if any(0.90 < vertex.co.z < 1.435 for vertex in objects["Body"].data.vertices):
        raise ValueError(f"{name}: torso skin under the clothing was reintroduced")
    trees = {component: _tree([obj]) for component, obj in objects.items()}
    for x in (-0.03, 0.0, 0.03):
        for z in (1.6615, 1.625, 1.594):
            px, _, pz = record["head_to_world"]((x, 0.0, z))
            hits = []
            for component, tree in trees.items():
                location, _, _, distance = tree.ray_cast(Vector((px, -1.0, pz)), Vector((0.0, 1.0, 0.0)), 2.0)
                if location is not None:
                    hits.append((distance, component))
            first = min(hits)[1] if hits else None
            if first not in ("Face", "Eyes", "Accessories"):
                raise ValueError(f"{name}: {first} hides the face at x={px:.3f}, z={pz:.3f}")
    rays = 0
    if record["crown_hair"]:
        scalp = _tree([objects["Head"], objects["Face"]])
        for z in (1.752, 1.762, 1.772):
            for step in range(36):
                angle = (step + 0.5) * math.tau / 36
                origin, direction = Vector((0.0, 0.0, z)), Vector((math.cos(angle), math.sin(angle), 0.0))
                scalp_hit, hair_hit = scalp.ray_cast(origin, direction, 0.3), trees["Hair"].ray_cast(origin, direction, 0.3)
                if scalp_hit[0] is None or hair_hit[0] is None or hair_hit[3] <= scalp_hit[3] + 0.001:
                    raise ValueError(f"{name}: scalp shows through the hair at z={z}, ray {step}")
                rays += 1
    print(f"{name}: geometry checks passed (ground contact, exposed skin, face visibility, {rays} scalp rays)")


def _studio(samples, percentage):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 4
    scene.render.resolution_percentage = percentage
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Review World")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (0.24, 0.24, 0.24, 1.0)
    background.inputs[1].default_value = 0.7
    studio = _scene_collection("Neutral review studio")
    for name, location, power, size in (("Large soft key", (-1.6, -5.0, 5.0), 520, 7.0),
                                        ("Neutral fill", (2.6, -3.4, 2.6), 220, 6.0),
                                        ("Edge separation", (0.0, 3.4, 4.0), 300, 7.0)):
        light = bpy.data.lights.new(name, "AREA")
        light.energy, light.size, light.shape = power, size, "DISK"
        obj = bpy.data.objects.new(name, light)
        studio.objects.link(obj)
        obj.location = location
        obj.rotation_euler = (Vector((0.0, 0.0, 1.0)) - obj.location).to_track_quat("-Z", "Y").to_euler()
    mesh = bpy.data.meshes.new("Neutral matte floor")
    mesh.from_pydata([(-100, -100, 0), (100, -100, 0), (100, 100, 0), (-100, 100, 0)], [], [(0, 1, 2, 3)])
    floor = bpy.data.objects.new("Neutral matte floor", mesh)
    studio.objects.link(floor)
    floor.is_shadow_catcher = True
    floor_material = bpy.data.materials.new("Studio gray")
    floor_material.use_nodes = True
    principled = floor_material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (0.16, 0.17, 0.17, 1.0)
    principled.inputs["Roughness"].default_value = 0.95
    mesh.materials.append(floor_material)
    camera = bpy.data.objects.new("Review Camera", bpy.data.cameras.new("Review Camera"))
    studio.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.clip_end = 100.0
    scene.camera = camera
    return camera


def _render(camera, path, resolution, target, scale):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = resolution
    target = Vector(target)
    camera.location = target + Vector((0.0, -6.0, 0.05))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = scale
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    return path


def _turn(records, degrees):
    for record in records:
        record["armature"].rotation_euler = (0.0, 0.0, math.radians(degrees))
    bpy.context.view_layer.update()


def _pose(records, rotations):
    for record in records:
        for bone in record["armature"].pose.bones:
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (0.0, 0.0, 0.0)
        for bone_name, degrees in rotations:
            record["armature"].pose.bones[bone_name].rotation_euler = (math.radians(degrees), 0.0, 0.0)
    bpy.context.view_layer.update()


def _compose(paths, columns, output):
    import numpy as np

    images = [bpy.data.images.load(str(path), check_existing=False) for path in paths]
    width, height = images[0].size
    rows = (len(images) + columns - 1) // columns
    sheet = np.zeros((rows * height, columns * width, 4), dtype=np.float32)
    for index, image in enumerate(images):
        pixels = np.empty(width * height * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        row, column = divmod(index, columns)
        top = (rows - 1 - row) * height  # Blender image rows start at the bottom.
        sheet[top:top + height, column * width:(column + 1) * width] = pixels.reshape(height, width, 4)
        bpy.data.images.remove(image)
    result = bpy.data.images.new("Review sheet", columns * width, rows * height, alpha=True)
    result.pixels.foreach_set(sheet.ravel())
    result.filepath_raw = str(output)
    result.file_format = "PNG"
    result.save()
    bpy.data.images.remove(result)
    return output


def _publish(source, name):
    scene = bpy.context.scene
    image = bpy.data.images.load(str(source), check_existing=False)
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    MEDIA.mkdir(parents=True, exist_ok=True)
    image.save_render(str(MEDIA / name), scene=scene)
    scene.render.image_settings.file_format = "PNG"
    bpy.data.images.remove(image)


def _render_reviews(records, camera, publish):
    lineup = {}
    for name, degrees in (("front", 0), ("three-quarter", 35), ("profile", 90), ("back", 180)):
        _turn(records, degrees)
        lineup[name] = _render(camera, ARTIFACTS / f"lineup-{name}.png", (2100, 840), (0.0, 0.0, 0.95), 5.3)
    _pose(records, STRIDE)
    _turn(records, 35)
    lineup["stride"] = _render(camera, ARTIFACTS / "lineup-stride.png", (2100, 840), (0.0, 0.0, 0.95), 5.3)
    _pose(records, ())
    heads = []
    for degrees in (0, 35):
        _turn(records, degrees)
        for record in records:
            target = (record["armature"].location.x, 0.0, record["head_to_world"]((0.0, 0.0, 1.668))[2])
            heads.append(_render(camera, ARTIFACTS / f"head-{record['id']}-{degrees}.png", (420, 420), target, 0.34))
    _turn(records, 0)
    faces = _compose(heads, len(records), ARTIFACTS / "faces.png")
    if publish:
        for source, name in ((lineup["front"], "CharacterLineupFront.jpg"),
                             (lineup["three-quarter"], "CharacterLineupThreeQuarter.jpg"),
                             (lineup["back"], "CharacterLineupBack.jpg"), (lineup["stride"], "CharacterStride.jpg"),
                             (faces, "CharacterFaces.jpg")):
            _publish(source, name)
    print("Review renders: " + ", ".join(str(path) for path in list(lineup.values()) + [faces]))


def _remove_obsolete_base_outputs():
    obsolete_outputs = (
        (ART / "Characters" / f"{OBSOLETE_BASE_ID}.fbx", ART / "Characters" / f"{BASE_ID}.fbx"),
        (ART / "Textures" / "Faces" / f"{OBSOLETE_BASE_ID}_Face.png",
         ART / "Textures" / "Faces" / f"{BASE_ID}_Face.png"),
        (ART / "Textures" / "Characters" / f"{OBSOLETE_BASE_ID}_Clothing.png",
         ART / "Textures" / "Characters" / f"{BASE_ID}_Clothing.png"),
    )
    migrations = []
    for source, destination in obsolete_outputs:
        source_meta = Path(f"{source}.meta")
        destination_meta = Path(f"{destination}.meta")
        if source_meta.exists():
            if destination_meta.exists():
                raise FileExistsError(f"Both legacy and renamed Unity metadata exist: {source_meta}, {destination_meta}")
            migrations.append((source_meta, destination_meta))
    for source_meta, destination_meta in migrations:
        source_meta.rename(destination_meta)
    for source, _ in obsolete_outputs:
        source.unlink(missing_ok=True)


def _export_one(record, filepath):
    filepath.parent.mkdir(parents=True, exist_ok=True)
    armature = record["armature"]
    # Each NLA track becomes one FBX take; control bones only drive the baked pose.
    anim.play(record, None)
    objects = [armature] + [record["objects"][name] for name in COMPONENTS]
    original_names = {obj: obj.name for obj in objects}
    original_location = armature.location.copy()
    if any(abs(angle) > 1e-6 for angle in armature.rotation_euler):
        raise RuntimeError(f"{record['id']}: review rotation leaked into the export")
    for index, obj in enumerate(objects):
        obj.name = f"__EXPORT_{record['id']}_{index}"
    armature.location = (0.0, 0.0, 0.0)
    armature.name = "Armature"
    for component in COMPONENTS:
        record["objects"][component].name = component
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(filepath), use_selection=True,
            object_types={"ARMATURE", "MESH"},
            apply_unit_scale=True, global_scale=1.0,
            axis_forward="-Z", axis_up="Y",
            use_space_transform=True, bake_space_transform=False,
            use_mesh_modifiers=True, mesh_smooth_type="OFF",
            add_leaf_bones=False, use_armature_deform_only=True,
            armature_nodetype="NULL", bake_anim=True, bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
            bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.1,
            path_mode="RELATIVE", embed_textures=False,
        )
    finally:
        for index, obj in enumerate(objects):
            obj.name = f"__RESTORE_{record['id']}_{index}"
        for obj, original_name in original_names.items():
            obj.name = original_name
        armature.location = original_location
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
    if not filepath.is_file() or filepath.stat().st_size < 4096:
        raise RuntimeError(f"FBX export did not produce a usable file: {filepath}")


def _material_image_size(mesh):
    for slot in mesh.material_slots:
        if slot.material and slot.material.use_nodes:
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    return tuple(node.image.size[:])
    return None


def _render_frames(camera, records, clip, frames, directory, shots, turn):
    """Render per-frame shots of one clip with preview face swaps; returns frame paths per shot."""
    directory.mkdir(parents=True, exist_ok=True)
    swaps = {record["id"]: _face_swapper(record) for record in records}
    for record in records:
        anim.play(record, clip)
    paths = {name: [] for name in shots}
    for index, frame in enumerate(frames):
        bpy.context.scene.frame_set(frame)
        _turn(records, turn)
        for record in records:
            blinking = any(start <= frame < start + 3 for start in PREVIEW_BLINKS[record["id"]])
            speaking = clip == "Talk" and anim.mouth_open(frame / anim.TALK_FRAMES, anim.TALK_FRAMES / anim.FPS)
            swaps[record["id"]](speaking, blinking)
        for name, (resolution, target, scale) in shots.items():
            paths[name].append(_render(camera, directory / f"{name}-{index:04d}.png", resolution, target, scale))
    _turn(records, 0)
    for record in records:
        anim.play(record, None)
        swaps[record["id"]](False, False)
    return paths


def _face_swapper(record):
    face = record["objects"]["Face"].data.materials[0]
    eyes = record["objects"]["Eyes"].data
    if eyes.materials[0] == face:
        eyes.materials[0] = face.copy()
    images = {state: models.load_image(FACES / f"{record['id']}_{suffix}.png")
              for state, suffix in (("neutral", "Face"), ("talk", "FaceTalk"), ("blink", "FaceBlink"))}

    def texture(material):
        return next(node for node in material.node_tree.nodes if node.type == "TEX_IMAGE")

    def apply(speaking, blinking):
        texture(face).image = images["talk" if speaking else "neutral"]
        texture(eyes.materials[0]).image = images["blink" if blinking else "neutral"]
    return apply


def _grid_frames(tiles, columns, output_pattern):
    """Compose equally sized tile sequences into one sequence (tile lists share frame counts)."""
    import numpy as np

    outputs = []
    for index in range(len(tiles[0])):
        images = [bpy.data.images.load(str(sequence[index]), check_existing=False) for sequence in tiles]
        width, height = images[0].size
        rows = (len(images) + columns - 1) // columns
        sheet = np.zeros((rows * height, columns * width, 4), dtype=np.float32)
        for position, image in enumerate(images):
            pixels = np.empty(width * height * 4, dtype=np.float32)
            image.pixels.foreach_get(pixels)
            row, column = divmod(position, columns)
            top = (rows - 1 - row) * height
            sheet[top:top + height, column * width:(column + 1) * width] = pixels.reshape(height, width, 4)
            bpy.data.images.remove(image)
        result = bpy.data.images.new("Preview frame", columns * width, rows * height, alpha=True)
        result.pixels.foreach_set(sheet.ravel())
        result.filepath_raw = str(output_pattern % index)
        result.file_format = "PNG"
        result.save()
        bpy.data.images.remove(result)
        outputs.append(output_pattern % index)
    return outputs


def _crop_frames(paths, columns, output_pattern):
    """Split each wide frame into equal-width column tiles."""
    import numpy as np

    tiles = [[] for _ in range(columns)]
    for index, path in enumerate(paths):
        image = bpy.data.images.load(str(path), check_existing=False)
        width, height = image.size
        pixels = np.empty(width * height * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        pixels = pixels.reshape(height, width, 4)
        bpy.data.images.remove(image)
        step = width // columns
        for column in range(columns):
            tile = bpy.data.images.new("Preview tile", step, height, alpha=True)
            tile.pixels.foreach_set(np.ascontiguousarray(pixels[:, column * step:(column + 1) * step]).ravel())
            tile.filepath_raw = str(output_pattern % (column, index))
            tile.file_format = "PNG"
            tile.save()
            bpy.data.images.remove(tile)
            tiles[column].append(output_pattern % (column, index))
    return tiles


def _encode(pattern, fps, gif, mp4=None):
    ffmpeg = shutil.which("ffmpeg")
    if ffmpeg is None:
        raise RuntimeError("ffmpeg is required to encode animation previews")
    subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", str(pattern),
                    "-vf", "split[a][b];[a]palettegen=max_colors=160:stats_mode=diff[p];"
                           "[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
                    "-loop", "0", str(gif)], check=True)
    if mp4:
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", str(pattern),
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-movflags", "+faststart",
                        str(mp4)], check=True)


def _render_animation_previews(records, camera, publish, scale=1.0, samples=6):
    """Idle lineup and a Talk grid (four story characters, base figure, Alexey close-up)."""
    frames_dir = ARTIFACTS / "animation-frames"
    shutil.rmtree(frames_dir, ignore_errors=True)
    scene = bpy.context.scene
    cycles = scene.cycles
    # Denoised low-sample, short-bounce renders keep ~180 preview frames near 20 minutes on CPU.
    cycles.samples = samples
    cycles.max_bounces, cycles.diffuse_bounces, cycles.glossy_bounces = 3, 2, 1
    cycles.transmission_bounces, cycles.volume_bounces, cycles.transparent_max_bounces = 0, 0, 2
    scene.render.use_persistent_data = True
    size = lambda w, h: (int(w * scale) // 2 * 2, int(h * scale) // 2 * 2)  # noqa: E731
    idle = _render_frames(camera, records, "Idle", range(0, anim.IDLE_FRAMES, 2), frames_dir / "idle",
                          {"lineup": (size(1050, 420), (0.0, 0.0, 0.95), 5.3)}, 20)
    alexey = next(record for record in records if record["id"] == "AlexeyVoron")
    head = alexey["head_to_world"]((0.0, 0.0, 1.655))
    talk = _render_frames(camera, records, "Talk", range(0, anim.TALK_FRAMES + 1, 2), frames_dir / "talk",
                          {"upper": (size(1500, 320), (0.0, 0.0, 1.43), 5.0),
                           "close": (size(300, 320), (alexey["armature"].location.x, 0.0, head[2]), 0.40)}, 15)
    tiles = _crop_frames(talk["upper"], len(records), str(frames_dir / "talk" / "tile-%d-%04d.png"))
    order = [tiles[i] for i in (1, 2, 3, 4, 0)] + [talk["close"]]
    grid = _grid_frames(order, 3, str(frames_dir / "talk" / "grid-%04d.png"))
    # Every second 24 fps frame, played at 12 fps: real-time speed.
    outputs = {"idle": (frames_dir / "idle" / "lineup-%04d.png", 12), "talk": (frames_dir / "talk" / "grid-%04d.png", 12)}
    results = {}
    for name, (pattern, fps) in outputs.items():
        gif, mp4 = ARTIFACTS / f"character-{name}.gif", ARTIFACTS / f"character-{name}.mp4"
        _encode(pattern, fps, gif, mp4)
        results[name] = gif
        if publish:
            shutil.copyfile(gif, MEDIA / f"Character{name.title()}.gif")
    print(f"Animation previews: {results['idle']}, {results['talk']} ({len(grid)} talk frames)")


def _validate_exported_fbxs(records):
    # Reload each FBX into an empty scene so checks cover what an importer receives.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = anim.FPS
    for record in records:
        filepath = ART / "Characters" / f"{record['id']}.fbx"
        existing_actions = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=str(filepath), use_anim=True, anim_offset=0.0)
        imported = list(bpy.context.scene.objects)
        armatures = [obj for obj in imported if obj.type == "ARMATURE"]
        meshes = {obj.name: obj for obj in imported if obj.type == "MESH"}
        if len(armatures) != 1:
            raise ValueError(f"{filepath.name}: expected one imported armature, found {len(armatures)}")
        if not set(COMPONENTS).issubset(meshes):
            raise ValueError(f"{filepath.name}: missing named meshes {set(COMPONENTS) - set(meshes)}")
        bones = armatures[0].data.bones
        for name, (_, _, parent) in models.BONE_DEFINITIONS.items():
            if name not in bones or (bones[name].parent.name if bones[name].parent else None) != parent:
                raise ValueError(f"{filepath.name}: shared skeleton bone {name} is missing or re-parented")
        triangles = 0
        for component in COMPONENTS:
            mesh = meshes[component]
            if not mesh.data.vertices or not mesh.vertex_groups or mesh.find_armature() != armatures[0]:
                raise ValueError(f"{filepath.name}/{component}: mesh is not skinned to the shared armature")
            if any(group.name not in BONE_NAMES for group in mesh.vertex_groups):
                raise ValueError(f"{filepath.name}/{component}: vertex group is outside the shared rig")
            for vertex in mesh.data.vertices:
                weights = [group.weight for group in vertex.groups if group.weight > 1e-5]
                if not weights or len(weights) > 4 or abs(sum(weights) - 1.0) > 0.01:
                    raise ValueError(f"{filepath.name}/{component}: imported vertex weights are invalid")
            triangles += sum(max(0, polygon.loop_total - 2) for polygon in mesh.data.polygons)
        if not 1500 <= triangles <= 5000:
            raise ValueError(f"{filepath.name}: imported triangle count is {triangles}")
        for component, size in (("Face", (128, 128)), ("Eyes", (128, 128)), ("Clothing", (256, 256))):
            if _material_image_size(meshes[component]) != size:
                raise ValueError(f"{filepath.name}: {component} lost its {size[0]} px texture in FBX export")
        points = [obj.matrix_world @ Vector(corner) for obj in meshes.values() for corner in obj.bound_box]
        extent = max(point.z for point in points) - min(point.z for point in points)
        if not 1.65 <= extent <= 1.98:
            raise ValueError(f"{filepath.name}: imported vertical extent is {extent:.3f} m")
        body = meshes["Body"]
        before = [body.matrix_world @ v.co for v in body.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().vertices]
        probe = armatures[0].pose.bones["LeftUpperArm"]
        imported_mode = probe.rotation_mode
        probe.rotation_mode = "XYZ"
        probe.rotation_euler[0] = 0.35
        bpy.context.view_layer.update()
        after = [body.matrix_world @ v.co for v in body.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().vertices]
        if max((a - b).length for a, b in zip(after, before)) < 0.01:
            raise ValueError(f"{filepath.name}: rotating LeftUpperArm did not deform the skinned body")
        probe.rotation_euler[0] = 0.0
        probe.rotation_mode = imported_mode
        _validate_takes(record, armatures[0], set(bpy.data.actions) - existing_actions, filepath.name)
        for obj in imported:
            bpy.data.objects.remove(obj, do_unlink=True)
        for armature in list(bpy.data.armatures):
            if armature.users == 0:
                bpy.data.armatures.remove(armature)


def _validate_takes(record, armature, actions, label):
    """Imported Idle/Talk takes must reproduce the source poses joint by joint."""
    worst = (0.0, 0.0)
    for clip, frames in (("Idle", anim.IDLE_FRAMES), ("Talk", anim.TALK_FRAMES)):
        # Importer names are "Armature|Idle", with ".001" suffixes after earlier imports.
        matches = [action for action in actions if action.name.split("|")[-1].split(".")[0] == clip]
        if len(matches) != 1:
            raise ValueError(f"{label}: expected one {clip} take, found {[a.name for a in actions]}")
        action = matches[0]
        first, last = action.frame_range
        if round(first) != 0 or round(last) != frames:
            raise ValueError(f"{label}: {clip} take spans frames {first:.1f}-{last:.1f}, expected 0-{frames}")
        data = armature.animation_data or armature.animation_data_create()
        data.action = action
        if getattr(data, "action_slot", None) is None and hasattr(action, "slots") and len(action.slots):
            data.action_slot = action.slots[0]
        world = armature.matrix_world.to_quaternion()
        for frame, reference in record["clip_references"][clip].items():
            bpy.context.scene.frame_set(frame)
            evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
            current = {}
            for name in anim.SAMPLE_BONES:
                bone = evaluated.pose.bones[name]
                delta = (world @ bone.matrix.to_quaternion()) @ (world @ evaluated.data.bones[name].matrix_local.to_quaternion()).inverted()
                current[name] = (armature.matrix_world @ bone.head, delta)
            distance, angle = anim.compare(reference, current)
            worst = (max(worst[0], distance), max(worst[1], angle))
            if distance > 0.003 or angle > 1.5:
                raise ValueError(f"{label}: {clip} frame {frame} differs from the source "
                                 f"({distance * 1000:.1f} mm, {angle:.2f}°)")
        data.action = None
    for action in actions:
        bpy.data.actions.remove(action)
    print(f"{label}: Idle/Talk takes match the source within {worst[0] * 1000:.2f} mm and {worst[1]:.2f}°")


def _write_metrics(metrics, fbx_validated):
    SOURCE.mkdir(parents=True, exist_ok=True)
    result = {
        "schema": "voron-character-art-metrics-2",
        "blender_version": bpy.app.version_string,
        "coordinate_system": {
            "source": "Blender Z-up; characters face local -Y",
            "fbx_export": "Y-up, -Z forward axis conversion for Unity +Z-forward scenes",
            "unity_height_m": 1.8,
            "ground": "Y=0 after FBX axis conversion",
        },
        "components": list(COMPONENTS),
        "characters": metrics,
        "texture_paths": {
            "faces": "Assets/_Game/Art/Textures/Faces/*_Face.png, *_FaceTalk.png, *_FaceBlink.png "
                     "(128x128, Face and Eyes meshes)",
            "clothing": "Assets/_Game/Art/Textures/Characters/*_Clothing.png (256x256 atlas for other meshes)",
            "ui_icons": "Assets/_Game/Art/UI/Icons/*_32.png (32x32)",
        },
        "animations": {
            "fps": anim.FPS,
            "takes": {"Idle": {"frames": anim.IDLE_FRAMES, "loop": True},
                      "Talk": {"frames": anim.TALK_FRAMES, "loop": False,
                               "speech_windows": [list(window) for window in anim.SPEECH_WINDOWS]}},
            "checks": ["idle loops", "talk starts and ends in the idle pose", "feet planted",
                       "hand holds reached", "FBX takes match the source poses"],
        },
        "quality_checks": {
            "weights_normalized": True, "max_influences": 4, "uv_range": "0..1",
            "one_shared_armature_per_fbx": True,
            "geometry_review": ["soles on the ground", "no torso skin under clothes",
                                "face unobstructed", "crown hair covers the scalp"],
            "fbx_round_trip_import": fbx_validated,
            "pose_deformation_smoke_test": fbx_validated,
        },
    }
    (SOURCE / "character_metrics.json").write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def build(args):
    if bpy.app.version < (4, 1, 0):
        raise RuntimeError("Blender 4.1 or newer is required; production output is pinned to Blender 4.5.3.")
    _clear_scene()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    (ART / "Characters").mkdir(parents=True, exist_ok=True)
    _remove_obsolete_base_outputs()
    generate_pixel_art_assets(ART)
    root = _scene_collection("Characters")
    count = len(models.CHARACTERS)
    records = [models.make_character(spec, root, (index - (count - 1) / 2) * SPACING)
               for index, spec in enumerate(models.CHARACTERS)]
    metrics = [_validate_record(record) for record in records]
    for record in records:
        _geometry_checks(record)
    camera = _studio(args.samples, args.resolution)
    if not args.skip_render:
        _render_reviews(records, camera, args.publish_media)
    for record in records:
        anim.build_clips(record)
        anim.check_clips(record)
    SOURCE.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "PS1CharacterPipeline.blend"), compress=True)
    for record in records:
        _export_one(record, ART / "Characters" / f"{record['id']}.fbx")
    if args.animation_previews:
        _render_animation_previews(records, camera, args.publish_media, args.preview_scale, args.preview_samples)
    if not args.skip_fbx_validation:
        _validate_exported_fbxs(records)
    _write_metrics(metrics, fbx_validated=not args.skip_fbx_validation)
    print("CHARACTER ART BUILD OK")
    for entry in metrics:
        print(f"{entry['id']} ({entry['display_name']}): {entry['total_triangles']} triangles, "
              f"{entry['height_m']:.3f} m")


def _arguments():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-render", action="store_true", help="Build assets without Cycles review renders")
    parser.add_argument("--skip-fbx-validation", action="store_true",
                        help="Skip re-importing exported FBXs; for diagnosing importer failures only")
    parser.add_argument("--publish-media", action="store_true", help="Copy inspected reviews into Docs/Media")
    parser.add_argument("--animation-previews", action="store_true", help="Render Idle and Talk GIF/MP4 previews")
    parser.add_argument("--preview-scale", type=float, default=1.0, help="Resolution factor for animation previews")
    parser.add_argument("--preview-samples", type=int, default=6, help="Cycles samples for animation previews")
    parser.add_argument("--samples", type=int, default=32, help="Cycles samples for review renders")
    parser.add_argument("--resolution", type=int, default=100, help="Review render resolution percentage")
    return parser.parse_args(argv)


if __name__ == "__main__":
    build(_arguments())
