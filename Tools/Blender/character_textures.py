"""Original pixel textures for the authored PS1 characters.

Every character has a 128 px face map, shared by its Face and Eyes meshes, and a
256 px atlas for skin, hair, clothing and accessories. All pixels are generated
here; no photographs, traced likenesses or third-party textures are used.
"""

from __future__ import annotations

import math
import random

from pixel_art import Canvas

ATLAS_SIZE = 256
FACE_SIZE = 128
# Face UVs are planar in unscaled head space, so every face map shares one layout.
FACE_HALF_WIDTH = 0.088
FACE_BOTTOM = 1.545
FACE_TOP = 1.782
GARMENT_Z = (0.55, 1.52)
GARMENT_ARC = 0.34
GARMENT_BACK_ARC = 0.29
SLEEVE_Z = (0.86, 1.44)
TROUSER_Z = (0.10, 1.05)
ISLANDS = {
    "front": (0, 0, 128, 144), "back": (128, 0, 256, 144),
    "sleeve": (0, 144, 64, 256), "trousers": (64, 144, 128, 256),
    "hair": (128, 144, 192, 208), "shirt": (192, 144, 224, 176),
    "accent": (224, 144, 256, 176), "skin": (192, 176, 224, 208),
    "cap": (224, 176, 256, 208), "leather": (128, 208, 160, 240),
    "lining": (160, 208, 192, 240), "metal": (192, 208, 208, 224),
    "dark": (208, 208, 224, 224), "trim": (224, 208, 256, 224),
    "sole": (128, 240, 192, 256), "knit": (192, 224, 256, 256),
}
BAYER = ((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5))


def rgba(color):
    return tuple(max(0, min(255, int(round(c)))) for c in color[:3]) + (255,)


def mix(a, b, amount):
    return tuple(a[i] + (b[i] - a[i]) * amount for i in range(3))


def shift(color, amount):
    return tuple(c + amount for c in color[:3])


def gauss(dx, dy=0.0):
    return math.exp(-(dx * dx + dy * dy))


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def face_px(x, z):
    """Face-map pixel of a point in unscaled head space (inverse of the Face UVs)."""
    return ((x / FACE_HALF_WIDTH + 1.0) * 0.5 * FACE_SIZE,
            (1.0 - (z - FACE_BOTTOM) / (FACE_TOP - FACE_BOTTOM)) * FACE_SIZE)


CX = FACE_SIZE / 2.0
EYE_DX = face_px(0.034, FACE_BOTTOM)[0] - CX
EYE_Y = face_px(0.0, 1.6615)[1]
BROW_Y = face_px(0.0, 1.681)[1]
NOSE_Y = face_px(0.0, 1.625)[1]
NOSTRIL_Y = face_px(0.0, 1.617)[1]
MOUTH_Y = face_px(0.0, 1.594)[1]
CHIN_Y = face_px(0.0, 1.556)[1]


def island_px(name, u, v):
    x0, y0, x1, y1 = ISLANDS[name]
    return x0 + 1 + u * (x1 - x0 - 2), y1 - 1 - v * (y1 - y0 - 2)


def garment_px(name, s, z):
    """Torso-island pixel for arc position s (close to x near the centre line) and height z."""
    arc = GARMENT_ARC if name == "front" else GARMENT_BACK_ARC
    return island_px(name, 0.5 + s / (2 * arc), (z - GARMENT_Z[0]) / (GARMENT_Z[1] - GARMENT_Z[0]))


def limb_px(name, u, z):
    low, high = SLEEVE_Z if name == "sleeve" else TROUSER_Z
    return island_px(name, u, (z - low) / (high - low))


def _get(canvas, x, y):
    index = (y * canvas.width + x) * 4
    return tuple(canvas.pixels[index:index + 3])


def _segment_distance(px, py, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = dx * dx + dy * dy
    t = 0.0 if length == 0 else max(0.0, min(1.0, ((px - a[0]) * dx + (py - a[1]) * dy) / length))
    return math.hypot(px - a[0] - t * dx, py - a[1] - t * dy)


def _polyline_distance(px, py, points):
    return min(_segment_distance(px, py, a, b) for a, b in zip(points, points[1:]))


def raster(points, width, height):
    """Pixels whose centres fall inside a float polygon."""
    inside = set()
    ys = [p[1] for p in points]
    for y in range(max(0, int(math.floor(min(ys)))), min(height, int(math.ceil(max(ys))) + 1)):
        centre = y + 0.5
        crossings = sorted(x0 + (centre - y0) * (x1 - x0) / (y1 - y0)
                           for (x0, y0), (x1, y1) in zip(points, points[1:] + points[:1])
                           if (y0 <= centre < y1) or (y1 <= centre < y0))
        for start, end in zip(crossings[::2], crossings[1::2]):
            for x in range(max(0, math.ceil(start - 0.5)), min(width, math.floor(end - 0.5) + 1)):
                inside.add((x, y))
    return inside


def fill(canvas, points, color):
    for x, y in raster(points, canvas.width, canvas.height):
        canvas.set(x, y, rgba(color))


def _bounds(points, reach, limit):
    xs, ys = [p[0] for p in points], [p[1] for p in points]
    x0, y0, x1, y1 = limit
    return (max(x0, int(min(xs) - reach)), max(y0, int(min(ys) - reach)),
            min(x1, int(max(xs) + reach) + 1), min(y1, int(max(ys) + reach) + 1))


def stroke(canvas, points, color, width=1.0, limit=None):
    x0, y0, x1, y1 = _bounds(points, width + 1, limit or (0, 0, canvas.width, canvas.height))
    for y in range(y0, y1):
        for x in range(x0, x1):
            if _polyline_distance(x + 0.5, y + 0.5, points) <= width / 2:
                canvas.set(x, y, rgba(color))


def soft(canvas, points, width, amount, limit):
    """Soft tonal band along a polyline; negative amounts darken."""
    x0, y0, x1, y1 = _bounds(points, width * 2, limit)
    for y in range(y0, y1):
        for x in range(x0, x1):
            k = math.exp(-(_polyline_distance(x + 0.5, y + 0.5, points) / width) ** 2)
            if k > 0.03:
                canvas.set(x, y, rgba(shift(_get(canvas, x, y), amount * k)))


FACE_LOOKS = {
    "CharacterBase": {
        "skin": (158, 138, 118), "light": (182, 163, 141), "shade": (124, 104, 90), "deep": (88, 72, 64),
        "lips": (140, 104, 92), "iris": (86, 78, 66), "sclera": (178, 172, 156), "lash": (52, 44, 38),
        "brow": {"color": (72, 58, 46), "thick": 2.2, "arch": 0.8, "drop": 0.6, "inner_drop": 0.0, "bushy": 0.15},
        "eye": {"w": 10.0, "h": 4.2, "lid": 0.14, "iris_r": 3.0, "tilt": 0.2},
        "mouth": {"w": 10.0, "upper": 2.0, "lower": 2.6, "down": 0.3},
        "bags": 0.08, "hollow": 0.08, "naso": 0.10,
        "hairline": (22.7, 22.0), "hair_shadow": (92, 76, 62), "extras": (),
    },
    "AlexeyVoron": {
        "skin": (156, 136, 114), "light": (180, 160, 136), "shade": (120, 100, 86), "deep": (84, 68, 60),
        "lips": (136, 100, 88), "iris": (84, 96, 84), "sclera": (174, 168, 152), "lash": (42, 36, 32),
        "brow": {"color": (46, 39, 33), "thick": 2.6, "arch": 0.4, "drop": 1.2, "inner_drop": 0.7, "bushy": 0.25},
        "eye": {"w": 10.0, "h": 4.0, "lid": 0.34, "iris_r": 2.9, "tilt": -0.3},
        "mouth": {"w": 10.0, "upper": 1.8, "lower": 2.4, "down": 0.9},
        "bags": 0.22, "hollow": 0.20, "naso": 0.20,
        "beard": {"color": (100, 90, 82), "density": 0.55},
        "hairline": (24.8, 20.5), "hair_shadow": (70, 60, 52), "extras": ("crows_feet_light",),
    },
    "ElenaVoron": {
        "skin": (176, 150, 130), "light": (198, 176, 156), "shade": (142, 114, 100), "deep": (104, 82, 74),
        "lips": (154, 98, 94), "iris": (90, 108, 88), "sclera": (186, 180, 166), "lash": (44, 34, 30),
        "brow": {"color": (100, 60, 42), "thick": 1.6, "arch": 1.8, "drop": 0.8, "inner_drop": -0.2, "bushy": 0.05},
        "eye": {"w": 10.5, "h": 4.8, "lid": 0.08, "iris_r": 3.2, "tilt": 0.6, "lashes": True},
        "mouth": {"w": 9.0, "upper": 2.4, "lower": 3.2, "down": 0.2},
        "bags": 0.05, "hollow": 0.06, "naso": 0.05, "blush": 0.10,
        "hairline": (30.0, 27.0), "hair_shadow": (112, 78, 62), "extras": ("freckles",),
    },
    "DrIlyaMorozov": {
        "skin": (170, 146, 132), "light": (192, 170, 156), "shade": (134, 110, 100), "deep": (94, 76, 72),
        "lips": (132, 100, 94), "iris": (98, 108, 114), "sclera": (174, 168, 150), "lash": (70, 64, 60),
        "brow": {"color": (128, 126, 120), "thick": 3.1, "arch": 1.2, "drop": 1.6, "inner_drop": 0.2, "bushy": 0.55},
        "eye": {"w": 9.5, "h": 3.6, "lid": 0.30, "iris_r": 2.7, "tilt": -0.6},
        "mouth": {"w": 10.5, "upper": 1.5, "lower": 2.0, "down": 1.2},
        "bags": 0.30, "hollow": 0.16, "naso": 0.32,
        "extras": ("forehead_lines", "crows_feet", "jowls", "age_spots"),
    },
    "PoliceOfficer": {
        "skin": (160, 126, 102), "light": (184, 152, 124), "shade": (124, 94, 76), "deep": (86, 62, 50),
        "lips": (134, 98, 86), "iris": (70, 64, 56), "sclera": (174, 168, 152), "lash": (38, 33, 29),
        "brow": {"color": (40, 34, 30), "thick": 3.0, "arch": 0.3, "drop": 0.4, "inner_drop": 1.2, "bushy": 0.3},
        "eye": {"w": 9.5, "h": 3.8, "lid": 0.24, "iris_r": 2.8, "tilt": -0.2},
        "mouth": {"w": 10.5, "upper": 1.6, "lower": 2.4, "down": 0.6},
        "bags": 0.12, "hollow": 0.12, "naso": 0.16,
        "beard": {"color": (104, 90, 80), "density": 0.3}, "mustache": (40, 34, 30), "extras": (),
    },
}


def _face_shade(px, py, look):
    ax = abs(px - CX)
    shade = smoothstep(42.0, 60.0, ax) * 0.42
    shade += smoothstep(CHIN_Y + 1.0, CHIN_Y + 6.5, py) * 0.85
    shade -= gauss((px - CX) / 24.0, (py - 30.0) / 16.0) * 0.16
    shade += gauss((ax - 52.0) / 7.0, (py - (BROW_Y - 8.0)) / 11.0) * 0.18
    for side in (-1, 1):
        cx = CX + side * EYE_DX
        shade += gauss((px - cx) / 11.0, (py - (EYE_Y - 1.5)) / 6.0) * 0.30
        shade += gauss((px - (cx - side * 8.5)) / 4.5, (py - (EYE_Y - 3.0)) / 4.0) * 0.22
        shade -= gauss((px - cx) / 13.0, (py - (BROW_Y - 3.5)) / 3.5) * 0.12
        shade += gauss((px - cx) / 9.5, (py - (EYE_Y + 6.5)) / 2.4) * look["bags"]
        fold = _segment_distance(px, py, (CX + side * 9.5, NOSTRIL_Y), (CX + side * 15.5, MOUTH_Y + 1.5))
        shade += math.exp(-(fold / 1.5) ** 2) * look["naso"]
    shade -= gauss((px - CX) / 2.4, (py - (NOSE_Y - 10.0)) / 10.0) * 0.22
    shade += gauss((ax - 5.5) / 2.2, (py - (NOSE_Y - 8.0)) / 9.0) * 0.30
    shade += gauss((px - CX) / 6.5, (py - (NOSTRIL_Y + 1.5)) / 2.0) * 0.55
    shade += gauss((ax - 7.0) / 2.6, (py - (NOSE_Y + 2.0)) / 2.6) * 0.28
    shade -= gauss((ax - 36.0) / 9.0, (py - (NOSE_Y - 8.0)) / 5.0) * 0.14
    shade += gauss((ax - 37.0) / 8.0, (py - (NOSE_Y + 6.0)) / 8.0) * look["hollow"]
    shade += gauss((px - CX) / 9.0, (py - (MOUTH_Y + 6.5)) / 2.0) * 0.30
    shade += gauss((ax - 14.0) / 2.4, (py - MOUTH_Y) / 2.2) * 0.25
    shade -= gauss((px - CX) / 9.0, (py - (CHIN_Y - 4.0)) / 4.0) * 0.14
    shade += gauss((ax - 46.0) / 8.0, (py - (MOUTH_Y + 10.0)) / 9.0) * 0.22
    return shade


def _beard_cover(px, py):
    ax = abs(px - CX)
    jaw = smoothstep(NOSE_Y + 3.0, MOUTH_Y - 2.0, py) * (1.0 - smoothstep(44.0, 55.0, ax))
    lip = smoothstep(NOSTRIL_Y + 1.5, NOSTRIL_Y + 4.0, py) * (1.0 - smoothstep(13.0, 18.0, ax))
    cover = max(jaw, lip)
    if ax < 12.0 and MOUTH_Y - 3.5 < py < MOUTH_Y + 4.5:
        cover *= 0.1
    return cover * (1.0 - 0.5 * smoothstep(CHIN_Y + 2.0, CHIN_Y + 7.0, py))


def _eye(canvas, side, look, closed=False):
    eye = look["eye"]
    cx, cy = CX + side * EYE_DX, EYE_Y
    w, h, lift = eye["w"], eye["h"], eye.get("tilt", 0.0)
    inner, outer = cx - side * w, cx + side * w
    top = [(outer, cy - lift), (cx + side * w * 0.5, cy - h * 0.85), (cx - side * w * 0.1, cy - h),
           (cx - side * w * 0.65, cy - h * 0.72), (inner, cy)]
    bottom = [(inner + side * 1.2, cy + 1.0), (cx - side * w * 0.25, cy + h * 0.5),
              (cx + side * w * 0.45, cy + h * 0.42), (outer - side * 0.8, cy + 0.5 - lift)]
    if closed:
        # The upper lid covers the opening and its lashes meet the lower lid.
        for x, y in raster(top + bottom, FACE_SIZE, FACE_SIZE):
            lid = smoothstep(cy - h, cy + h * 0.4, y + 0.5)
            canvas.set(x, y, rgba(mix(mix(look["skin"], look["light"], 0.25), look["shade"], 0.2 + 0.35 * lid)))
        seam = [(outer + side * 1.5, cy - lift + 0.2), (cx + side * w * 0.45, cy + h * 0.3),
                (cx - side * w * 0.25, cy + h * 0.36), (inner, cy + 0.4)]
        stroke(canvas, seam, look["lash"], 1.5)
        stroke(canvas, [(x, y - 2.0) for x, y in top[:-1]], mix(look["skin"], look["shade"], 0.7), 1.0)
        return
    iris_x, iris_y = cx - side * 0.4, cy - 0.4
    lid = cy - h + eye["lid"] * h * 1.5
    opening = raster(top + bottom, FACE_SIZE, FACE_SIZE)
    for x, y in opening:
        px, py = x + 0.5, y + 0.5
        distance = math.hypot(px - iris_x, (py - iris_y) * 1.1)
        if py < lid:
            color = mix(look["shade"], look["skin"], 0.3)
        elif distance <= 1.3:
            color = (26, 24, 22)
        elif distance <= eye["iris_r"]:
            color = look["iris"] if distance < eye["iris_r"] - 0.9 else mix(look["iris"], (28, 26, 24), 0.55)
        else:
            dim = smoothstep(cy - h * 0.1, cy - h, py) * 0.5 + smoothstep(w * 0.5, w, abs(px - cx)) * 0.3
            color = mix(look["sclera"], look["shade"], dim)
        canvas.set(x, y, rgba(color))
    glint = (int(iris_x - 1.3), int(iris_y - 1.2))
    if glint in opening and glint[1] + 0.5 >= lid:
        canvas.set(glint[0], glint[1], rgba(mix(look["sclera"], (240, 236, 222), 0.6)))
    stroke(canvas, [(outer + side * 2.0, cy - lift - 1.4)] + top, look["lash"], 1.5)
    stroke(canvas, [(x, y - 2.8) for x, y in top[:-1]], mix(look["skin"], look["shade"], 0.75), 1.0)
    stroke(canvas, bottom, mix(look["skin"], look["shade"], 0.55), 1.0)
    if eye.get("lashes"):
        for k in range(3):
            x = outer - side * (1.5 + k * 2.2)
            stroke(canvas, [(x, cy - h * 0.8 + k * 0.4 - 0.2), (x + side * 1.4, cy - h * 0.8 - 1.6 + k * 0.5)],
                   look["lash"], 1.0)


def _brow(canvas, side, look, rng):
    brow = look["brow"]
    cx = CX + side * EYE_DX
    inner = cx - side * (look["eye"]["w"] + 0.5)
    outer = cx + side * (look["eye"]["w"] + 2.5)
    centre, top, bottom = [], [], []
    for k in range(6):
        t = k / 5
        x = inner + (outer - inner) * t
        y = (BROW_Y + brow["inner_drop"] * (1 - t) ** 2 + brow["drop"] * t * t
             - brow["arch"] * math.sin(math.pi * min(1.0, t * 1.15)))
        half = brow["thick"] * (1.0 - 0.45 * t) / 2
        centre.append((x, y))
        top.append((x, y - half))
        bottom.append((x, y + half))
    shape = raster(top + bottom[::-1], FACE_SIZE, FACE_SIZE)
    for x, y in shape:
        canvas.set(x, y, rgba(shift(brow["color"], rng.uniform(-7, 7))))
    x0, y0, x1, y1 = _bounds(centre, brow["thick"] + 2, (0, 0, FACE_SIZE, FACE_SIZE))
    for y in range(y0, y1):
        for x in range(x0, x1):
            near = _polyline_distance(x + 0.5, y + 0.5, centre) < brow["thick"] * 0.5 + 1.3
            if (x, y) not in shape and near and rng.random() < brow["bushy"]:
                canvas.set(x, y, rgba(mix(brow["color"], look["skin"], 0.35)))


def _nose(canvas, look):
    nostril = mix(look["deep"], look["shade"], 0.3)
    for side in (-1, 1):
        fill(canvas, [(CX + side * 3.4, NOSTRIL_Y - 0.6), (CX + side * 6.4, NOSTRIL_Y - 0.9),
                      (CX + side * 6.8, NOSTRIL_Y + 0.6), (CX + side * 3.6, NOSTRIL_Y + 0.9)], nostril)
    stroke(canvas, [(CX - 2.0, NOSE_Y - 1.0), (CX, NOSE_Y - 2.2), (CX + 2.0, NOSE_Y - 1.0)],
           mix(look["skin"], look["light"], 0.6), 1.0)


def _mouth(canvas, look, open_mouth=False):
    mouth = look["mouth"]
    w, upper_h, lower_h, down = mouth["w"], mouth["upper"], mouth["lower"], mouth.get("down", 0.0)
    y = MOUTH_Y
    line = [(CX - w, y + down), (CX - w * 0.5, y + 0.2), (CX + w * 0.5, y + 0.2), (CX + w, y + down)]
    upper = [(CX - w, y + down), (CX - w * 0.5, y - upper_h * 0.8), (CX - 1.6, y - upper_h),
             (CX, y - upper_h + 0.8), (CX + 1.6, y - upper_h), (CX + w * 0.5, y - upper_h * 0.8),
             (CX + w, y + down)]
    fill(canvas, upper + [line[2], line[1]], mix(look["lips"], look["shade"], 0.35))
    if open_mouth:
        # Mid-syllable shape: dark cavity, a hint of upper teeth and a dropped lower lip.
        gap = 3.2
        cavity = [(CX - w * 0.78, y + down * 0.7), (CX - w * 0.4, y + 0.2), (CX + w * 0.4, y + 0.2),
                  (CX + w * 0.78, y + down * 0.7), (CX + w * 0.42, y + gap + 0.6), (CX - w * 0.42, y + gap + 0.6)]
        fill(canvas, cavity, mix(look["deep"], (38, 20, 20), 0.55))
        stroke(canvas, [(CX - w * 0.34, y + 0.9), (CX + w * 0.34, y + 0.9)], mix(look["sclera"], look["shade"], 0.25), 1.0)
        lip = [(CX - w * 0.72, y + gap + down * 0.5), (CX, y + gap + 0.4), (CX + w * 0.72, y + gap + down * 0.5),
               (CX + w * 0.42, y + gap + lower_h), (CX - w * 0.42, y + gap + lower_h)]
        fill(canvas, lip, look["lips"])
        stroke(canvas, [(CX - w * 0.26, y + gap + lower_h * 0.55), (CX + w * 0.26, y + gap + lower_h * 0.55)],
               mix(look["lips"], look["light"], 0.35), 1.0)
        stroke(canvas, cavity[:4], mix(look["deep"], look["lips"], 0.3), 1.0)
        return
    fill(canvas, [(CX - w * 0.8, y + 0.3 + down * 0.6), (CX, y + 0.3), (CX + w * 0.8, y + 0.3 + down * 0.6),
                  (CX + w * 0.45, y + lower_h), (CX - w * 0.45, y + lower_h)], look["lips"])
    stroke(canvas, [(CX - w * 0.28, y + lower_h * 0.55), (CX + w * 0.28, y + lower_h * 0.55)],
           mix(look["lips"], look["light"], 0.35), 1.0)
    stroke(canvas, line, mix(look["deep"], look["lips"], 0.35), 1.0)


def _mustache(canvas, look, rng):
    # A broad drooping chevron that spans past the mouth corners.
    shape = [(CX - 19.0, MOUTH_Y + 2.5), (CX - 15.5, MOUTH_Y - 3.5), (CX - 7.0, NOSTRIL_Y + 2.0),
             (CX, NOSTRIL_Y + 2.8), (CX + 7.0, NOSTRIL_Y + 2.0), (CX + 15.5, MOUTH_Y - 3.5),
             (CX + 19.0, MOUTH_Y + 2.5), (CX + 14.5, MOUTH_Y - 0.3), (CX + 6.0, MOUTH_Y - 1.6),
             (CX, MOUTH_Y - 1.1), (CX - 6.0, MOUTH_Y - 1.6), (CX - 14.5, MOUTH_Y - 0.3)]
    for x, y in raster(shape, FACE_SIZE, FACE_SIZE):
        tone = rng.uniform(-8.0, 8.0) + (10.0 if (x + y) % 5 == 0 else 0.0)
        canvas.set(x, y, rgba(shift(look["mustache"], tone)))


def _face_extras(canvas, look, rng):
    extras = look.get("extras", ())
    crease = mix(look["skin"], look["shade"], 0.75)
    light = mix(look["skin"], look["light"], 0.7)
    if "forehead_lines" in extras:
        for offset in (13.0, 18.5, 24.0):
            y = BROW_Y - offset
            points = [(CX - 22.0, y + 1.2), (CX - 9.0, y - 0.3), (CX + 9.0, y - 0.3), (CX + 22.0, y + 1.2)]
            stroke(canvas, points, crease, 1.0)
            stroke(canvas, [(px, py + 1.0) for px, py in points], light, 1.0)
    for side in (-1, 1):
        outer = CX + side * (EYE_DX + look["eye"]["w"])
        if "crows_feet" in extras or "crows_feet_light" in extras:
            for dy in ((-2.0, 0.5, 3.0) if "crows_feet" in extras else (0.5, 3.0)):
                stroke(canvas, [(outer + side * 1.5, EYE_Y + dy * 0.4), (outer + side * 5.5, EYE_Y + dy)], crease, 1.0)
        if "jowls" in extras:
            stroke(canvas, [(CX + side * 15.5, MOUTH_Y + 2.5), (CX + side * 18.5, MOUTH_Y + 10.0),
                            (CX + side * 19.5, MOUTH_Y + 15.0)], crease, 1.0)
    if "age_spots" in extras:
        spot = mix(look["skin"], look["shade"], 0.45)
        for _ in range(9):
            x, y = int(CX + rng.uniform(-34.0, 34.0)), int(rng.uniform(8.0, BROW_Y - 12.0))
            canvas.set(x, y, rgba(spot))
            canvas.set(x + 1, y, rgba(spot))
    if "freckles" in extras:
        freckle = mix(look["skin"], look["shade"], 0.55)
        for _ in range(46):
            x, y = CX + rng.gauss(0.0, 17.0), NOSE_Y - 9.0 + rng.gauss(0.0, 5.5)
            if not (abs(x - CX) < 3.0 and y < NOSE_Y) and 0 <= x < FACE_SIZE and 0 <= y < FACE_SIZE:
                canvas.set(int(x), int(y), rgba(freckle))


def paint_face(path, character_id, mouth_open=False, eyes_closed=False):
    """Paint one face map; the talk and blink variants differ only at the mouth or the eyes."""
    look = FACE_LOOKS[character_id]
    rng = random.Random(f"{character_id}:face")
    canvas = Canvas(FACE_SIZE, FACE_SIZE, rgba(look["skin"]))
    tones = (look["light"], mix(look["light"], look["skin"], 0.5), look["skin"],
             mix(look["skin"], look["shade"], 0.5), look["shade"], look["deep"])
    beard, hairline = look.get("beard"), look.get("hairline")
    for y in range(FACE_SIZE):
        for x in range(FACE_SIZE):
            px, py = x + 0.5, y + 0.5
            threshold = (BAYER[y % 4][x % 4] + 0.5) / 16.0
            color = tones[max(0, min(5, int(math.floor(2.0 + _face_shade(px, py, look) * 3.0 + threshold))))]
            if look.get("blush"):
                warmth = look["blush"] * sum(gauss((px - (CX + s * 30.0)) / 9.0, (py - (NOSE_Y - 2.0)) / 6.0)
                                             for s in (-1, 1))
                if warmth > threshold * 0.6:
                    color = mix(color, (color[0] + 16, color[1] - 4, color[2] - 2), 0.6)
            if beard:
                cover = _beard_cover(px, py) * beard["density"]
                if cover > 0.02:
                    color = mix(color, beard["color"], cover * 0.35)
                    if rng.random() < cover * 0.45:
                        color = mix(color, beard["color"], 0.55)
            if hairline:
                edge = hairline[0] + (hairline[1] - hairline[0]) * min(1.0, ((px - CX) / 34.0) ** 2)
                if smoothstep(edge + 3.0, edge - 4.0, py) > threshold:
                    color = mix(color, look["hair_shadow"], 0.55)
            canvas.set(x, y, rgba(color))
    _face_extras(canvas, look, rng)
    for side in (-1, 1):
        _brow(canvas, side, look, rng)
        _eye(canvas, side, look, eyes_closed)
    _nose(canvas, look)
    _mouth(canvas, look, mouth_open)
    if look.get("mustache"):
        _mustache(canvas, look, rng)
    canvas.save_png(path)


def _fill_island(canvas, name, base, rng, pattern=None):
    x0, y0, x1, y1 = ISLANDS[name]
    for y in range(y0, y1):
        for x in range(x0, x1):
            n = rng.uniform(-2.0, 2.0)
            if pattern == "twill":
                n += -3.0 if (x + y) % 4 == 0 else 0.8
            elif pattern == "knit":
                n += (-5.0 if (x - x0) % 3 == 0 else 1.5) + (-2.0 if (y - y0) % 3 == 0 else 0.0)
            elif pattern == "rib":
                n += -6.0 if (x - x0) % 2 == 0 else 2.0
            elif pattern == "weave":
                n += 1.5 if (x + y) % 2 else -1.5
            canvas.set(x, y, rgba(shift(base, n)))


def _paint_hair(canvas, base, rng, contrast):
    x0, y0, x1, y1 = ISLANDS["hair"]
    strands = [rng.uniform(-8.0, 8.0) * contrast for _ in range(x1 - x0)]
    for y in range(y0, y1):
        v = (y1 - 1 - y) / (y1 - y0 - 1)
        for x in range(x0, x1):
            k = x - x0
            n = strands[(k + (y // 6) % 2) % len(strands)] + rng.uniform(-2.5, 2.5)
            n += 7.0 * contrast * gauss((v - 0.62) / 0.12)
            n -= 9.0 * smoothstep(0.16, 0.0, v)
            if (k * 7 + y // 3) % 11 == 0:
                n -= 11.0 * contrast
            canvas.set(x, y, rgba(shift(base, n)))


def _patch(canvas, name, corners, color, edge=None, top=None):
    points = [garment_px(name, s, z) for s, z in corners]
    fill(canvas, points, color)
    if edge:
        stroke(canvas, points + points[:1], edge, 1.0, ISLANDS[name])
    if top:
        stroke(canvas, points[:2], top, 1.0, ISLANDS[name])


def _seam(canvas, name, points, color, width=1.0):
    stroke(canvas, [garment_px(name, s, z) for s, z in points], color, width, ISLANDS[name])


def _fold(canvas, name, points, width, amount):
    soft(canvas, [garment_px(name, s, z) for s, z in points], width, amount, ISLANDS[name])


def _rib_band(canvas, name, z0, z1, amount=-8):
    x0, y0, x1, y1 = ISLANDS[name]
    top, bottom = garment_px(name, 0.0, z1)[1], garment_px(name, 0.0, z0)[1]
    for y in range(max(y0, int(top)), min(y1, int(bottom) + 1)):
        for x in range(x0, x1):
            if (x - x0) % 2 == 0:
                canvas.set(x, y, rgba(shift(_get(canvas, x, y), amount)))


def _stripes(canvas, name, color, spacing, diagonal=True):
    x0, y0, x1, y1 = ISLANDS[name]
    for y in range(y0, y1):
        for x in range(x0, x1):
            if ((x + y) if diagonal else x) % spacing == 0:
                canvas.set(x, y, rgba(color))


def _limb_details(canvas, colors, cuff=0.905, crease=True):
    sleeve, trousers = colors["sleeve"], colors["trousers"]
    arms, legs = ISLANDS["sleeve"], ISLANDS["trousers"]
    for u in (0.18, 0.30):
        soft(canvas, [limb_px("sleeve", u - 0.06, 1.06), limb_px("sleeve", u + 0.06, 1.09)], 1.5, -9, arms)
    for u in (0.68, 0.82):
        soft(canvas, [limb_px("sleeve", u - 0.05, 1.10), limb_px("sleeve", u + 0.05, 1.07)], 1.4, -6, arms)
    stroke(canvas, [limb_px("sleeve", 0.0, cuff), limb_px("sleeve", 1.0, cuff)], shift(sleeve, -12), 1.0, arms)
    stroke(canvas, [limb_px("sleeve", 0.0, 1.415), limb_px("sleeve", 1.0, 1.415)], shift(sleeve, -10), 1.0, arms)
    if crease:
        stroke(canvas, [limb_px("trousers", 0.25, 0.16), limb_px("trousers", 0.25, 0.95)], shift(trousers, 8), 1.0, legs)
    stroke(canvas, [limb_px("trousers", 0.5, 0.14), limb_px("trousers", 0.5, 0.98)], shift(trousers, -7), 1.0, legs)
    for u in (0.16, 0.34):
        soft(canvas, [limb_px("trousers", u, 0.54), limb_px("trousers", u + 0.06, 0.57)], 1.4, -8, legs)
    soft(canvas, [limb_px("trousers", 0.1, 0.20), limb_px("trousers", 0.4, 0.21)], 1.6, -6, legs)


def _leather(canvas, colors, highlight=14, stitch=True):
    limit = ISLANDS["leather"]
    soft(canvas, [island_px("leather", 0.38, 0.22), island_px("leather", 0.62, 0.22)], 3.0, highlight, limit)
    if stitch:
        stroke(canvas, [island_px("leather", 0.0, 0.34), island_px("leather", 1.0, 0.34)],
               shift(colors["leather"], -12), 1.0, limit)
    for name in ("metal", "dark"):
        x0, y0, x1, y1 = ISLANDS[name]
        soft(canvas, [(x0 + 4.0, y0 + 4.0), (x0 + 7.0, y0 + 5.0)], 2.0, 26 if name == "metal" else 16, ISLANDS[name])


def _placket(canvas, colors, stripes=None):
    base = colors["shirt"]
    x0, y0, x1, y1 = ISLANDS["shirt"]
    if stripes:
        for x in range(x0 + 1, x1, 3):
            stroke(canvas, [(x + 0.5, y0), (x + 0.5, y1)], stripes, 1.0, ISLANDS["shirt"])
    centre = (x0 + x1) / 2
    stroke(canvas, [(centre, y0 + 1), (centre, y1 - 1)], shift(base, -14), 1.0, ISLANDS["shirt"])
    for y in range(y0 + 5, y1 - 2, 8):
        canvas.set(int(centre) + 1, y, rgba(shift(base, 18)))


def _details_base(canvas, colors, rng):
    for name in ("front", "back"):
        _rib_band(canvas, name, 0.855, 0.905)
        _rib_band(canvas, name, 1.44, 1.48, -6)
        for s in (-0.12, 0.13):
            _fold(canvas, name, [(s, 0.92), (s * 0.8, 1.12)], 2.0, -6)
    x0, y0, x1, y1 = ISLANDS["sleeve"]
    cuff_top = int(limb_px("sleeve", 0.0, 0.918)[1])
    for y in range(cuff_top, y1):
        for x in range(x0, x1):
            if (x - x0) % 2 == 0:
                canvas.set(x, y, rgba(shift(_get(canvas, x, y), -8)))
    _limb_details(canvas, colors, cuff=0.918)
    _leather(canvas, colors)


def _details_alexey(canvas, colors, rng):
    coat = colors["front"]
    dark, deep, light = shift(coat, -12), shift(coat, -20), shift(coat, 9)
    for s in (-0.17, -0.08, 0.09, 0.18):
        _fold(canvas, "front", [(s, 0.66), (s * 0.8, 0.96)], 2.0, -6)
    for side in (-1, 1):
        _patch(canvas, "front", [(side * 0.104, 1.052), (side * 0.180, 1.046), (side * 0.180, 1.020),
                                 (side * 0.104, 1.026)], shift(coat, -5), dark, light)
        _seam(canvas, "front", [(side * 0.014, 0.66), (side * 0.014, 1.14)], dark)
    _seam(canvas, "front", [(-0.145, 1.322), (-0.075, 1.328)], deep, 1.5)
    _seam(canvas, "front", [(-0.145, 1.330), (-0.075, 1.336)], light)
    for z in (1.100, 0.985, 0.870):
        _seam(canvas, "front", [(-0.034, z), (-0.020, z)], deep)
    for name in ("front", "back"):
        _seam(canvas, name, [(-0.33, 0.672), (0.33, 0.672)], dark)
    _seam(canvas, "back", [(0.0, 0.84), (0.0, 1.46)], dark)
    _seam(canvas, "back", [(0.0, 0.65), (0.0, 0.84)], deep, 1.6)
    _patch(canvas, "back", [(-0.10, 1.062), (0.10, 1.062), (0.10, 1.024), (-0.10, 1.024)], shift(coat, 3), dark)
    for side in (-1, 1):
        x, y = garment_px("back", side * 0.085, 1.043)
        canvas.set(int(x), int(y), rgba(shift(coat, -24)))
    _limb_details(canvas, colors)
    _placket(canvas, colors)
    _stripes(canvas, "accent", shift(colors["accent"], 14), 6)
    _leather(canvas, colors)


def _details_elena(canvas, colors, rng):
    coat = colors["front"]
    dark, light = shift(coat, -12), shift(coat, 9)
    for side in (-1, 1):
        _seam(canvas, "front", [(side * 0.10, 1.40), (side * 0.086, 1.22), (side * 0.075, 1.02),
                                (side * 0.10, 0.60)], dark)
        _seam(canvas, "front", [(side * 0.120, 0.930), (side * 0.170, 0.962)], shift(coat, -20), 1.5)
        _seam(canvas, "front", [(side * 0.120, 0.922), (side * 0.170, 0.954)], light)
        _seam(canvas, "back", [(side * 0.09, 1.40), (side * 0.07, 1.02), (side * 0.10, 0.60)], dark)
        for s in (0.04, 0.08):
            _fold(canvas, "back", [(side * s, 0.96), (side * s, 1.08)], 1.2, -6)
    for name in ("front", "back"):
        _seam(canvas, name, [(-0.33, 0.602), (0.33, 0.602)], dark)
    _seam(canvas, "back", [(0.0, 0.585), (0.0, 0.72)], shift(coat, -20), 1.6)
    _seam(canvas, "back", [(0.0, 0.72), (0.0, 1.46)], dark)
    arms = ISLANDS["sleeve"]
    for z in (0.876, 0.915):
        stroke(canvas, [limb_px("sleeve", 0.0, z), limb_px("sleeve", 1.0, z)], shift(colors["sleeve"], -12), 1.0, arms)
    _limb_details(canvas, colors, cuff=0.93, crease=False)
    x0, y0, x1, y1 = ISLANDS["accent"]
    for v in (0.2, 0.8):
        y = y1 - 1 - v * (y1 - y0 - 2)
        stroke(canvas, [(x0, y), (x1, y)], shift(colors["accent"], 12), 1.0, ISLANDS["accent"])
    _leather(canvas, colors, highlight=12)
    stroke(canvas, [island_px("leather", 0.72, 0.35), island_px("leather", 0.72, 1.0)],
           shift(colors["leather"], 16), 1.0, ISLANDS["leather"])


def _details_morozov(canvas, colors, rng):
    coat = colors["front"]
    stitch, shadow = shift(coat, -16), shift(coat, -28)
    _patch(canvas, "front", [(-0.142, 1.325), (-0.074, 1.325), (-0.074, 1.250), (-0.142, 1.250)],
           shift(coat, -3), stitch)
    _seam(canvas, "front", [(-0.142, 1.322), (-0.074, 1.322)], shadow, 1.5)
    for side in (-1, 1):
        s0, s1 = sorted((side * 0.090, side * 0.192))
        _patch(canvas, "front", [(s0, 1.040), (s1, 1.040), (s1, 0.930), (s0, 0.930)], shift(coat, -3), stitch)
        _seam(canvas, "front", [(s0, 1.037), (s1, 1.037)], shadow, 1.5)
        _seam(canvas, "front", [(side * 0.058, 0.64), (side * 0.058, 1.30)], stitch)
        _fold(canvas, "front", [(side * 0.17, 0.66), (side * 0.15, 0.90)], 2.0, -8)
    stain = ISLANDS["front"]
    x, y = garment_px("front", 0.12, 0.80)
    for py in range(int(y) - 4, int(y) + 5):
        for px in range(int(x) - 5, int(x) + 6):
            k = gauss((px - x) / 4.0, (py - y) / 3.0) * 0.18
            if stain[0] <= px < stain[2] and stain[1] <= py < stain[3] and k > 0.02:
                canvas.set(px, py, rgba(mix(_get(canvas, px, py), (150, 138, 104), k)))
    for name in ("front", "back"):
        _seam(canvas, name, [(-0.33, 0.642), (0.33, 0.642)], stitch)
    _seam(canvas, "back", [(0.0, 0.625), (0.0, 0.78)], shadow, 1.6)
    _seam(canvas, "back", [(0.0, 0.78), (0.0, 1.46)], stitch)
    _patch(canvas, "back", [(-0.09, 1.07), (0.09, 1.07), (0.09, 1.035), (-0.09, 1.035)], shift(coat, 4), stitch)
    _limb_details(canvas, colors)
    _placket(canvas, colors, stripes=shift(colors["shirt"], -10))
    x0, y0, x1, y1 = ISLANDS["accent"]
    for y in range(y0 + 2, y1, 5):
        for x in range(x0 + 2 + (y // 5) % 2 * 2, x1, 5):
            canvas.set(x, y, rgba(shift(colors["accent"], 26)))
    _leather(canvas, colors, highlight=20, stitch=False)


def _details_officer(canvas, colors, rng):
    cloth = colors["front"]
    dark, deep, light = shift(cloth, -12), shift(cloth, -20), shift(cloth, 10)
    _seam(canvas, "front", [(0.0, 0.84), (0.0, 1.32)], deep, 1.5)
    for side in (-1, 1):
        s0, s1 = sorted((side * 0.064, side * 0.136))
        _patch(canvas, "front", [(s0, 1.325), (s1, 1.325), (s1, 1.212), (s0, 1.212)], shift(cloth, 2), dark)
        _seam(canvas, "front", [(side * 0.100, 1.21), (side * 0.100, 1.30)], deep)
        s0, s1 = sorted((side * 0.080, side * 0.170))
        _patch(canvas, "front", [(s0, 0.935), (s1, 0.935), (s1, 0.905), (s0, 0.905)], shift(cloth, -4), dark, light)
        _fold(canvas, "front", [(side * 0.14, 1.02), (side * 0.12, 1.16)], 1.8, -6)
    for name in ("front", "back"):
        _seam(canvas, name, [(-0.33, 0.846), (0.33, 0.846)], dark)
    _seam(canvas, "back", [(0.0, 0.84), (0.0, 1.46)], dark)
    _seam(canvas, "back", [(-0.2, 1.36), (0.2, 1.36)], dark)
    arms = ISLANDS["sleeve"]
    cx, cy = limb_px("sleeve", 0.5, 1.30)
    shield = [(cx - 4.0, cy - 4.0), (cx + 4.0, cy - 4.0), (cx + 4.0, cy + 1.5), (cx, cy + 5.0), (cx - 4.0, cy + 1.5)]
    fill(canvas, shield, colors["trim"])
    stroke(canvas, shield + shield[:1], colors["metal"], 1.0, arms)
    _limb_details(canvas, colors, crease=False)
    stroke(canvas, [limb_px("trousers", 0.5, 0.28), limb_px("trousers", 0.5, 0.98)],
           shift(colors["trousers"], 16), 1.0, ISLANDS["trousers"])
    x0, y0, x1, y1 = ISLANDS["cap"]
    stroke(canvas, [(x0, y0 + 3.5), (x1, y0 + 3.5)], shift(colors["cap"], 18), 1.0, ISLANDS["cap"])
    x0, y0, x1, y1 = ISLANDS["accent"]
    for y in range(y0 + 3, y1, 6):
        stroke(canvas, [(x0, y + 0.5), (x1, y + 0.5)], shift(colors["accent"], 7), 1.0, ISLANDS["accent"])
    _leather(canvas, colors, highlight=22, stitch=False)


ATLAS_LOOKS = {
    "CharacterBase": {
        "colors": {"front": (70, 76, 70), "back": (68, 74, 68), "sleeve": (68, 74, 68), "trousers": (56, 55, 52),
                   "hair": (78, 62, 48), "shirt": (136, 134, 124), "accent": (62, 68, 62), "cap": (64, 70, 64),
                   "leather": (60, 48, 40), "lining": (48, 50, 47), "metal": (132, 126, 108),
                   "dark": (34, 34, 33), "trim": (82, 64, 48), "sole": (30, 28, 26), "knit": (62, 68, 62)},
        "patterns": {"front": "knit", "back": "knit", "sleeve": "knit", "knit": "rib", "trousers": "twill"},
        "hair_contrast": 0.9, "details": _details_base,
    },
    "AlexeyVoron": {
        "colors": {"front": (66, 64, 58), "back": (63, 62, 57), "sleeve": (62, 61, 56), "trousers": (44, 46, 45),
                   "hair": (42, 36, 31), "shirt": (148, 148, 136), "accent": (92, 44, 40), "cap": (60, 60, 56),
                   "leather": (58, 42, 32), "lining": (40, 38, 35), "metal": (118, 108, 88),
                   "dark": (30, 29, 28), "trim": (88, 80, 62), "sole": (26, 26, 24), "knit": (60, 60, 56)},
        "patterns": {"front": "twill", "back": "twill", "sleeve": "twill", "trousers": "twill", "shirt": "weave"},
        "hair_contrast": 1.0, "details": _details_alexey,
    },
    "ElenaVoron": {
        "colors": {"front": (86, 98, 106), "back": (83, 95, 103), "sleeve": (83, 95, 103), "trousers": (48, 48, 52),
                   "hair": (98, 58, 42), "shirt": (156, 146, 130), "accent": (72, 82, 90), "cap": (80, 92, 100),
                   "leather": (62, 44, 36), "lining": (60, 52, 50), "metal": (146, 132, 100),
                   "dark": (34, 32, 32), "trim": (120, 90, 60), "sole": (30, 27, 25), "knit": (150, 116, 60)},
        "patterns": {"front": "twill", "back": "twill", "sleeve": "twill", "trousers": "twill",
                     "knit": "knit", "shirt": "rib"},
        "hair_contrast": 1.0, "details": _details_elena,
    },
    "DrIlyaMorozov": {
        "colors": {"front": (142, 142, 133), "back": (139, 139, 130), "sleeve": (139, 139, 130),
                   "trousers": (60, 56, 50), "hair": (146, 144, 138), "shirt": (140, 148, 154),
                   "accent": (66, 54, 72), "cap": (160, 160, 150), "leather": (32, 30, 29),
                   "lining": (132, 132, 124), "metal": (112, 112, 114), "dark": (36, 36, 38),
                   "trim": (48, 64, 112), "sole": (24, 24, 23), "knit": (86, 82, 60)},
        "patterns": {"front": "weave", "back": "weave", "sleeve": "weave", "trousers": "twill", "knit": "knit"},
        "hair_contrast": 0.8, "details": _details_morozov,
    },
    "PoliceOfficer": {
        "colors": {"front": (48, 58, 66), "back": (46, 56, 64), "sleeve": (46, 56, 64), "trousers": (42, 50, 58),
                   "hair": (38, 33, 29), "shirt": (124, 132, 136), "accent": (30, 33, 38), "cap": (48, 58, 66),
                   "leather": (30, 28, 27), "lining": (38, 44, 50), "metal": (152, 134, 80),
                   "dark": (22, 22, 24), "trim": (104, 96, 70), "sole": (22, 22, 21), "knit": (48, 58, 66)},
        "patterns": {"front": "twill", "back": "twill", "sleeve": "twill", "trousers": "twill", "cap": "twill"},
        "hair_contrast": 0.8, "details": _details_officer,
    },
}


def paint_atlas(path, character_id):
    look = ATLAS_LOOKS[character_id]
    face = FACE_LOOKS[character_id]
    rng = random.Random(f"{character_id}:atlas")
    colors = dict(look["colors"])
    # Head, ears, neck and hands continue the face map's side tone.
    colors["skin"] = mix(face["skin"], face["shade"], 0.22)
    canvas = Canvas(ATLAS_SIZE, ATLAS_SIZE, rgba(colors["lining"]))
    for name, base in colors.items():
        _fill_island(canvas, name, base, rng, look["patterns"].get(name))
    _paint_hair(canvas, colors["hair"], rng, look["hair_contrast"])
    look["details"](canvas, colors, rng)
    canvas.save_png(path)
