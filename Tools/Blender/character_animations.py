"""Idle and Talk clips for the shared character rig, authored per character.

Poses are small rotations about character axes applied as FK on top of the rest pose:
pitch tips a bone's far end forward, yaw turns it toward the character's left, roll tilts
it toward the character's left. Legs are solved with IK against planted feet, so pelvis
sway bends the knees naturally; idle holds (hand in pocket, clasped hands, hands on the
belt) use IK hand targets. Every frame is keyed. The FBX exporter bakes the evaluated
pose into the 19 deform bones; CTRL_ bones only drive the solve and are not exported.
"""

from __future__ import annotations

import math

import bpy
from mathutils import Matrix, Vector

FPS = 24
IDLE_FRAMES = 144
TALK_FRAMES = 96
# Mirrors Voron.Characters.FacePerformance so previews flap the mouth like the game.
SPEECH_WINDOWS = ((0.10, 0.44), (0.56, 0.86))
SYLLABLES = (0.12, 0.07, 0.09, 0.06, 0.15, 0.08, 0.10, 0.05, 0.13, 0.09)
SIDES = {"L": ("Left", 1.0), "R": ("Right", -1.0)}
LIMB_BONES = {"LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm"}
POSED_BONES = ("Hips", "Spine", "Chest", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpperArm",
               "RightUpperArm", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand")
SAMPLE_BONES = ("Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "RightUpperArm", "LeftLowerArm",
                "RightLowerArm", "LeftHand", "RightHand", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot")


def mouth_open(normalized, length):
    if length <= 0.0:
        return False
    for start, end in SPEECH_WINDOWS:
        if start <= normalized < end:
            t = ((normalized - start) * length) % sum(SYLLABLES)
            for index, duration in enumerate(SYLLABLES):
                if t < duration:
                    return index % 2 == 0
                t -= duration
    return False


# Pose algebra: {bone: [pitch, yaw, roll]}, "hips": [x, y, z] metres, "ik.L"/"ik.R": [influence].

def pose(**bones):
    return {name: [float(v) for v in values] for name, values in bones.items()}


def merge(*poses):
    result = {}
    for item in poses:
        for key, values in item.items():
            if key in result:
                result[key] = [a + b for a, b in zip(result[key], values)]
            else:
                result[key] = list(values)
    return result


def scaled(item, factor):
    return {key: [value * factor for value in values] for key, values in item.items()}


def arm(side, fwd=0.0, out=0.0, twist=0.0, bend=0.0, turn=0.0, flex=0.0, dev=0.0):
    """Semantic arm pose: flexion, abduction, external twist, elbow bend, supination,
    wrist flexion toward the palm and deviation toward the thumb, all in degrees."""
    name, s = SIDES[side]
    fore = max(-45.0, min(45.0, turn * 0.4))  # rest of the supination is taken by the rigid hand
    return {f"{name}UpperArm": [-fwd, s * twist, -s * out],
            f"{name}LowerArm": [-bend, s * fore, 0.0],
            f"{name}Hand": [flex, s * (turn - fore), s * dev]}


def shoulders(shrug=0.0, fwd=0.0, sides="LR"):
    result = {}
    for side in sides:
        name, s = SIDES[side]
        result[f"{name}Shoulder"] = [0.0, -s * fwd, -s * shrug]
    return result


def ik(side, value):
    return {f"ik.{side}": [value]}


def bump(frame, centre, width):
    distance = abs(frame - centre) / width
    return 0.0 if distance >= 1.0 else 0.5 + 0.5 * math.cos(math.pi * distance)


def envelope(t, start, rise, hold, fall):
    if t <= start or t >= start + rise + hold + fall:
        return 0.0
    if t < start + rise:
        u = (t - start) / rise
    elif t <= start + rise + hold:
        return 1.0
    else:
        u = 1.0 - (t - start - rise - hold) / fall
    return u * u * (3.0 - 2.0 * u)


def hermite(keys, frame):
    """Smooth pose track through (frame, pose) keys with time-aware tangents and resting ends."""
    channels = {}
    for _, item in keys:
        for key, values in item.items():
            channels.setdefault(key, len(values))
    frames = [f for f, _ in keys]
    values = [{key: item.get(key, [0.0] * size) for key, size in channels.items()} for _, item in keys]
    if frame <= frames[0]:
        return values[0]
    if frame >= frames[-1]:
        return values[-1]
    index = next(i for i in range(len(frames) - 1) if frames[i] <= frame <= frames[i + 1])
    t0, t1 = frames[index], frames[index + 1]
    h = t1 - t0
    u = (frame - t0) / h

    def tangent(i, key, component):
        if i == 0 or i == len(frames) - 1:
            return 0.0
        return (values[i + 1][key][component] - values[i - 1][key][component]) / (frames[i + 1] - frames[i - 1])
    h00, h10, h01, h11 = 2 * u ** 3 - 3 * u ** 2 + 1, u ** 3 - 2 * u ** 2 + u, -2 * u ** 3 + 3 * u ** 2, u ** 3 - u ** 2
    return {key: [h00 * values[index][key][c] + h10 * h * tangent(index, key, c)
                  + h01 * values[index + 1][key][c] + h11 * h * tangent(index + 1, key, c)
                  for c in range(size)] for key, size in channels.items()}


RELAXED_L = arm("L", fwd=5, out=-2, bend=14, turn=25, flex=3)
RELAXED_R = arm("R", fwd=5, out=-2, bend=14, turn=25, flex=3)

# Hand holds: wrist position and hand rotation (character axes, applied to the rest hand) in
# the parent's rest frame, plus the elbow pole. Positions sit just outside each costume.
HOLDS = {
    # The forearm enters the coat under the pocket flap; the hand, turned edge-on, stays
    # behind the cloth (checked vertex by vertex in check_clips).
    "pocket": dict(wrist=(-0.126, -0.050, 1.004), rot=(12.0, -45.0, 4.0), parent="Hips", elbow=(-0.42, 0.26, 1.14),
                   hidden=True),
    "clasp_L": dict(wrist=(0.050, -0.158, 0.962), rot=(-18.0, 20.0, 50.0), parent="Hips", elbow=(0.42, 0.10, 1.05)),
    "clasp_R": dict(wrist=(-0.044, -0.176, 0.972), rot=(-18.0, -20.0, -50.0), parent="Hips", elbow=(-0.42, 0.10, 1.05)),
    "belt_L": dict(wrist=(0.138, -0.148, 1.012), rot=(-14.0, 18.0, 16.0), parent="Hips", elbow=(0.48, 0.24, 1.10)),
    "belt_R": dict(wrist=(-0.138, -0.148, 1.012), rot=(-14.0, -18.0, -16.0), parent="Hips", elbow=(-0.48, 0.24, 1.10)),
}

STYLES = {
    "CharacterBase": dict(
        posture=merge(pose(Spine=[1, 0, 0], Neck=[1, 0, 0], Head=[-1, 0, 0]), RELAXED_L, RELAXED_R,
                      {"hips": [0.0, 0.0, -0.003]}),
        holds={}, breaths=2, breath=1.0, sway=(0.011, 1.8),
        glances=[(0.52, 0.06, 0.12, 0.08, 14.0, 2.0, 0.0)],
        talk=[(0, {}),
              (7, merge(pose(Chest=[-1.5, 0, 0], Head=[-2, 0, 0]), arm("R", fwd=4, bend=10))),
              (15, merge(pose(Chest=[-1, 4, 0], Head=[0, 4, 1]), arm("R", fwd=16, out=6, bend=58, turn=75, flex=-8))),
              (32, merge(pose(Chest=[-1, 3, 0], Head=[1, 2, -1]), arm("R", fwd=20, out=9, bend=66, turn=85, flex=-12))),
              (44, merge(pose(Head=[0, 0, 3]), arm("R", fwd=9, out=4, bend=36, turn=40))),
              (52, merge(pose(Head=[-1, -2, 3], Chest=[-1, 0, 0]), arm("R", fwd=8, bend=30, turn=30),
                         arm("L", fwd=5, bend=22, turn=20))),
              (60, merge(pose(Chest=[-1.5, 0, 0]), arm("R", fwd=17, out=13, bend=56, turn=70),
                         arm("L", fwd=17, out=13, bend=56, turn=70))),
              (77, merge(pose(Head=[1, 0, 0]), arm("R", fwd=15, out=17, bend=52, turn=78),
                         arm("L", fwd=15, out=17, bend=52, turn=78))),
              (87, merge(arm("R", fwd=3, bend=8), arm("L", fwd=3, bend=8))),
              (92, {}), (96, {})],
        beats=[(17, 1.0), (25, 0.8), (34, 1.0), (61, 0.9), (69, 0.8), (77, 1.0)], beat_arms="RL",
    ),
    "AlexeyVoron": dict(
        posture=merge(pose(Spine=[3, 0, 0], Chest=[4, 0, 0], Neck=[2, 0, 0], Head=[3, -3, -2]),
                      shoulders(shrug=-2, fwd=4), RELAXED_L, arm("R", fwd=8, out=4, bend=40), ik("R", 1.0),
                      {"hips": [-0.006, 0.0, -0.004]}),
        holds={"R": "pocket"}, breaths=2, breath=1.3, sway=(0.009, 1.5),
        glances=[(0.70, 0.06, 0.12, 0.08, -11.0, 3.0, 0.0)],
        sighs=[(0.22, 0.10, 0.08, 0.16, 1.0)],
        talk=[(0, {}),
              (8, merge(pose(Chest=[-2, 0, 0], Head=[-3, 3, 1]), arm("L", fwd=4, bend=8))),
              (16, merge(pose(Chest=[-1, -3, 0], Head=[-1, -1, 2]), arm("L", fwd=15, out=5, bend=60, turn=80, flex=-10))),
              (34, merge(pose(Chest=[-1, -3, 0], Head=[0, -2, 1]), arm("L", fwd=18, out=7, bend=64, turn=90, flex=-14))),
              (44, merge(pose(Head=[2, 0, -3]), shoulders(shrug=3), arm("L", fwd=10, out=6, bend=44, turn=60))),
              (52, merge(pose(Head=[1, 2, -2]), shoulders(shrug=1), arm("L", fwd=8, bend=34, turn=40))),
              (60, merge(pose(Chest=[-1, -4, 0], Head=[-1, -3, 0]), arm("L", fwd=26, out=4, bend=44, turn=35, flex=-12))),
              (76, merge(pose(Chest=[-1, -4, 0], Head=[0, -3, 0]), arm("L", fwd=24, out=6, bend=48, turn=40, flex=-10))),
              (86, merge(pose(Head=[4, 0, 0]), arm("L", fwd=3, bend=6))),
              (92, {}), (96, {})],
        beats=[(18, 0.8), (27, 0.7), (35, 0.9), (62, 1.0), (70, 0.8), (77, 0.9)], beat_arms="L",
    ),
    "ElenaVoron": dict(
        posture=merge(pose(Hips=[0, 3, -2.5], Spine=[0, -2, 1.4], Chest=[-1, -1, 0.8], Head=[-1, 0, 4]),
                      arm("L", fwd=9, out=-2, bend=24, turn=40, flex=4), arm("R", fwd=9, out=-2, bend=24, turn=40, flex=4),
                      {"hips": [0.014, 0.0, -0.011]}),
        holds={}, breaths=3, breath=0.8, sway=(0.007, 1.3),
        glances=[(0.20, 0.07, 0.12, 0.08, 15.0, 1.0, 2.0), (0.70, 0.06, 0.10, 0.08, -9.0, -1.0, -3.0)],
        talk=[(0, {}),
              (7, merge(pose(Chest=[-2, 0, 0], Head=[-2, 0, -2]), arm("L", fwd=4, bend=6), arm("R", fwd=4, bend=6))),
              (15, merge(pose(Head=[-1, 3, -3]), arm("L", fwd=16, out=10, bend=62, turn=80),
                         arm("R", fwd=16, out=10, bend=62, turn=80))),
              (31, merge(pose(Head=[0, 4, -1]), arm("L", fwd=18, out=14, bend=66, turn=90),
                         arm("R", fwd=18, out=14, bend=66, turn=90))),
              (44, merge(pose(Head=[1, 0, 5]), shoulders(shrug=5), arm("L", fwd=10, out=12, bend=48, turn=90),
                         arm("R", fwd=10, out=12, bend=48, turn=90))),
              (52, merge(pose(Head=[0, 0, 3]), shoulders(shrug=1), arm("L", fwd=6, bend=30, turn=40),
                         arm("R", fwd=6, bend=30, turn=40))),
              (60, merge(pose(Chest=[-1, 3, 0], Head=[-1, 2, 0]), arm("R", fwd=28, out=6, bend=52, turn=95, flex=-10),
                         arm("L", fwd=6, bend=26, turn=40))),
              (78, merge(pose(Chest=[-1, 3, 0], Head=[0, 2, 1]), arm("R", fwd=26, out=8, bend=56, turn=95, flex=-12),
                         arm("L", fwd=6, bend=26, turn=40))),
              (87, merge(arm("R", fwd=3, bend=8), arm("L", fwd=3, bend=8))),
              (92, {}), (96, {})],
        beats=[(17, 0.8), (25, 0.7), (33, 0.8), (62, 0.8), (70, 0.7), (78, 0.8)], beat_arms="RL",
        shake=(62, 80, 5.0),
    ),
    "DrIlyaMorozov": dict(
        posture=merge(pose(Spine=[-1, 0, 0], Chest=[-2, 0, 0], Neck=[4, 0, 0], Head=[2, 0, 0]),
                      RELAXED_L, RELAXED_R, ik("L", 1.0), ik("R", 1.0), {"hips": [0.0, 0.0, -0.002]}),
        holds={"L": "clasp_L", "R": "clasp_R"}, breaths=2, breath=0.9, sway=(0.005, 1.0),
        glances=[(0.36, 0.08, 0.14, 0.10, 3.0, 8.0, 0.0), (0.78, 0.06, 0.08, 0.06, 10.0, 0.0, 0.0)],
        talk=[(0, {}),
              (6, pose(Chest=[-1.5, 0, 0], Head=[-2, 0, 0])),
              (14, merge(pose(Chest=[-1, 3, 0], Head=[-1, 3, 0]), ik("R", -1.0), arm("R", fwd=28, out=6, bend=70, turn=45))),
              (33, merge(pose(Chest=[-1, 3, 0], Head=[0, 2, 0]), ik("R", -1.0), arm("R", fwd=31, out=8, bend=74, turn=50))),
              (44, merge(pose(Head=[3, 0, 0]), ik("R", -1.0), arm("R", fwd=22, out=6, bend=64, turn=45))),
              (54, merge(pose(Head=[1, 0, 0]), ik("R", 0.0))),
              (62, merge(pose(Chest=[-1, 0, 0], Head=[-1, 0, 0]), shoulders(shrug=2))),
              (80, pose(Head=[1, -2, 0])),
              (90, {}), (96, {})],
        beats=[(18, 1.0), (26, 0.9), (35, 1.0), (63, 0.9), (71, 0.9), (79, 0.8)], beat_arms="R",
    ),
    "PoliceOfficer": dict(
        posture=merge(pose(Spine=[-2, 0, 0], Chest=[-3, 0, 0], Head=[-3, 0, 0]), shoulders(fwd=-4),
                      RELAXED_L, RELAXED_R, ik("L", 1.0), ik("R", 1.0), {"hips": [0.0, 0.0, -0.002]}),
        holds={"L": "belt_L", "R": "belt_R"}, breaths=2, breath=1.0, sway=(0.004, 0.8),
        glances=[(0.24, 0.10, 0.14, 0.10, -18.0, 0.0, 0.0), (0.66, 0.08, 0.10, 0.08, 12.0, 2.0, 0.0)],
        talk=[(0, {}),
              (6, pose(Chest=[-1, 0, 0], Head=[-2, 0, 0])),
              (15, merge(pose(Head=[1, 0, 0]), ik("R", -1.0), arm("R", fwd=46, out=10, bend=58, flex=-62))),
              (34, merge(pose(Head=[1, 1, 0]), ik("R", -1.0), arm("R", fwd=48, out=11, bend=56, flex=-68))),
              (44, merge(pose(Head=[0, 3, 0]), ik("R", -1.0), arm("R", fwd=24, out=8, bend=40, flex=-20))),
              (58, merge(pose(Chest=[0, 4, 0], Head=[0, 6, 0]), ik("R", -1.0),
                         arm("R", fwd=64, out=12, twist=5, bend=12, flex=-4))),
              (76, merge(pose(Chest=[0, 4, 0], Head=[1, 6, 0]), ik("R", -1.0),
                         arm("R", fwd=62, out=12, twist=5, bend=14, flex=-4))),
              (88, merge(pose(Head=[2, 0, 0]), ik("R", 0.0))),
              (92, {}), (96, {})],
        beats=[(18, 1.2), (26, 1.0), (35, 1.2), (62, 1.1), (70, 1.0), (78, 1.2)], beat_arms="",
    ),
}


def idle_pose(style, frame, frames=IDLE_FRAMES):
    # Every layer is periodic or ends inside the loop, and the pelvis never rises enough
    # to straighten a leg past its rest length, so feet stay planted by the leg IK.
    t = frame / frames
    layers = [style["posture"]]
    inhale = 0.5 - 0.5 * math.cos(2 * math.pi * style["breaths"] * t)
    breath = merge(pose(Chest=[-1.5, 0, 0], Spine=[-0.4, 0, 0], Neck=[1.0, 0, 0], Head=[0.5, 0, 0]),
                   shoulders(shrug=1.5), arm("L", out=0.7), arm("R", out=0.7))
    layers.append(scaled(breath, style["breath"] * inhale))
    weight = math.sin(2 * math.pi * t)  # +1: weight on the left leg
    shift, tilt = style["sway"]
    layers.append(merge({"hips": [shift * weight, 0.0, -0.0015 * abs(weight)]},
                        pose(Hips=[0, 0.6 * tilt * math.sin(2 * math.pi * t + 0.9), -tilt * weight],
                             Spine=[0, 0, 0.6 * tilt * weight], Chest=[0, 0, 0.3 * tilt * weight],
                             Head=[0, 0, 0.2 * tilt * weight])))
    for start, rise, hold, fall, yaw, pitch, roll in style.get("glances", ()):
        amount = envelope(t, start, rise, hold, fall)
        layers.append(scaled(pose(Chest=[0, 0.15 * yaw, 0], Neck=[0.4 * pitch, 0.35 * yaw, 0.4 * roll],
                                  Head=[0.6 * pitch, 0.5 * yaw, 0.6 * roll]), amount))
    for start, rise, hold, fall, amount in style.get("sighs", ()):
        rise_amount = envelope(t, start, rise, 0.0, 0.001)
        drop_amount = envelope(t, start + rise, 0.001, hold, fall)
        layers.append(scaled(merge(pose(Chest=[-3.5, 0, 0], Head=[-2, 0, 0]), shoulders(shrug=4)), amount * rise_amount))
        layers.append(scaled(merge(pose(Chest=[2.5, 0, 0], Head=[3, 0, 0]), shoulders(shrug=-2.5)), amount * drop_amount))
    layers.append(pose(Head=[0.6 * math.sin(2 * math.pi * 3 * t + 1.3), 0.8 * math.sin(2 * math.pi * 2 * t + 0.4), 0]))
    return merge(*layers)


def talk_pose(style, frame):
    layers = [idle_pose(style, 0), hermite(style["talk"], frame)]
    inhale = bump(frame, 6, 6) + 0.8 * bump(frame, 51, 6)
    layers.append(scaled(merge(pose(Chest=[-1.8, 0, 0], Neck=[1.0, 0, 0]), shoulders(shrug=1.8)), inhale))
    accent = sum(amount * bump(frame, beat, 4.5) for beat, amount in style["beats"])
    layers.append(scaled(pose(Neck=[0.9, 0, 0], Head=[2.6, 0, 0], Chest=[0.5, 0, 0]), accent))
    for side in style["beat_arms"]:
        layers.append(scaled(arm(side, bend=6, flex=-4), accent))
    if "shake" in style:
        start, end, amplitude = style["shake"]
        if start < frame < end:
            fade = math.sin(math.pi * (frame - start) / (end - start))
            layers.append(pose(Head=[0, amplitude * fade * math.sin(2 * math.pi * (frame - start) / 8.0), 0]))
    return merge(*layers)


# Rig ------------------------------------------------------------------------------

def _signed_angle(u, v, normal):
    angle = u.angle(v)
    return -angle if u.cross(v).angle(normal) < 1.0 else angle


def _pole_angle(base, tip, pole):
    pole_normal = (tip.tail - base.head).cross(pole - base.head)
    projected = pole_normal.cross(base.tail - base.head)
    return _signed_angle(base.x_axis, projected, base.tail - base.head)


def _control(edit_bones, name, head, tail, parent=None, roll=0.0):
    bone = edit_bones.new(name)
    bone.head, bone.tail, bone.roll = head, tail, roll
    bone.parent = edit_bones[parent] if parent else None
    bone.use_deform = False
    return bone


def _char_rotation(pitch, yaw, roll, limb):
    rx = Matrix.Rotation(math.radians(pitch), 3, "X")
    ry = Matrix.Rotation(math.radians(roll), 3, "Y")
    rz = Matrix.Rotation(math.radians(yaw), 3, "Z")
    return rx @ ry @ rz if limb else rz @ rx @ ry


def add_control_rig(armature, style):
    """IK legs for every character and IK hand holds where the style asks for them."""
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    bones = armature.data.edit_bones
    angles = {}
    for side, (name, s) in SIDES.items():
        foot = bones[f"{name}Foot"]
        _control(bones, f"CTRL_Foot.{side}", foot.head.copy(), foot.tail.copy(), roll=foot.roll)
        knee = _control(bones, f"CTRL_Knee.{side}", Vector((s * 0.13, -0.5, 0.54)), Vector((s * 0.13, -0.5, 0.59)))
        angles[f"{name}LowerLeg"] = _pole_angle(bones[f"{name}UpperLeg"], bones[f"{name}LowerLeg"], knee.head)
        if side in style["holds"]:
            spec = HOLDS[style["holds"][side]]
            hand = bones[f"{name}Hand"]
            target = _control(bones, f"CTRL_Hand.{side}", hand.head.copy(), hand.tail.copy(), spec["parent"], hand.roll)
            rotation = _char_rotation(*spec["rot"], limb=False) @ hand.matrix.to_3x3()
            target.matrix = Matrix.Translation(spec["wrist"]) @ rotation.to_4x4()
            elbow = _control(bones, f"CTRL_Elbow.{side}", Vector(spec["elbow"]),
                             Vector(spec["elbow"]) + Vector((0.0, 0.0, 0.05)), "Chest")
            angles[f"{name}LowerArm"] = _pole_angle(bones[f"{name}UpperArm"], bones[f"{name}LowerArm"], elbow.head)
    bpy.ops.object.mode_set(mode="OBJECT")

    poses = armature.pose.bones
    for side, (name, _) in SIDES.items():
        solver = poses[f"{name}LowerLeg"].constraints.new("IK")
        solver.name = "IK"
        solver.target, solver.subtarget = armature, f"CTRL_Foot.{side}"
        solver.pole_target, solver.pole_subtarget = armature, f"CTRL_Knee.{side}"
        solver.pole_angle, solver.chain_count, solver.use_stretch = angles[f"{name}LowerLeg"], 2, False
        planted = poses[f"{name}Foot"].constraints.new("COPY_ROTATION")
        planted.name = "Planted"
        planted.target, planted.subtarget = armature, f"CTRL_Foot.{side}"
        if side in style["holds"]:
            solver = poses[f"{name}LowerArm"].constraints.new("IK")
            solver.name = "Hold"
            solver.target, solver.subtarget = armature, f"CTRL_Hand.{side}"
            solver.pole_target, solver.pole_subtarget = armature, f"CTRL_Elbow.{side}"
            solver.pole_angle, solver.chain_count, solver.use_stretch = angles[f"{name}LowerArm"], 2, False
            grip = poses[f"{name}Hand"].constraints.new("COPY_ROTATION")
            grip.name = "Hold"
            grip.target, grip.subtarget = armature, f"CTRL_Hand.{side}"
    for bone in poses:
        bone.rotation_mode = "QUATERNION"


def _set_pose(armature, item, rest):
    poses = armature.pose.bones
    for name in POSED_BONES:
        pitch, yaw, roll = item.get(name, (0.0, 0.0, 0.0))
        world = _char_rotation(pitch, yaw, roll, name in LIMB_BONES).to_quaternion()
        local = rest[name].inverted() @ world @ rest[name]
        if local.dot(poses[name].rotation_quaternion) < 0.0:
            local.negate()
        poses[name].rotation_quaternion = local
    poses["Hips"].location = rest["Hips"].inverted() @ Vector(item.get("hips", (0.0, 0.0, 0.0)))
    for side, (name, _) in SIDES.items():
        influence = max(0.0, min(1.0, item.get(f"ik.{side}", [0.0])[0]))
        for bone in (f"{name}LowerArm", f"{name}Hand"):
            constraint = poses[bone].constraints.get("Hold")
            if constraint is not None:
                constraint.influence = influence


def _key_pose(armature, frame):
    poses = armature.pose.bones
    for name in POSED_BONES:
        poses[name].keyframe_insert("rotation_quaternion", frame=frame, group=name)
    poses["Hips"].keyframe_insert("location", frame=frame, group="Hips")
    for name in ("LeftLowerArm", "LeftHand", "RightLowerArm", "RightHand"):
        constraint = poses[name].constraints.get("Hold")
        if constraint is not None:
            constraint.keyframe_insert("influence", frame=frame)


def build_clips(record):
    """Create the control rig, the Idle and Talk actions and one NLA track per FBX take."""
    armature = record["armature"]
    style = STYLES[record["id"]]
    add_control_rig(armature, style)
    rest = {name: armature.data.bones[name].matrix_local.to_quaternion() for name in POSED_BONES}
    preferences = bpy.context.preferences.edit
    interpolation = preferences.keyframe_new_interpolation_type
    preferences.keyframe_new_interpolation_type = "LINEAR"
    data = armature.animation_data_create()
    actions = {}
    try:
        for clip, frames, evaluate in (("Idle", IDLE_FRAMES, lambda f: idle_pose(style, f)),
                                       ("Talk", TALK_FRAMES, lambda f: talk_pose(style, f))):
            action = bpy.data.actions.new(f"{record['id']}_{clip}")
            data.action = action
            for frame in range(frames + 1):
                _set_pose(armature, evaluate(frame), rest)
                _key_pose(armature, frame)
            actions[clip] = action
    finally:
        preferences.keyframe_new_interpolation_type = interpolation
    data.action = None
    for clip, action in actions.items():
        track = data.nla_tracks.new()
        track.name = clip
        strip = track.strips.new(clip, 0, action)
        strip.name = clip
        if hasattr(strip, "action_slot") and strip.action_slot is None and len(action.slots):
            strip.action_slot = action.slots[0]
    record["actions"] = actions
    _reset(armature)
    return actions


def _reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        bone.location = (0.0, 0.0, 0.0)


def play(record, clip):
    """Evaluate one clip directly (NLA muted) for checks and previews; clip None restores NLA."""
    data = record["armature"].animation_data
    for track in data.nla_tracks:
        track.mute = clip is not None
    data.action = record["actions"][clip] if clip else None
    if clip is None:
        _reset(record["armature"])


def sample(record, frame):
    """Joint positions and world-space rotation deltas from the bind pose, in armature space."""
    bpy.context.scene.frame_set(frame)
    evaluated = record["armature"].evaluated_get(bpy.context.evaluated_depsgraph_get())
    result = {}
    for name in SAMPLE_BONES:
        bone = evaluated.pose.bones[name]
        delta = bone.matrix.to_quaternion() @ evaluated.data.bones[name].matrix_local.to_quaternion().inverted()
        result[name] = (bone.head.copy(), delta)
    return result


def compare(first, second):
    """Largest joint distance (m) and rotation difference (degrees) between two samples."""
    distance = max((first[name][0] - second[name][0]).length for name in first)
    angle = max(math.degrees(first[name][1].rotation_difference(second[name][1]).angle) for name in first)
    return distance, min(angle, 360.0 - angle)


def check_clips(record):
    """Loops close, Talk starts and ends in the Idle pose, feet stay planted, holds reach."""
    name = record["id"]
    play(record, "Idle")
    start = sample(record, 0)
    distance, angle = compare(start, sample(record, IDLE_FRAMES))
    if distance > 1e-4 or angle > 0.05:
        raise ValueError(f"{name}: Idle does not loop ({distance * 1000:.2f} mm, {angle:.3f}°)")
    rest_feet = {side: record["armature"].data.bones[f"{side}Foot"].head_local.copy() for side in ("Left", "Right")}
    references = {"Idle": {}, "Talk": {}}
    for frame in range(0, IDLE_FRAMES + 1, 4):
        current = sample(record, frame)
        if frame % 12 == 0:
            references["Idle"][frame] = current
        for side in ("Left", "Right"):
            slip = (current[f"{side}Foot"][0] - rest_feet[side]).length
            if slip > 0.001:
                raise ValueError(f"{name}: {side} foot slides {slip * 1000:.1f} mm at Idle frame {frame}")
        _check_holds(record, frame)
    play(record, "Talk")
    for frame in (0, TALK_FRAMES):
        distance, angle = compare(start, sample(record, frame))
        if distance > 1e-4 or angle > 0.05:
            raise ValueError(f"{name}: Talk frame {frame} differs from the Idle pose")
    for frame in range(0, TALK_FRAMES + 1, 4):
        current = sample(record, frame)
        if frame % 12 == 0:
            references["Talk"][frame] = current
        _check_holds(record, frame)
    play(record, None)
    record["clip_references"] = references
    print(f"{name}: Idle/Talk checks passed (loop, idle-to-talk continuity, planted feet, hand holds)")


def _check_holds(record, frame):
    armature = record["armature"]
    evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
    holds = STYLES[record["id"]]["holds"]
    for side, (name, _) in SIDES.items():
        constraint = evaluated.pose.bones[f"{name}LowerArm"].constraints.get("Hold")
        if constraint is None or constraint.influence < 0.999:
            continue
        wrist = evaluated.pose.bones[f"{name}Hand"].head
        target = evaluated.pose.bones[f"CTRL_Hand.{side}"].head
        if (wrist - target).length > 0.004:
            raise ValueError(f"{record['id']}: {name} hand misses its hold by {(wrist - target).length * 1000:.1f} mm "
                             f"at frame {frame}")
        if HOLDS[holds[side]].get("hidden") and frame % 36 == 0:
            _check_hidden_hand(record, name, frame)


def _check_hidden_hand(record, name, frame):
    from mathutils.bvhtree import BVHTree

    depsgraph = bpy.context.evaluated_depsgraph_get()
    armature = record["armature"].evaluated_get(depsgraph)
    cloth = record["objects"]["Clothing"].evaluated_get(depsgraph).to_mesh()
    tree = BVHTree.FromPolygons([v.co.copy() for v in cloth.vertices], [tuple(p.vertices) for p in cloth.polygons])
    body = record["objects"]["Body"]
    group = body.vertex_groups[f"{name}Hand"].index
    hand = {v.index for v in body.data.vertices if any(g.group == group and g.weight > 0.5 for g in v.groups)}
    mesh = body.evaluated_get(depsgraph).to_mesh()
    for index in hand:
        point = mesh.vertices[index].co
        hit = tree.ray_cast(Vector((point.x, -1.0, point.z)), Vector((0.0, 1.0, 0.0)), 2.0)
        if hit[0] is None or hit[0].y > point.y:
            raise ValueError(f"{record['id']}: {name} hand shows outside the coat at frame {frame} "
                             f"({point.x:.3f}, {point.y:.3f}, {point.z:.3f})")
    record["objects"]["Clothing"].evaluated_get(depsgraph).to_mesh_clear()
    body.evaluated_get(depsgraph).to_mesh_clear()
    del armature
