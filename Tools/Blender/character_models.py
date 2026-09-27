"""Authored low-poly PS1 characters: geometry, shared-rig skinning and materials.

Imported by generate_ps1_characters.py inside Blender 4.5.3. The five models share one
skeleton, seven named skinned components and one planar face-UV layout, while each has
its own head proportions, hair, build and costume.
"""

from __future__ import annotations

import math
from pathlib import Path

import bmesh
import bpy

import character_textures as tex

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Assets" / "_Game" / "Art"
COMPONENTS = ("Body", "Head", "Face", "Eyes", "Hair", "Clothing", "Accessories")
HEAD_CENTER_Z = 1.663
CROWN_CENTRE_Z = 1.660
CHARACTERS = (
    {"id": "CharacterBase", "display_name": "Character Base", "subtitle": "SHARED RIG"},
    {"id": "AlexeyVoron", "display_name": "Alexey Voron", "subtitle": "DETECTIVE"},
    {"id": "ElenaVoron", "display_name": "Elena Voron", "subtitle": "FAMILY"},
    {"id": "DrIlyaMorozov", "display_name": "Dr. Ilya Morozov", "subtitle": "PHYSICIAN"},
    {"id": "PoliceOfficer", "display_name": "Police Officer", "subtitle": "UNIFORM"},
)
BONE_DEFINITIONS = {
    "Hips": ((0, 0, 0.91), (0, 0, 1.00), None),
    "Spine": ((0, 0, 0.96), (0, 0, 1.20), "Hips"),
    "Chest": ((0, 0, 1.20), (0, 0, 1.40), "Spine"),
    "Neck": ((0, 0, 1.39), (0, 0, 1.51), "Chest"),
    "Head": ((0, 0, 1.49), (0, 0, 1.74), "Neck"),
    # Characters face -Y, so their anatomical left is +X.
    "LeftShoulder": ((0.12, 0, 1.37), (0.21, 0, 1.37), "Chest"),
    "LeftUpperArm": ((0.21, 0, 1.37), (0.29, 0, 1.08), "LeftShoulder"),
    "LeftLowerArm": ((0.29, 0, 1.08), (0.30, -0.01, 0.88), "LeftUpperArm"),
    "LeftHand": ((0.30, -0.01, 0.88), (0.31, -0.03, 0.79), "LeftLowerArm"),
    "RightShoulder": ((-0.12, 0, 1.37), (-0.21, 0, 1.37), "Chest"),
    "RightUpperArm": ((-0.21, 0, 1.37), (-0.29, 0, 1.08), "RightShoulder"),
    "RightLowerArm": ((-0.29, 0, 1.08), (-0.30, -0.01, 0.88), "RightUpperArm"),
    "RightHand": ((-0.30, -0.01, 0.88), (-0.31, -0.03, 0.79), "RightLowerArm"),
    "LeftUpperLeg": ((0.12, 0, 0.96), (0.13, 0, 0.54), "Hips"),
    "LeftLowerLeg": ((0.13, 0, 0.54), (0.13, 0, 0.13), "LeftUpperLeg"),
    "LeftFoot": ((0.13, 0, 0.13), (0.13, -0.18, 0.10), "LeftLowerLeg"),
    "RightUpperLeg": ((-0.12, 0, 0.96), (-0.13, 0, 0.54), "Hips"),
    "RightLowerLeg": ((-0.13, 0, 0.54), (-0.13, 0, 0.13), "RightUpperLeg"),
    "RightFoot": ((-0.13, 0, 0.13), (-0.13, -0.18, 0.10), "RightLowerLeg"),
}
BONE_NAMES = tuple(BONE_DEFINITIONS)


def gauss(dx, dy=0.0):
    return math.exp(-(dx * dx + dy * dy))


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def _normalize(vector):
    length = math.sqrt(sum(c * c for c in vector)) or 1.0
    return tuple(c / length for c in vector)


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


# Skin weights ---------------------------------------------------------------

def mix_weights(first, second, amount):
    amount = max(0.0, min(1.0, amount))
    return {first: 1.0 - amount, second: amount}


def normal_weights(weights):
    cleaned = {name: max(0.0, float(weight)) for name, weight in weights.items() if weight > 0.0001}
    total = sum(cleaned.values())
    if not total:
        return {"Hips": 1.0}
    return {name: weight / total for name, weight in cleaned.items()}


def torso_weights(z):
    if z < 0.94:
        return {"Hips": 1.0}
    if z < 1.04:
        return mix_weights("Hips", "Spine", (z - 0.94) / 0.10)
    if z < 1.18:
        return {"Spine": 1.0}
    if z < 1.30:
        return mix_weights("Spine", "Chest", (z - 1.18) / 0.12)
    return {"Chest": 1.0}


def side_name(x):
    return "Left" if x > 0 else "Right"


def chest_weights(vertex):
    return torso_weights(vertex[2])


def skirt_weights(vertex):
    x, _, z = vertex
    if z >= 0.98:
        return torso_weights(z)
    # Coat skirts follow the thighs so strides do not push the legs through them.
    amount = min(0.8, (0.98 - z) / 0.30 * 0.8) * min(1.0, abs(x) / 0.06)
    return mix_weights("Hips", side_name(x) + "UpperLeg", amount)


def arm_weights(side, z):
    name = side_name(side)
    if z < 1.055:
        return {name + "LowerArm": 1.0}
    if z < 1.135:
        return mix_weights(name + "LowerArm", name + "UpperArm", (z - 1.055) / 0.08)
    if z < 1.385:
        return {name + "UpperArm": 1.0}
    return mix_weights(name + "UpperArm", name + "Shoulder", min(0.5, (z - 1.385) / 0.04 * 0.5))


def leg_weights(side, z):
    name = side_name(side)
    return mix_weights(name + "LowerLeg", name + "UpperLeg", (z - 0.47) / 0.13)


def neck_weights(vertex):
    z = vertex[2]
    if z < 1.46:
        return mix_weights("Chest", "Neck", (z - 1.42) / 0.04)
    if z < 1.53:
        return {"Neck": 1.0}
    return mix_weights("Neck", "Head", (z - 1.53) / 0.05)


def hair_weights(vertex):
    return {"Head": 1.0} if vertex[2] >= 1.56 else mix_weights("Neck", "Head", (vertex[2] - 1.44) / 0.12)


HEAD_WEIGHTS = {"Head": 1.0}
COLLAR_WEIGHTS = {"Chest": 0.7, "Neck": 0.3}


# Mesh assembly ---------------------------------------------------------------

class MeshBuilder:
    def __init__(self, name):
        self.name = name
        self.vertices, self.faces, self.face_uvs, self.vertex_weights = [], [], [], []

    def add(self, vertices, faces, uv_faces, weights):
        offset = len(self.vertices)
        self.vertices.extend(tuple(float(c) for c in vertex) for vertex in vertices)
        self.faces.extend(tuple(offset + int(i) for i in face) for face in faces)
        if len(uv_faces) != len(faces):
            raise ValueError(f"{self.name}: UV face count does not match topology")
        self.face_uvs.extend([tuple((float(u), float(v)) for u, v in loop) for loop in uv_faces])
        self.vertex_weights.extend(normal_weights(weight) for weight in weights)

    def object(self, collection, armature, material, character_id, sharp_angle):
        if not self.faces:
            raise ValueError(f"{self.name}: refusing to create an empty mesh")
        mesh = bpy.data.meshes.new(f"{character_id}_{self.name}Mesh")
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update(calc_edges=True)
        mesh.materials.append(material)
        uv_layer = mesh.uv_layers.new(name="UVMap")
        for polygon, coords in zip(mesh.polygons, self.face_uvs):
            if len(coords) != polygon.loop_total:
                raise ValueError(f"{self.name}: invalid UV layout on polygon {polygon.index}")
            for loop_index, coord in zip(polygon.loop_indices, coords):
                uv_layer.data[loop_index].uv = coord
        obj = bpy.data.objects.new(f"{character_id}_{self.name}", mesh)
        collection.objects.link(obj)
        obj.parent = armature
        obj.matrix_parent_inverse.identity()
        modifier = obj.modifiers.new("Character Skin", "ARMATURE")
        modifier.object = armature
        modifier.use_vertex_groups = True
        groups = {name: obj.vertex_groups.new(name=name) for name in BONE_NAMES}
        for vertex_index, weights in enumerate(self.vertex_weights):
            for bone_name, weight in weights.items():
                groups[bone_name].add([vertex_index], weight, "REPLACE")
        # Parts are authored as separate polygons; welding gives continuous shading.
        weld = bmesh.new()
        weld.from_mesh(mesh)
        bmesh.ops.remove_doubles(weld, verts=list(weld.verts), dist=0.00001)
        weld.to_mesh(mesh)
        weld.free()
        for polygon in mesh.polygons:
            polygon.use_smooth = True
        if sharp_angle:
            mesh.set_sharp_from_angle(angle=math.radians(sharp_angle))
        obj["character_id"] = character_id
        obj["component_role"] = self.name
        return obj


def newell(points):
    nx = ny = nz = 0.0
    for (x1, y1, z1), (x2, y2, z2) in zip(points, points[1:] + points[:1]):
        nx += (y1 - y2) * (z1 + z2)
        ny += (z1 - z2) * (x1 + x2)
        nz += (x1 - x2) * (y1 + y2)
    return nx, ny, nz


def uv(island, u, v):
    x0, y0, x1, y1 = tex.ISLANDS[island]
    u, v = min(1.0, max(0.0, u)), min(1.0, max(0.0, v))
    return ((x0 + 1 + u * (x1 - x0 - 2)) / tex.ATLAS_SIZE,
            1 - (y1 - 1 - v * (y1 - y0 - 2)) / tex.ATLAS_SIZE)


def garment_uv(island, point):
    arc = tex.GARMENT_ARC if island == "front" else tex.GARMENT_BACK_ARC
    return uv(island, 0.5 + point[0] / (2 * arc),
              (point[2] - tex.GARMENT_Z[0]) / (tex.GARMENT_Z[1] - tex.GARMENT_Z[0]))


def garment_rect(s, z, half_s, half_z):
    span = tex.GARMENT_Z[1] - tex.GARMENT_Z[0]
    return (0.5 + (s - half_s) / (2 * tex.GARMENT_ARC), (z - half_z - tex.GARMENT_Z[0]) / span,
            0.5 + (s + half_s) / (2 * tex.GARMENT_ARC), (z + half_z - tex.GARMENT_Z[0]) / span)


def poly(builder, points, island, weights, outward=None, coords=None):
    """Add one polygon, dropping repeated corners and winding it toward outward."""
    kept, unique = [], []
    for index, point in enumerate(points):
        point = tuple(float(c) for c in point)
        if all(math.dist(point, other) > 1e-7 for other in unique):
            unique.append(point)
            kept.append(index)
    if len(unique) < 3:
        return
    if coords is None:
        count = len(unique)
        base = ([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)] if count == 4 else
                [(0.5 + 0.48 * math.cos(k * math.tau / count), 0.5 + 0.48 * math.sin(k * math.tau / count))
                 for k in range(count)])
        coords = [uv(island, u, v) for u, v in base]
    else:
        coords = [coords[index] for index in kept]
    if outward is not None and sum(n * o for n, o in zip(newell(unique), outward)) < 0:
        unique.reverse()
        coords.reverse()
    vertex_weights = [weights(p) for p in unique] if callable(weights) else [weights] * len(unique)
    builder.add(unique, [tuple(range(len(unique)))], [coords], vertex_weights)


def ring(cx, cy, z, rx, ry, count, phase=0.0, direction=1):
    return [(cx + rx * math.cos(phase + direction * k * math.tau / count),
             cy + ry * math.sin(phase + direction * k * math.tau / count), z) for k in range(count)]


def grid(builder, rows, island, weights, outward, closed=True, v_values=None):
    count = len(rows[0])
    columns = count if closed else count - 1
    if v_values is None:
        v_values = [index / (len(rows) - 1) for index in range(len(rows))]
    for r in range(len(rows) - 1):
        for c in range(columns):
            c2 = (c + 1) % count
            quad = [rows[r][c], rows[r][c2], rows[r + 1][c2], rows[r + 1][c]]
            coords = [uv(island, c / columns, v_values[r]), uv(island, (c + 1) / columns, v_values[r]),
                      uv(island, (c + 1) / columns, v_values[r + 1]), uv(island, c / columns, v_values[r + 1])]
            centroid = tuple(sum(p[i] for p in quad) / 4 for i in range(3))
            poly(builder, quad, island, weights, outward(centroid) if callable(outward) else outward, coords)


def axis_outward(centres):
    """Outward direction from a vertical limb axis interpolated between ring centres."""
    ordered = sorted(centres, key=lambda centre: centre[2])

    def outward(point):
        z = min(max(point[2], ordered[0][2]), ordered[-1][2])
        for a, b in zip(ordered, ordered[1:]):
            if z <= b[2]:
                t = (z - a[2]) / (b[2] - a[2]) if b[2] > a[2] else 0.0
                return point[0] - (a[0] + (b[0] - a[0]) * t), point[1] - (a[1] + (b[1] - a[1]) * t), 0.0
        return point[0] - ordered[-1][0], point[1] - ordered[-1][1], 0.0
    return outward


BOX_FACES = ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5))
BOX_SIGNS = tuple(((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1) for i in range(8))


def box(builder, centre, half, island, weights, frame=((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)),
        rect=(0.0, 0.0, 1.0, 1.0)):
    corners = [tuple(centre[i] + sx * half[0] * frame[0][i] + sy * half[1] * frame[1][i]
                     + sz * half[2] * frame[2][i] for i in range(3)) for sx, sy, sz in BOX_SIGNS]
    u0, v0, u1, v1 = rect
    for face in BOX_FACES:
        constant = next(axis for axis in range(3) if len({BOX_SIGNS[i][axis] for i in face}) == 1)
        a_axis, b_axis = [axis for axis in range(3) if axis != constant]
        coords = [uv(island, u0 if BOX_SIGNS[i][a_axis] < 0 else u1, v0 if BOX_SIGNS[i][b_axis] < 0 else v1)
                  for i in face]
        points = [corners[i] for i in face]
        centroid = tuple(sum(p[i] for p in points) / 4 for i in range(3))
        poly(builder, points, island, weights, tuple(c - o for c, o in zip(centroid, centre)), coords)


def frame_z(angle):
    c, s = math.cos(angle), math.sin(angle)
    return (c, s, 0.0), (-s, c, 0.0), (0.0, 0.0, 1.0)


def tube(builder, points, radii, island, weights, sides=6, closed=False, up=(0.0, 0.0, 1.0)):
    """Tube along a polyline; radii are half-extents along the path normal and binormal."""
    count = len(points)
    loops = []
    for index, point in enumerate(points):
        if closed:
            before, after = points[index - 1], points[(index + 1) % count]
        else:
            before, after = points[max(0, index - 1)], points[min(count - 1, index + 1)]
        tangent = _normalize(tuple(b - a for a, b in zip(before, after)))
        normal = _normalize(_cross(tangent, up))
        binormal = _cross(normal, tangent)
        loops.append([tuple(point[i] + radii[0] * math.cos(angle) * normal[i] + radii[1] * math.sin(angle) * binormal[i]
                            for i in range(3))
                      for angle in (k * math.tau / sides + math.pi / sides for k in range(sides))])
    segments = count if closed else count - 1
    for index in range(segments):
        first, second = loops[index], loops[(index + 1) % count]
        axis = tuple((a + b) / 2 for a, b in zip(points[index], points[(index + 1) % count]))
        for k in range(sides):
            k2 = (k + 1) % sides
            quad = [first[k], first[k2], second[k2], second[k]]
            centroid = tuple(sum(p[i] for p in quad) / 4 for i in range(3))
            coords = [uv(island, u, v) for u, v in ((k / sides, index / segments), ((k + 1) / sides, index / segments),
                                                    ((k + 1) / sides, (index + 1) / segments),
                                                    (k / sides, (index + 1) / segments))]
            poly(builder, quad, island, weights, tuple(c - a for c, a in zip(centroid, axis)), coords)
    if not closed:
        for loop, point, other in ((loops[0], points[0], points[1]), (loops[-1], points[-1], points[-2])):
            poly(builder, loop, island, weights, tuple(p - o for p, o in zip(point, other)))


def disc(builder, centre, normal, radius, depth, island, weights, sides=6):
    normal = _normalize(normal)
    up = (0.0, 0.0, 1.0) if abs(normal[2]) < 0.9 else (0.0, 1.0, 0.0)
    tube(builder, [tuple(centre[i] - normal[i] * depth / 2 for i in range(3)),
                   tuple(centre[i] + normal[i] * depth / 2 for i in range(3))],
         (radius, radius), island, weights, sides, up=up)


# Head ---------------------------------------------------------------------

# z, half width, front depth, back depth of an average adult head, in metres.
BASE_HEAD = (
    (1.545, 0.030, 0.050, 0.020), (1.556, 0.046, 0.068, 0.040), (1.570, 0.058, 0.076, 0.058),
    (1.588, 0.064, 0.082, 0.072), (1.600, 0.066, 0.083, 0.079), (1.612, 0.069, 0.080, 0.085),
    (1.625, 0.072, 0.076, 0.091), (1.640, 0.075, 0.080, 0.097), (1.656, 0.077, 0.080, 0.102),
    (1.667, 0.077, 0.081, 0.104), (1.678, 0.076, 0.085, 0.106), (1.690, 0.075, 0.087, 0.106),
    (1.713, 0.073, 0.085, 0.102), (1.736, 0.068, 0.076, 0.092), (1.758, 0.054, 0.058, 0.072),
    (1.775, 0.030, 0.031, 0.040), (1.782, 0.0, 0.0, 0.0),
)
FACE_COLUMNS = (-1.0, -0.85, -0.65, -0.45, -0.23, 0.0, 0.23, 0.45, 0.65, 0.85, 1.0)
# Two grid rows cover the whole painted eye opening, so blink maps can swap it alone.
EYE_ROWS = (8, 9)
EYE_COLUMNS = (2, 3, 6, 7)
HEADS = {
    "CharacterBase": dict(scale=1.04, length=1.0, jaw=1.0, cranium=1.0, chin=1.0, back=1.0, nose_tip=0.022,
                          nose_root=0.003, nose_width=0.016, socket=0.005, brow=0.003, cheek=0.002, lips=0.003,
                          chin_bump=0.003),
    "AlexeyVoron": dict(scale=1.04, length=1.0, jaw=1.03, cranium=0.99, chin=1.02, back=1.0, nose_tip=0.024,
                        nose_root=0.004, nose_width=0.016, socket=0.006, brow=0.004, cheek=0.004, lips=0.002,
                        chin_bump=0.004),
    "ElenaVoron": dict(scale=0.97, length=0.99, jaw=0.90, cranium=1.0, chin=0.96, back=0.98, nose_tip=0.018,
                       nose_root=0.002, nose_width=0.014, socket=0.004, brow=0.001, cheek=0.003, lips=0.0045,
                       chin_bump=0.002),
    "DrIlyaMorozov": dict(scale=1.04, length=1.03, jaw=1.05, cranium=1.02, chin=0.97, back=1.0, nose_tip=0.028,
                          nose_root=0.005, nose_width=0.019, socket=0.006, brow=0.005, cheek=0.001, lips=0.0015,
                          chin_bump=0.002),
    "PoliceOfficer": dict(scale=1.06, length=1.0, jaw=1.10, cranium=1.0, chin=1.07, back=1.0, nose_tip=0.023,
                          nose_root=0.004, nose_width=0.017, socket=0.005, brow=0.006, cheek=0.004, lips=0.0025,
                          chin_bump=0.005),
}


def head_rows(shape):
    rows = []
    for z, w, df, db in BASE_HEAD:
        jaw = smoothstep(1.650, 1.570, z)
        crown = smoothstep(1.690, 1.760, z)
        w *= 1 + (shape["jaw"] - 1) * jaw + (shape["cranium"] - 1) * crown
        df *= 1 + (shape["chin"] - 1) * jaw
        rows.append((z, w, df, db * shape["back"]))
    return rows


def head_space(shape):
    scale, length = shape["scale"], shape["length"]

    def to_world(point):
        x, y, z = point
        return x * scale, y * scale, HEAD_CENTER_Z + (z - HEAD_CENTER_Z) * scale * length
    return to_world


def face_depth(x, z, w, df, shape):
    """Front surface y (negative is forward) with nose, sockets, brow, cheeks, lips and chin."""
    contour = -df * math.sqrt(max(0.0, 1.0 - (x / w) ** 2)) if w > 1e-6 else 0.0
    ax = abs(x)
    tip, root, nose = 1.625, 1.672, 0.0
    if tip - 0.012 < z < root + 0.006:
        t = max(0.0, min(1.0, (root - z) / (root - tip)))
        height = shape["nose_root"] + (shape["nose_tip"] - shape["nose_root"]) * t ** 1.4
        if z < tip:
            height *= max(0.0, 1.0 - (tip - z) / 0.012)
        width = 0.011 + (shape["nose_width"] - 0.011) * t
        nose = height * max(0.0, 1.0 - (ax / width) ** 2)
    wing = shape["nose_tip"] * 0.3 * gauss((ax - shape["nose_width"]) / 0.006, (z - 1.619) / 0.006)
    socket = shape["socket"] * gauss((ax - 0.034) / 0.016, (z - 1.665) / 0.009)
    brow = shape["brow"] * gauss((ax - 0.030) / 0.030, (z - 1.682) / 0.008)
    cheek = shape["cheek"] * gauss((ax - 0.052) / 0.014, (z - 1.642) / 0.012)
    lips = shape["lips"] * math.exp(-(x / 0.022) ** 4) * (gauss((z - 1.600) / 0.005) + 0.9 * gauss((z - 1.588) / 0.005))
    chin = shape["chin_bump"] * gauss(x / 0.022, (z - 1.558) / 0.008)
    return contour - nose - wing - brow - cheek - lips - chin + socket


def face_uv(point):
    x, _, z = point
    return (min(1.0, max(0.0, (x / tex.FACE_HALF_WIDTH + 1.0) * 0.5)),
            min(1.0, max(0.0, (z - tex.FACE_BOTTOM) / (tex.FACE_TOP - tex.FACE_BOTTOM))))


def surface_radius(rows, theta, z):
    """Head contour distance from the vertical axis at an angle (0 = +x, pi/2 = back)."""
    if z >= rows[-1][0]:
        return 0.0
    values = rows[0][1:]
    for lower, upper in zip(rows, rows[1:]):
        if lower[0] <= z <= upper[0]:
            t = (z - lower[0]) / (upper[0] - lower[0])
            values = [lower[i] + (upper[i] - lower[i]) * t for i in (1, 2, 3)]
            break
    w, df, db = values
    depth = db if math.sin(theta) > 0 else df
    if w < 1e-6 or depth < 1e-6:
        return 0.0
    return 1.0 / math.sqrt((math.cos(theta) / w) ** 2 + (math.sin(theta) / depth) ** 2)


def build_head(parts, shape, to_world):
    rows = head_rows(shape)
    outward = lambda c: (c[0], c[1], c[2] - HEAD_CENTER_Z)  # noqa: E731
    front = [[(column * w, face_depth(column * w, z, w, df, shape), z) for column in FACE_COLUMNS]
             for z, w, df, _ in rows]
    for r in range(len(rows) - 1):
        for c in range(len(FACE_COLUMNS) - 1):
            quad = [front[r][c], front[r][c + 1], front[r + 1][c + 1], front[r + 1][c]]
            target = parts["Eyes"] if r in EYE_ROWS and c in EYE_COLUMNS else parts["Face"]
            world = [to_world(p) for p in quad]
            centroid = tuple(sum(p[i] for p in world) / 4 for i in range(3))
            poly(target, world, None, HEAD_WEIGHTS, outward(centroid), [face_uv(p) for p in quad])
    back = [[to_world((w * math.cos(a * math.pi / 8), db * math.sin(a * math.pi / 8), z)) for a in range(9)]
            for z, w, _, db in rows]
    grid(parts["Head"], back, "skin", HEAD_WEIGHTS, outward, closed=False)
    poly(parts["Head"], [to_world(p) for p in front[0]] + back[0][1:-1], "skin", HEAD_WEIGHTS, (0.0, 0.0, -1.0))
    for side in (-1, 1):
        rim, root = [], []
        for k in range(7):
            angle = k * math.tau / 7
            reach = 0.010 + 0.003 * math.sin(angle)
            rim.append(to_world((side * (0.074 + reach), 0.014 + 0.016 * math.cos(angle) + 0.005 * math.sin(angle),
                                 1.651 + 0.028 * math.sin(angle))))
            root.append(to_world((side * 0.073, 0.013 + 0.011 * math.cos(angle), 1.651 + 0.021 * math.sin(angle))))
        poly(parts["Head"], rim, "skin", HEAD_WEIGHTS, (side * 0.9, 0.2, 0.1))
        centre = to_world((side * 0.078, 0.013, 1.651))
        for k in range(7):
            quad = [root[k], root[(k + 1) % 7], rim[(k + 1) % 7], rim[k]]
            centroid = tuple(sum(p[i] for p in quad) / 4 for i in range(3))
            poly(parts["Head"], quad, "skin", HEAD_WEIGHTS, tuple(a - b for a, b in zip(centroid, centre)))
    return rows


# Hair and head accessories ------------------------------------------------------

def keyed(phi, keys):
    """Piecewise-linear value by |phi| in degrees (0 = face centre, 180 = back)."""
    angle = abs(math.degrees(math.atan2(math.sin(phi), math.cos(phi))))
    for (a0, v0), (a1, v1) in zip(keys, keys[1:]):
        if angle <= a1:
            return v0 + (v1 - v0) * (angle - a0) / (a1 - a0)
    return keys[-1][1]


def signed_degrees(phi):
    return math.degrees(math.atan2(math.sin(phi), math.cos(phi)))


def scalp_distance(rows, theta, alpha):
    """Distance from the crown centre to the scalp along an elevation angle (bisection)."""
    radial, rise = math.cos(alpha), math.sin(alpha)
    low, high = 0.0, 0.3
    for _ in range(32):
        mid = (low + high) / 2
        if mid * radial < surface_radius(rows, theta, CROWN_CENTRE_Z + mid * rise):
            low = mid
        else:
            high = mid
    return low


def hair_shell(builder, rows, to_world, style):
    """Scalp-following hair: thin at the hairline, thicker at the crown or curtain.

    Crown styles use per-column rows up to the highest hairline, then rings at shared elevation
    offset radially from the crown centre; equal row indices at unequal heights would fold the
    dome into the scalp, and horizontal offsets alone would leave a spike above the head.
    """
    count = style.get("rows", 8)
    columns = style.get("columns", 24)
    sector = style.get("sector")
    if sector:
        phis = [math.radians(sector[0] + (sector[1] - sector[0]) * k / (columns - 1)) for k in range(columns)]
    else:
        phis = [-math.pi + math.tau * k / columns for k in range(columns)]
    crown = "top" not in style
    apex = rows[-1][0] + style.get("apex", 0.014)
    lower = style.get("lower_rows", 4) if crown else count
    upper = count if crown else 0
    common = max(style["bottom"](phi) for phi in phis)
    loops = []
    for i in range(lower + upper + 1):
        loop = []
        for phi in phis:
            theta = phi - math.pi / 2
            bottom = style["bottom"](phi)
            if crown and i == lower + upper:
                loop.append(to_world((0.0, 0.0, apex)))
                continue
            if not crown:
                z = bottom + (style["top"](phi) - bottom) * i / lower
                t = i / lower
            elif i <= lower:
                z = bottom + (common - bottom) * (i / lower) ** 1.4
                t = (z - bottom) / (apex - bottom)
            else:
                start = (common - bottom) / (apex - bottom)
                s = (i - lower) / upper
                t = start + (1.0 - start) * s
                alpha0 = math.atan2(common - CROWN_CENTRE_Z, surface_radius(rows, theta, common))
                alpha = alpha0 + (math.pi / 2 - alpha0) * s
                reach = scalp_distance(rows, theta, alpha) + style["pad"](phi, t) + 0.003 * s
                loop.append(to_world((reach * math.cos(alpha) * math.cos(theta),
                                      reach * math.cos(alpha) * math.sin(theta),
                                      CROWN_CENTRE_Z + reach * math.sin(alpha))))
                continue
            radius = surface_radius(rows, theta, z) + style["pad"](phi, t)
            if "min_radius" in style:
                radius = max(radius, style["min_radius"](phi, z))
            loop.append(to_world((radius * math.cos(theta), radius * math.sin(theta), z)))
        loops.append(loop)
    grid(builder, loops, "hair", hair_weights, lambda c: (c[0], c[1], c[2] - HEAD_CENTER_Z), closed=not sector)


def hair_base(builder, rows, to_world):
    def pad(phi, t):
        body = keyed(phi, ((0, 0.011), (60, 0.007), (100, 0.006), (180, 0.009))) + 0.004 * t * t
        return 0.0015 + (body - 0.0015) * smoothstep(0.0, 0.3, t)
    hair_shell(builder, rows, to_world, {
        "bottom": lambda phi: keyed(phi, ((0, 1.740), (30, 1.740), (50, 1.716), (66, 1.680), (80, 1.670),
                                          (93, 1.692), (115, 1.688), (140, 1.645), (180, 1.618))),
        "pad": pad, "rows": 6, "lower_rows": 4, "apex": 0.014})


def hair_alexey(builder, rows, to_world):
    def pad(phi, t):
        degrees = signed_degrees(phi)
        body = keyed(phi, ((0, 0.013), (40, 0.012), (70, 0.006), (100, 0.006), (140, 0.009), (180, 0.010)))
        body += 0.004 * gauss((degrees - 22.0) / 22.0) * smoothstep(0.25, 0.6, t)
        body -= 0.004 * gauss((degrees + 28.0) / 10.0) * smoothstep(0.35, 0.8, t)
        body += 0.005 * t * t
        return 0.0015 + (body - 0.0015) * smoothstep(0.0, 0.3, t)
    hair_shell(builder, rows, to_world, {
        "bottom": lambda phi: keyed(phi, ((0, 1.736), (22, 1.737), (36, 1.745), (52, 1.716), (66, 1.676),
                                          (79, 1.664), (91, 1.690), (112, 1.688), (138, 1.640), (180, 1.613))),
        "pad": pad, "rows": 6, "lower_rows": 4, "apex": 0.016})


def hair_elena(builder, rows, to_world):
    def bottom(phi):
        # Tucked behind the ears, then falling to the shoulders.
        z = keyed(phi, ((0, 1.726), (38, 1.724), (52, 1.708), (64, 1.692), (100, 1.690), (110, 1.640),
                        (120, 1.540), (135, 1.475), (180, 1.458)))
        return z - 0.014 * gauss((signed_degrees(phi) + 18.0) / 20.0)

    def pad(phi, t):
        body = keyed(phi, ((0, 0.012), (60, 0.012), (100, 0.013), (180, 0.014))) + 0.006 * t * t
        edge = 0.006 if abs(signed_degrees(phi)) < 55 else 0.004
        return edge + (body - edge) * smoothstep(0.0, 0.25, t)

    def min_radius(phi, z):
        if abs(signed_degrees(phi)) < 105 or z > 1.62:
            return 0.0
        return 0.080 + 0.024 * smoothstep(1.60, 1.47, z)
    hair_shell(builder, rows, to_world, {"bottom": bottom, "pad": pad, "min_radius": min_radius,
                                         "rows": 6, "lower_rows": 8, "apex": 0.018})


def hair_morozov(builder, rows, to_world):
    def pad(phi, t):
        body = keyed(phi, ((60, 0.002), (78, 0.006), (98, 0.008), (130, 0.008), (180, 0.007)))
        return 0.001 + body * math.sin(math.pi * t) ** 0.7
    hair_shell(builder, rows, to_world, {
        "sector": (60.0, 300.0), "columns": 17, "rows": 5, "pad": pad,
        "bottom": lambda phi: keyed(phi, ((60, 1.676), (80, 1.668), (95, 1.690), (122, 1.668), (150, 1.628),
                                          (180, 1.608))),
        "top": lambda phi: keyed(phi, ((60, 1.698), (90, 1.722), (130, 1.742), (180, 1.750)))})


def hair_officer(builder, rows, to_world):
    hair_shell(builder, rows, to_world, {
        "sector": (55.0, 305.0), "columns": 17, "rows": 4,
        "pad": lambda phi, t: 0.0015 + 0.0028 * smoothstep(0.0, 0.3, t),
        "bottom": lambda phi: keyed(phi, ((55, 1.700), (72, 1.672), (84, 1.664), (96, 1.690), (125, 1.668),
                                          (150, 1.630), (180, 1.614))),
        "top": lambda phi: keyed(phi, ((55, 1.728), (90, 1.726), (180, 1.716)))})


def service_cap(builder, rows, to_world):
    thetas = [-math.pi / 2 + math.tau * k / 24 for k in range(24)]
    outward = lambda c: (c[0], c[1], 0.0)  # noqa: E731

    def head_loop(z, pad):
        return [to_world(((surface_radius(rows, t, z) + pad) * math.cos(t),
                          (surface_radius(rows, t, z) + pad) * math.sin(t), z)) for t in thetas]

    def oval(z, rx, front, back, cy):
        return [to_world((rx * math.cos(t), cy + (back if math.sin(t) > 0 else front) * math.sin(t), z)) for t in thetas]
    band_low, band_high = head_loop(1.706, 0.008), head_loop(1.740, 0.010)
    crown = [band_high, oval(1.770, 0.104, 0.124, 0.113, -0.006), oval(1.785, 0.101, 0.120, 0.110, -0.006)]
    grid(builder, [band_low, band_high], "accent", HEAD_WEIGHTS, outward)
    grid(builder, crown, "cap", HEAD_WEIGHTS, outward)
    poly(builder, crown[-1], "cap", HEAD_WEIGHTS, (0.0, 0.0, 1.0))
    visor_thetas = [-math.pi / 2 + math.radians(-66.0 + 16.5 * k) for k in range(9)]
    inner, outer = [], []
    for t in visor_thetas:
        r_in = surface_radius(rows, t, 1.706) + 0.008
        r_out = r_in + 0.050 * (0.55 + 0.45 * math.cos(t + math.pi / 2))
        inner.append((r_in * math.cos(t), r_in * math.sin(t), 1.706))
        outer.append((r_out * math.cos(t), r_out * math.sin(t), 1.690))
    lower = lambda loop: [(x, y, z - 0.004) for x, y, z in loop]  # noqa: E731
    world = lambda loop: [to_world(p) for p in loop]  # noqa: E731
    grid(builder, [world(inner), world(outer)], "dark", HEAD_WEIGHTS, (0.0, -0.3, 1.0), closed=False)
    grid(builder, [world(lower(inner)), world(lower(outer))], "dark", HEAD_WEIGHTS, (0.0, 0.3, -1.0), closed=False)
    grid(builder, [world(lower(outer)), world(outer)], "dark", HEAD_WEIGHTS, outward, closed=False)
    badge_y = -(surface_radius(rows, -math.pi / 2, 1.752) + 0.021)
    disc(builder, to_world((0.0, badge_y, 1.754)), (0.0, -1.0, 0.25), 0.010, 0.003, "metal", HEAD_WEIGHTS)
    cord = [to_world(((surface_radius(rows, t, 1.713) + 0.0105) * math.cos(t),
                      (surface_radius(rows, t, 1.713) + 0.0105) * math.sin(t), 1.713)) for t in visor_thetas]
    tube(builder, cord, (0.0022, 0.0022), "metal", HEAD_WEIGHTS, sides=4)


def glasses(builder, rows, shape, to_world):
    def depth(x, z):
        for lower, upper in zip(rows, rows[1:]):
            if lower[0] <= z <= upper[0]:
                t = (z - lower[0]) / (upper[0] - lower[0])
                w, df = lower[1] + (upper[1] - lower[1]) * t, lower[2] + (upper[2] - lower[2]) * t
                return face_depth(x, z, w, df, shape)
        raise ValueError("Glasses must sit on the modelled face")
    for side in (-1, 1):
        loop = []
        for k in range(8):
            angle = k * math.tau / 8 + math.pi / 8
            x = side * 0.034 + 0.0185 * math.copysign(abs(math.cos(angle)) ** 0.7, math.cos(angle))
            z = 1.664 + 0.013 * math.copysign(abs(math.sin(angle)) ** 0.7, math.sin(angle))
            loop.append(to_world((x, depth(x, z) - 0.012, z)))
        tube(builder, loop, (0.0016, 0.0016), "metal", HEAD_WEIGHTS, sides=4, closed=True, up=(0.0, 1.0, 0.0))
        temple = [(side * 0.0525, depth(side * 0.0525, 1.667) - 0.011, 1.667), (side * 0.075, -0.030, 1.668),
                  (side * 0.081, 0.010, 1.662)]
        tube(builder, [to_world(p) for p in temple], (0.0015, 0.0015), "metal", HEAD_WEIGHTS, sides=4)
    bridge_y = depth(0.0155, 1.668) - 0.010
    tube(builder, [to_world(p) for p in ((-0.0155, bridge_y, 1.668), (0.0, bridge_y - 0.003, 1.670),
                                         (0.0155, bridge_y, 1.668))],
         (0.0015, 0.0015), "metal", HEAD_WEIGHTS, sides=4, up=(0.0, 1.0, 0.0))


# Body -----------------------------------------------------------------------

NECK = ((1.440, 0.062, 0.057, 0.010), (1.478, 0.058, 0.056, 0.010), (1.516, 0.055, 0.054, 0.008),
        (1.558, 0.052, 0.053, 0.012), (1.600, 0.046, 0.048, 0.018))
PALM = ((0.300, -0.024, 0.889, 0.025, 0.023), (0.304, -0.028, 0.862, 0.028, 0.023),
        (0.308, -0.033, 0.829, 0.032, 0.024), (0.309, -0.039, 0.804, 0.030, 0.018))


def neck(builder, k):
    rings = [ring(0.0, cy, z, rx * k, ry * k, 12) for z, rx, ry, cy in NECK]
    grid(builder, rings, "skin", neck_weights, axis_outward([(0.0, cy, z) for z, _, _, cy in NECK]))


def hands(builder, side, scale):
    weights = {side_name(side) + "Hand": 1.0}
    pivot = (side * 0.300, -0.010, 0.886)

    def place(point):
        return tuple(pivot[i] + (point[i] - pivot[i]) * scale for i in range(3))

    def limb(shape, count):
        shape = sorted(shape, key=lambda item: item[2])
        rings = [[place(p) for p in ring(side * x, y, z, rx, ry, count)] for x, y, z, rx, ry in shape]
        grid(builder, rings, "skin", weights, axis_outward([place((side * x, y, z)) for x, y, z, _, _ in shape]))
        poly(builder, rings[0], "skin", weights, (0.0, 0.0, -1.0))
        return rings
    palm = limb(PALM, 8)
    poly(builder, palm[-1], "skin", weights, (0.0, 0.0, 1.0))
    for finger in range(4):
        x = 0.286 + finger * 0.015
        tip = 0.765 + (0.010 if finger in (0, 3) else 0.0)
        limb(((x, -0.039, 0.814, 0.009, 0.018), (x, -0.045, 0.789, 0.008, 0.014), (x, -0.051, tip, 0.0065, 0.010)), 6)
    limb(((0.272, -0.028, 0.849, 0.012, 0.013), (0.261, -0.032, 0.823, 0.011, 0.011),
          (0.262, -0.040, 0.806, 0.008, 0.009)), 6)


# Garments -------------------------------------------------------------------

def torso_ring(row, inset=0.0):
    """Garment cross-section from the front-left opening edge around the back."""
    z, w, df, db, gap = row
    w, df, db = w - inset, df - inset, db - inset
    shape = ((-gap, -df), (-w * 0.45, -df * 0.99), (-w * 0.80, -df * 0.82), (-w, -df * 0.32), (-w, db * 0.30),
             (-w * 0.79, db * 0.84), (-w * 0.42, db), (0.0, db * 1.025), (w * 0.42, db), (w * 0.79, db * 0.84),
             (w, db * 0.30), (w, -df * 0.32), (w * 0.80, -df * 0.82), (w * 0.45, -df * 0.99), (gap, -df))
    return [(x, y, z) for x, y in shape]


def ring_arcs(points):
    """Arc-length coordinates: front islands from the opening, back islands from the spine."""
    seg = [math.dist(points[j][:2], points[j + 1][:2]) for j in range(14)]
    front, back = [0.0] * 15, [0.0] * 15
    front[0], front[14] = -abs(points[0][0]), abs(points[14][0])
    for j in range(1, 5):
        front[j] = front[j - 1] - seg[j - 1]
    for j in range(13, 9, -1):
        front[j] = front[j + 1] + seg[j]
    for j in range(6, 3, -1):
        back[j] = back[j + 1] - seg[j]
    for j in range(8, 11):
        back[j] = back[j - 1] + seg[j - 1]
    return front, back


def row_at(rows, z):
    if z <= rows[0][0]:
        return (z,) + tuple(rows[0][1:4]) + (0.0,)
    for lower, upper in zip(rows, rows[1:]):
        if z <= upper[0]:
            t = (z - lower[0]) / (upper[0] - lower[0])
            return (z,) + tuple(lower[i] + (upper[i] - lower[i]) * t for i in (1, 2, 3)) + (0.0,)
    return (z,) + tuple(rows[-1][1:4]) + (0.0,)


def front_point(rows, x, z, offset=0.0):
    """Point, horizontal tangent and outward normal on a closed garment front."""
    points = torso_ring(row_at(rows, z))
    front = [points[j] for j in (3, 2, 1, 0, 13, 12, 11)]
    for a, b in zip(front, front[1:]):
        if a[0] <= x <= b[0]:
            t = (x - a[0]) / (b[0] - a[0]) if b[0] > a[0] else 0.0
            tangent = _normalize((b[0] - a[0], b[1] - a[1], 0.0))
            normal = (tangent[1], -tangent[0], 0.0)
            y = a[1] + (b[1] - a[1]) * t
            return (x + normal[0] * offset, y + normal[1] * offset, z), tangent, normal
    raise ValueError(f"x={x} is outside the garment front")


def surface_frame(rows, x, z, offset):
    point, tangent, normal = front_point(rows, x, z, offset)
    return point, (tangent, normal, (0.0, 0.0, 1.0)), normal


def torso(builder, rows, lining):
    rings = [torso_ring(row) for row in rows]
    span = tex.GARMENT_Z[1] - tex.GARMENT_Z[0]
    for r in range(len(rings) - 1):
        arcs = (ring_arcs(rings[r]), ring_arcs(rings[r + 1]))
        for c in range(14):
            island = "front" if c < 4 or c > 9 else "back"
            arc = tex.GARMENT_ARC if island == "front" else tex.GARMENT_BACK_ARC
            quad, coords = [], []
            for rr, cc in ((r, c), (r, c + 1), (r + 1, c + 1), (r + 1, c)):
                point = rings[rr][cc]
                s = arcs[rr - r][0 if island == "front" else 1][cc]
                quad.append(point)
                coords.append(uv(island, 0.5 + s / (2 * arc), (point[2] - tex.GARMENT_Z[0]) / span))
            centroid = tuple(sum(p[i] for p in quad) / 4 for i in range(3))
            poly(builder, quad, island, skirt_weights, (centroid[0], centroid[1], 0.0), coords)
    depth = len(rows) if lining == "full" else 2
    inner = [torso_ring(row, 0.006) for row in rows[:depth]]
    grid(builder, inner, "lining", skirt_weights, lambda c: (-c[0], -c[1], 0.0), closed=False)
    grid(builder, [rings[0], inner[0]], "lining", skirt_weights, (0.0, 0.0, -1.0), closed=False)
    if lining == "full" and any(row[4] > 0.002 for row in rows):
        for column, direction in ((0, 1.0), (14, -1.0)):
            edge = [[rings[r][column], inner[r][column]] for r in range(len(rings))]
            grid(builder, edge, "lining", skirt_weights, (direction, 0.0, 0.0), closed=False)


def arm_center(z, side, inset=0.0):
    if z <= 1.08:
        t = (z - 0.88) / 0.20
        x, y = 0.300 - 0.010 * t, -0.010 + 0.010 * t
    elif z <= 1.37:
        t = (z - 1.08) / 0.29
        x, y = 0.290 - 0.080 * t, 0.0
    else:
        t = (z - 1.37) / 0.055
        x, y = 0.210 - 0.030 * t, 0.004 * t
    return side * (x - inset * smoothstep(1.30, 1.41, z)), y


def leg_center(z, side):
    if z >= 0.54:
        t = (z - 0.54) / 0.42
        # Thighs converge toward the crotch so they stay inside short garment hems.
        return side * (0.130 - 0.024 * t ** 1.5), -0.004 + 0.004 * t
    t = (z - 0.13) / 0.41
    return side * (0.133 - 0.003 * t), 0.002 - 0.006 * t


def limb(builder, side, table, centre_fn, island, weights, span, caps):
    rings, centres, vs = [], [], []
    for z, rx, ry in table:
        cx, cy = centre_fn(z)
        # Seams run on the inner side; u = 0.25 is the front on both limbs.
        rings.append(ring(cx, cy, z, rx, ry, 10, math.pi if side > 0 else 0.0, 1 if side > 0 else -1))
        centres.append((cx, cy, z))
        vs.append((z - span[0]) / (span[1] - span[0]))
    grid(builder, rings, island, weights, axis_outward(centres), v_values=vs)
    poly(builder, rings[0], island, weights, (0.0, 0.0, -1.0))
    if caps:
        poly(builder, rings[-1], island, weights, (0.0, 0.0, 1.0))


def sleeve(builder, side, table, inset=0.010):
    limb(builder, side, table, lambda z: arm_center(z, side, inset), "sleeve",
         lambda v: arm_weights(side, v[2]), tex.SLEEVE_Z, True)


def leg(builder, side, table):
    limb(builder, side, table, lambda z: leg_center(z, side), "trousers",
         lambda v: leg_weights(side, v[2]), tex.TROUSER_Z, False)


PELVIS = ((0.842, 0.050, 0.056, 0.062), (0.880, 0.148, 0.094, 0.104), (0.935, 0.166, 0.100, 0.108),
          (0.990, 0.160, 0.098, 0.104), (1.040, 0.152, 0.096, 0.100))


def pelvis(builder, k=1.0):
    rings = [torso_ring((z, w * k, df * k, db * k, 0.0)) for z, w, df, db in PELVIS]
    vs = [(z - tex.TROUSER_Z[0]) / (tex.TROUSER_Z[1] - tex.TROUSER_Z[0]) for z, *_ in PELVIS]
    grid(builder, rings, "trousers", skirt_weights, lambda c: (c[0], c[1], 0.0), v_values=vs)


SHOE_OUTLINE = ((-0.044, 0.075), (-0.055, 0.044), (-0.055, -0.082), (-0.054, -0.154), (-0.035, -0.184),
                (0.030, -0.187), (0.051, -0.164), (0.052, -0.092), (0.047, 0.059))


def shoe(builder, side, style):
    weights = {side_name(side) + "Foot": 1.0}
    fx = side * 0.133
    heel = style == "heel"

    def lift(y):
        return 0.036 * smoothstep(-0.035, 0.035, y) if heel else 0.0
    soles = [[(fx + x, y, lift(y)) for x, y in SHOE_OUTLINE],
             [(fx + x, y, lift(y) + (0.012 if heel else 0.030)) for x, y in SHOE_OUTLINE]]
    uppers = [soles[1]]
    for scale, height in ((0.95, 0.047), (0.82, 0.084), (0.60, 0.145)):
        uppers.append([(fx + x * scale, 0.014 + (y - 0.014) * scale,
                        lift(y) * 0.5 + height - 0.063 * (height - 0.032) / 0.113 * smoothstep(0.02, 0.16, -y))
                       for x, y in SHOE_OUTLINE])
    shafts = {"boot": ((0.18, 0.050, 0.056), (0.24, 0.054, 0.060), (0.30, 0.058, 0.064), (0.335, 0.060, 0.066)),
              "heel": ((0.172, 0.045, 0.050), (0.205, 0.047, 0.052))}.get(style, ())
    for z, rx, ry in shafts:
        cx, cy = leg_center(z, side)
        uppers.append([(cx + rx * math.cos(math.radians(110 + 40 * k)), cy + ry * math.sin(math.radians(110 + 40 * k)), z)
                       for k in range(len(SHOE_OUTLINE))])
    outward = lambda c: (c[0] - fx, c[1] + 0.04, 0.0)  # noqa: E731
    grid(builder, soles, "sole", weights, outward)
    grid(builder, uppers, "leather", weights, outward)
    poly(builder, soles[0], "sole", weights, (0.0, 0.0, -1.0))
    poly(builder, uppers[-1], "leather", weights, (0.0, 0.0, 1.0))
    if heel:
        box(builder, (fx, 0.047, 0.018), (0.021, 0.022, 0.018), "sole", weights)


def band(builder, rows, z0, z1, outset, island):
    low, high = torso_ring(row_at(rows, z0), -outset), torso_ring(row_at(rows, z1), -outset)
    grid(builder, [low, high], island, chest_weights, lambda c: (c[0], c[1], 0.0))


def coat_collar(builder, low, high, z0, z1, island, span=(58.0, 302.0)):
    for inset, sign in ((0.0, 1.0), (0.003, -1.0)):
        rows = []
        for (rx, ry), z in ((low, z0), (high, z1)):
            rows.append([((rx - inset) * math.cos(math.radians(span[0] + (span[1] - span[0]) * k / 10) - math.pi / 2),
                          0.004 + (ry - inset) * math.sin(math.radians(span[0] + (span[1] - span[0]) * k / 10)
                                                         - math.pi / 2), z) for k in range(11)])
        grid(builder, rows, island, COLLAR_WEIGHTS, lambda c, s=sign: (s * c[0], s * (c[1] - 0.004), 0.0), closed=False)


def front_poly(builder, points, island, outward=(0.0, -1.0, 0.0)):
    coords = [garment_uv(island, p) for p in points] if island in ("front", "back") else None
    poly(builder, points, island, chest_weights, outward, coords)


def shirt_collar(builder):
    coords = [uv("shirt", u, v) for u, v in ((0.04, 0.95), (0.3, 0.95), (0.3, 0.55), (0.04, 0.55))]
    for side in (-1, 1):
        poly(builder, [(side * 0.008, -0.066, 1.484), (side * 0.052, -0.071, 1.485), (side * 0.066, -0.095, 1.404),
                       (side * 0.016, -0.104, 1.438)], "shirt", COLLAR_WEIGHTS, (0.0, -1.0, 0.35), coords)


def tie(builder, island, knot_z, tip_z, y):
    front_poly(builder, [(-0.013, y, knot_z - 0.020), (0.013, y, knot_z - 0.020), (0.016, y + 0.001, knot_z),
                         (-0.016, y + 0.001, knot_z)], island)
    front_poly(builder, [(-0.021, y + 0.005, tip_z + 0.023), (0.0, y - 0.003, tip_z), (0.023, y + 0.005, tip_z + 0.023),
                         (0.012, y - 0.004, knot_z - 0.024), (-0.011, y - 0.004, knot_z - 0.024)], island)


def scaled(table, k):
    return tuple((z, rx * k, ry * k) for z, rx, ry in table)


DRESS_LEG = ((0.125, 0.046, 0.049), (0.155, 0.049, 0.052), (0.185, 0.046, 0.050), (0.330, 0.048, 0.055),
             (0.480, 0.052, 0.060), (0.535, 0.055, 0.058), (0.585, 0.058, 0.064), (0.720, 0.068, 0.074),
             (0.840, 0.075, 0.082), (0.920, 0.078, 0.084))
# Thighs hidden by long coats stay slim so strides do not push them through the skirt.
COAT_LEG = DRESS_LEG[:7] + ((0.720, 0.062, 0.068), (0.840, 0.064, 0.070), (0.920, 0.064, 0.070))
COAT_SLEEVE = ((0.874, 0.036, 0.038), (0.905, 0.038, 0.040), (0.965, 0.042, 0.044), (1.025, 0.045, 0.046),
               (1.085, 0.046, 0.047), (1.140, 0.049, 0.051), (1.220, 0.052, 0.056), (1.300, 0.055, 0.059),
               (1.345, 0.054, 0.058), (1.372, 0.046, 0.053), (1.393, 0.028, 0.042))
BASE_SWEATER = (
    (0.860, 0.194, 0.114, 0.120, 0.0), (0.900, 0.188, 0.112, 0.118, 0.0), (1.000, 0.174, 0.108, 0.112, 0.0),
    (1.100, 0.178, 0.112, 0.112, 0.0), (1.220, 0.193, 0.118, 0.113, 0.0), (1.330, 0.203, 0.116, 0.113, 0.0),
    (1.400, 0.198, 0.105, 0.102, 0.0), (1.440, 0.140, 0.084, 0.086, 0.0), (1.472, 0.068, 0.064, 0.074, 0.0),
)
BASE_SLEEVE = ((0.874, 0.034, 0.036), (0.915, 0.035, 0.037), (0.925, 0.037, 0.039), (0.985, 0.040, 0.042),
               (1.050, 0.042, 0.044), (1.110, 0.045, 0.047), (1.200, 0.049, 0.053), (1.290, 0.052, 0.056),
               (1.340, 0.051, 0.055), (1.368, 0.043, 0.050), (1.390, 0.026, 0.039))
ALEXEY_COAT = (
    (0.650, 0.224, 0.122, 0.126, 0.022), (0.700, 0.220, 0.122, 0.125, 0.016), (0.840, 0.196, 0.118, 0.121, 0.008),
    (0.980, 0.180, 0.116, 0.118, 0.006), (1.100, 0.183, 0.119, 0.117, 0.010), (1.230, 0.198, 0.124, 0.119, 0.040),
    (1.340, 0.209, 0.122, 0.117, 0.070), (1.405, 0.203, 0.107, 0.104, 0.064), (1.445, 0.142, 0.084, 0.084, 0.058),
    (1.478, 0.070, 0.066, 0.074, 0.046),
)
ELENA_COAT = (
    (0.585, 0.232, 0.128, 0.130, 0.012), (0.640, 0.224, 0.126, 0.128, 0.010), (0.780, 0.196, 0.118, 0.122, 0.004),
    (0.900, 0.176, 0.112, 0.116, 0.0), (1.000, 0.152, 0.103, 0.104, 0.0), (1.060, 0.154, 0.105, 0.104, 0.0),
    (1.160, 0.170, 0.116, 0.104, 0.0), (1.260, 0.184, 0.121, 0.106, 0.018), (1.350, 0.190, 0.114, 0.106, 0.046),
    (1.402, 0.184, 0.100, 0.096, 0.055), (1.442, 0.128, 0.080, 0.080, 0.052), (1.474, 0.063, 0.061, 0.066, 0.044),
)
ELENA_SLEEVE = ((0.874, 0.039, 0.040), (0.912, 0.039, 0.040), (0.918, 0.034, 0.035), (0.970, 0.036, 0.037),
                (1.030, 0.038, 0.039), (1.090, 0.040, 0.041), (1.150, 0.042, 0.044), (1.230, 0.045, 0.048),
                (1.300, 0.047, 0.051), (1.340, 0.046, 0.050), (1.366, 0.039, 0.045), (1.386, 0.023, 0.035))
ELENA_LEG = ((0.170, 0.048, 0.050), (0.200, 0.043, 0.046), (0.330, 0.042, 0.048), (0.480, 0.046, 0.053),
             (0.535, 0.049, 0.052), (0.585, 0.052, 0.057), (0.720, 0.056, 0.061), (0.840, 0.058, 0.064),
             (0.920, 0.058, 0.064))
MOROZOV_COAT = (
    (0.625, 0.224, 0.126, 0.124, 0.052), (0.680, 0.220, 0.126, 0.123, 0.050), (0.820, 0.200, 0.127, 0.120, 0.046),
    (0.960, 0.189, 0.131, 0.118, 0.045), (1.080, 0.191, 0.134, 0.117, 0.046), (1.200, 0.198, 0.130, 0.118, 0.052),
    (1.320, 0.204, 0.122, 0.116, 0.064), (1.400, 0.197, 0.106, 0.102, 0.062), (1.442, 0.138, 0.084, 0.084, 0.056),
    (1.476, 0.070, 0.068, 0.076, 0.046),
)
OFFICER_TUNIC = (
    (0.835, 0.208, 0.124, 0.130, 0.0), (0.900, 0.194, 0.118, 0.124, 0.0), (0.980, 0.182, 0.115, 0.119, 0.0),
    (1.080, 0.188, 0.121, 0.120, 0.0), (1.200, 0.206, 0.129, 0.122, 0.0), (1.320, 0.221, 0.127, 0.121, 0.012),
    (1.408, 0.215, 0.111, 0.107, 0.030), (1.448, 0.151, 0.089, 0.092, 0.036), (1.482, 0.076, 0.071, 0.084, 0.030),
)
OFFICER_SLEEVE = ((0.874, 0.037, 0.039), (0.905, 0.038, 0.040), (0.965, 0.042, 0.044), (1.025, 0.045, 0.047),
                  (1.085, 0.047, 0.048), (1.140, 0.050, 0.052), (1.220, 0.054, 0.058), (1.300, 0.057, 0.061),
                  (1.350, 0.057, 0.061), (1.380, 0.048, 0.055), (1.402, 0.029, 0.043))
OFFICER_LEG = ((0.290, 0.050, 0.054), (0.340, 0.052, 0.058), (0.480, 0.056, 0.062), (0.535, 0.058, 0.061),
               (0.585, 0.062, 0.067), (0.720, 0.072, 0.078), (0.840, 0.079, 0.086), (0.920, 0.081, 0.087))


def outfit_base(parts, rows, shape, to_world):
    clothing, extra = parts["Clothing"], parts["Accessories"]
    torso(clothing, BASE_SWEATER, "hem")
    pelvis(clothing)
    for side in (-1, 1):
        sleeve(clothing, side, BASE_SLEEVE)
        leg(clothing, side, scaled(DRESS_LEG, 0.97))
        shoe(clothing, side, "dress")
    grid(clothing, [torso_ring((1.458, 0.074, 0.070, 0.074, 0.0)), torso_ring((1.492, 0.060, 0.058, 0.062, 0.0))],
         "knit", COLLAR_WEIGHTS, lambda c: (c[0], c[1], 0.0))
    watch = {"LeftLowerArm": 1.0}
    box(extra, (0.300, -0.009, 0.902), (0.013, 0.017, 0.009), "metal", watch, frame_z(math.radians(8)))
    band_rings = [ring(0.300, -0.010, z, 0.038, 0.040, 8) for z in (0.897, 0.907)]
    grid(extra, band_rings, "trim", watch, lambda c: (c[0] - 0.300, c[1] + 0.010, 0.0))


def outfit_alexey(parts, rows, shape, to_world):
    clothing, extra = parts["Clothing"], parts["Accessories"]
    coat = ALEXEY_COAT
    torso(clothing, coat, "full")
    pelvis(clothing)
    front_poly(clothing, [(-0.058, -0.101, 1.14), (0.058, -0.101, 1.14), (0.052, -0.079, 1.47),
                          (-0.052, -0.079, 1.47)], "shirt")
    shirt_collar(clothing)
    tie(clothing, "accent", 1.456, 1.202, -0.115)
    coat_collar(clothing, (0.074, 0.074), (0.082, 0.086), 1.466, 1.532, "back")
    for side in (-1, 1):
        sleeve(clothing, side, COAT_SLEEVE)
        leg(clothing, side, COAT_LEG)
        shoe(clothing, side, "dress")
        front_poly(clothing, [(side * 0.009, -0.130, 1.160), (side * 0.092, -0.142, 1.323),
                              (side * 0.077, -0.119, 1.356), (side * 0.124, -0.099, 1.403),
                              (side * 0.056, -0.081, 1.473), (side * 0.041, -0.085, 1.424)], "front", (0.0, -1.0, 0.25))
        point, frame, _ = surface_frame(coat, side * 0.142, 1.036, 0.004)
        box(clothing, point, (0.037, 0.003, 0.013), "front", chest_weights, frame,
            garment_rect(side * 0.142, 1.036, 0.037, 0.013))
    for z in (1.100, 0.985, 0.870):
        point, _, normal = surface_frame(coat, 0.022, z, 0.003)
        disc(extra, point, normal, 0.0085, 0.004, "dark", chest_weights)


def scarf(builder):
    loop = [(0.071 * math.cos(k * math.tau / 12), 0.006 + 0.068 * math.sin(k * math.tau / 12),
             1.479 + 0.011 * math.sin(k * math.tau / 12)) for k in range(12)]
    tube(builder, loop, (0.024, 0.020), "knit", COLLAR_WEIGHTS, sides=6, closed=True)
    tube(builder, [(-0.036, -0.082, 1.458), (-0.044, -0.100, 1.40), (-0.050, -0.112, 1.33), (-0.054, -0.118, 1.26)],
         (0.042, 0.009), "knit", chest_weights, sides=4, up=(0.0, 1.0, 0.0))


def outfit_elena(parts, rows, shape, to_world):
    clothing, extra = parts["Clothing"], parts["Accessories"]
    coat = ELENA_COAT
    torso(clothing, coat, "full")
    pelvis(clothing, 0.95)
    front_poly(clothing, [(-0.048, -0.108, 1.14), (0.048, -0.108, 1.14), (0.044, -0.079, 1.465),
                          (-0.044, -0.079, 1.465)], "shirt")
    scarf(clothing)
    band(clothing, coat, 1.004, 1.046, 0.005, "accent")
    point, frame, normal = surface_frame(coat, 0.0, 1.025, 0.009)
    box(extra, point, (0.020, 0.004, 0.017), "metal", chest_weights, frame)
    point, frame, _ = surface_frame(coat, 0.032, 0.972, 0.008)
    box(clothing, point, (0.012, 0.003, 0.036), "accent", skirt_weights, frame)
    for side in (-1, 1):
        sleeve(clothing, side, ELENA_SLEEVE, 0.018)
        leg(clothing, side, ELENA_LEG)
        shoe(clothing, side, "heel")
        front_poly(clothing, [(side * 0.004, -0.124, 1.260), (side * 0.088, -0.130, 1.355), (side * 0.118, -0.097, 1.402),
                              (side * 0.054, -0.076, 1.470), (side * 0.040, -0.088, 1.400)], "front", (0.0, -1.0, 0.25))
        for z in (1.130, 1.215, 0.915, 0.825):
            point, _, normal = surface_frame(coat, side * 0.052, z, 0.003)
            disc(extra, point, normal, 0.009, 0.004, "dark", skirt_weights)


def outfit_morozov(parts, rows, shape, to_world):
    clothing, extra = parts["Clothing"], parts["Accessories"]
    coat = MOROZOV_COAT
    torso(clothing, coat, "full")
    pelvis(clothing, 1.03)
    front_poly(clothing, [(-0.075, -0.127, 0.93), (0.075, -0.127, 0.93), (0.070, -0.114, 1.36), (0.0, -0.121, 1.25),
                          (-0.070, -0.114, 1.36)], "knit")
    front_poly(clothing, [(-0.050, -0.104, 1.24), (0.050, -0.104, 1.24), (0.048, -0.078, 1.47),
                          (-0.048, -0.078, 1.47)], "shirt")
    shirt_collar(clothing)
    tie(clothing, "accent", 1.456, 1.250, -0.113)
    point, frame, _ = surface_frame(coat, -0.108, 1.287, 0.003)
    box(clothing, point, (0.034, 0.002, 0.038), "front", chest_weights, frame, garment_rect(-0.108, 1.287, 0.034, 0.038))
    for x, island in ((-0.118, "trim"), (-0.104, "dark")):
        base = (point[0] + (x + 0.108), point[1] - 0.004, point[2] + 0.030)
        tube(extra, [base, (base[0], base[1], base[2] + 0.034)], (0.0035, 0.0035), island, chest_weights, sides=5)
    for side in (-1, 1):
        sleeve(clothing, side, scaled(COAT_SLEEVE, 1.03))
        leg(clothing, side, scaled(COAT_LEG, 1.04))
        shoe(clothing, side, "dress")
        front_poly(clothing, [(side * 0.050, -0.136, 1.200), (side * 0.100, -0.134, 1.330), (side * 0.086, -0.116, 1.360),
                              (side * 0.126, -0.100, 1.400), (side * 0.056, -0.081, 1.472),
                              (side * 0.046, -0.090, 1.420)], "front", (0.0, -1.0, 0.25))
        point, frame, _ = surface_frame(coat, side * 0.142, 0.985, 0.003)
        box(clothing, point, (0.052, 0.002, 0.056), "front", skirt_weights, frame,
            garment_rect(side * 0.142, 0.985, 0.052, 0.056))
    for z in (1.150, 1.000, 0.850):
        point, _, normal = surface_frame(coat, -0.058, z, 0.003)
        disc(extra, point, normal, 0.008, 0.004, "lining", skirt_weights)
    glasses(extra, rows, shape, to_world)


def outfit_officer(parts, rows, shape, to_world):
    clothing, extra = parts["Clothing"], parts["Accessories"]
    tunic = OFFICER_TUNIC
    torso(clothing, tunic, "hem")
    pelvis(clothing, 1.04)
    front_poly(clothing, [(-0.030, -0.121, 1.19), (0.030, -0.121, 1.19), (0.034, -0.083, 1.478),
                          (-0.034, -0.083, 1.478)], "shirt")
    shirt_collar(clothing)
    tie(clothing, "dark", 1.462, 1.330, -0.094)
    low, out_low = torso_ring(row_at(tunic, 0.944), -0.001), torso_ring(row_at(tunic, 0.944), -0.008)
    high, out_high = torso_ring(row_at(tunic, 0.990), -0.001), torso_ring(row_at(tunic, 0.990), -0.008)
    grid(clothing, [low, out_low], "leather", chest_weights, (0.0, 0.0, -1.0))
    grid(clothing, [out_low, out_high], "leather", chest_weights, lambda c: (c[0], c[1], 0.0))
    grid(clothing, [out_high, high], "leather", chest_weights, (0.0, 0.0, 1.0))
    point, frame, _ = surface_frame(tunic, 0.0, 0.967, 0.011)
    box(extra, point, (0.022, 0.004, 0.019), "metal", chest_weights, frame)
    box(clothing, (0.196, -0.018, 0.905), (0.022, 0.040, 0.064), "leather", {"Hips": 1.0}, frame_z(math.radians(8)))
    point, frame, _ = surface_frame(tunic, -0.112, 1.355, 0.011)
    box(extra, point, (0.018, 0.010, 0.030), "dark", chest_weights, frame)
    tube(extra, [(point[0] + 0.008, point[1], point[2] + 0.028), (point[0] + 0.008, point[1], point[2] + 0.085)],
         (0.003, 0.003), "dark", chest_weights, sides=4)
    point, _, normal = surface_frame(tunic, 0.108, 1.372, 0.002)
    disc(extra, point, normal, 0.013, 0.003, "metal", chest_weights)
    for z in (1.300, 1.190, 1.080, 0.900):
        point, _, normal = surface_frame(tunic, 0.0, z, 0.003)
        disc(extra, point, normal, 0.0075, 0.004, "metal", skirt_weights)
    angle = math.radians(27.0)
    for side in (-1, 1):
        sleeve(clothing, side, OFFICER_SLEEVE, 0.004)
        leg(clothing, side, OFFICER_LEG)
        shoe(clothing, side, "boot")
        front_poly(clothing, [(side * 0.004, -0.128, 1.322), (side * 0.072, -0.126, 1.405), (side * 0.058, -0.084, 1.480),
                              (side * 0.034, -0.092, 1.432)], "front", (0.0, -1.0, 0.25))
        ux, uz = (side * math.cos(angle), 0.0, -math.sin(angle)), (side * math.sin(angle), 0.0, math.cos(angle))
        centre = (side * 0.146, 0.002, 1.456)
        box(clothing, centre, (0.058, 0.026, 0.004), "cap", chest_weights, (ux, (0.0, 1.0, 0.0), uz))
        button = tuple(centre[i] - 0.045 * ux[i] + 0.0055 * uz[i] for i in range(3))
        disc(extra, button, uz, 0.006, 0.003, "metal", chest_weights)
        point, frame, normal = surface_frame(tunic, side * 0.100, 1.318, 0.004)
        box(clothing, point, (0.040, 0.003, 0.014), "front", chest_weights, frame,
            garment_rect(side * 0.100, 1.318, 0.040, 0.014))
        disc(extra, (point[0] + normal[0] * 0.004, point[1] + normal[1] * 0.004, point[2] - 0.004), normal,
             0.005, 0.003, "metal", chest_weights)
    service_cap(extra, rows, to_world)


RECIPES = {
    "CharacterBase": {"hair": hair_base, "neck": 1.0, "hand": 1.0, "outfit": outfit_base, "crown": True},
    "AlexeyVoron": {"hair": hair_alexey, "neck": 1.0, "hand": 1.0, "outfit": outfit_alexey, "crown": True},
    "ElenaVoron": {"hair": hair_elena, "neck": 0.86, "hand": 0.9, "outfit": outfit_elena, "crown": True},
    "DrIlyaMorozov": {"hair": hair_morozov, "neck": 1.04, "hand": 1.0, "outfit": outfit_morozov, "crown": False},
    "PoliceOfficer": {"hair": hair_officer, "neck": 1.14, "hand": 1.06, "outfit": outfit_officer, "crown": False},
}
SHARP_ANGLES = {"Hair": 60.0, "Clothing": 55.0, "Accessories": 55.0}


# Blender objects ------------------------------------------------------------

def make_armature(collection, character_id):
    data = bpy.data.armatures.new(f"{character_id}_Rig")
    armature = bpy.data.objects.new(f"{character_id}_Armature", data)
    collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = {}
    for name, (head, tail, parent_name) in BONE_DEFINITIONS.items():
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        if parent_name:
            bone.parent = edit_bones[parent_name]
        edit_bones[name] = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.show_in_front = True
    armature.data.display_type = "OCTAHEDRAL"
    armature["character_id"] = character_id
    armature["rig_contract"] = "Armature/Hips/Spine/Chest/Neck/Head"
    armature.select_set(False)
    return armature


def load_image(path):
    image = bpy.data.images.load(str(path), check_existing=True)
    image.filepath = str(path)
    image.colorspace_settings.name = "sRGB"
    image.pack()
    return image


def material(name, image):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    principled.inputs["Roughness"].default_value = 0.9
    specular = principled.inputs.get("Specular IOR Level")
    if specular is not None:
        specular.default_value = 0.05
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = f"{name}_PixelTexture"
    texture.image = image
    texture.interpolation = "Closest"
    texture.extension = "EXTEND"
    texture.location = (-360, 80)
    mat.node_tree.links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    return mat


def make_character(spec, root_collection, location_x=0.0):
    character_id = spec["id"]
    recipe, shape = RECIPES[character_id], HEADS[character_id]
    collection = bpy.data.collections.new(f"Character_{character_id}")
    root_collection.children.link(collection)
    armature = make_armature(collection, character_id)
    armature.location.x = location_x
    face_path = ART / "Textures" / "Faces" / f"{character_id}_Face.png"
    atlas_path = ART / "Textures" / "Characters" / f"{character_id}_Clothing.png"
    tex.paint_face(face_path, character_id)
    tex.paint_face(face_path.with_name(f"{character_id}_FaceTalk.png"), character_id, mouth_open=True)
    tex.paint_face(face_path.with_name(f"{character_id}_FaceBlink.png"), character_id, eyes_closed=True)
    tex.paint_atlas(atlas_path, character_id)
    images = {"face": load_image(face_path), "clothing": load_image(atlas_path)}
    face_material = material(f"M_{character_id}_FaceTexture", images["face"])
    atlas_material = material(f"M_{character_id}_ClothAtlas", images["clothing"])

    parts = {name: MeshBuilder(name) for name in COMPONENTS}
    to_world = head_space(shape)
    rows = build_head(parts, shape, to_world)
    recipe["hair"](parts["Hair"], rows, to_world)
    neck(parts["Body"], recipe["neck"])
    for side in (-1, 1):
        hands(parts["Body"], side, recipe["hand"])
    recipe["outfit"](parts, rows, shape, to_world)

    objects = {}
    for component, builder in parts.items():
        mat = face_material if component in ("Face", "Eyes") else atlas_material
        objects[component] = builder.object(collection, armature, mat, character_id, SHARP_ANGLES.get(component))
    return {"id": character_id, "display_name": spec["display_name"], "subtitle": spec["subtitle"],
            "collection": collection, "armature": armature, "objects": objects, "images": images,
            "head_to_world": to_world, "crown_hair": recipe["crown"]}
