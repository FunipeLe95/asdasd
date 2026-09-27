"""Small dependency-free raster toolkit for the original character textures."""

from __future__ import annotations

import json
import struct
import zlib
from pathlib import Path


class Canvas:
    def __init__(self, width: int, height: int, color=(0, 0, 0, 0)):
        self.width = width
        self.height = height
        self.pixels = bytearray(color * (width * height))

    def set(self, x: int, y: int, color):
        if 0 <= x < self.width and 0 <= y < self.height:
            i = (y * self.width + x) * 4
            self.pixels[i:i + 4] = bytes(color)

    def rect(self, x0: int, y0: int, x1: int, y1: int, color):
        raw_x0, raw_x1 = sorted((x0, x1))
        raw_y0, raw_y1 = sorted((y0, y1))
        x0, x1 = max(0, raw_x0), min(self.width, raw_x1)
        y0, y1 = max(0, raw_y0), min(self.height, raw_y1)
        if x0 >= x1 or y0 >= y1:
            return
        row = bytes(color) * (x1 - x0)
        for y in range(y0, y1):
            i = (y * self.width + x0) * 4
            self.pixels[i:i + len(row)] = row

    def line(self, x0: int, y0: int, x1: int, y1: int, color, thickness=1):
        dx, dy = abs(x1 - x0), abs(y1 - y0)
        sx = 1 if x0 < x1 else -1
        sy = 1 if y0 < y1 else -1
        error = dx - dy
        radius = max(0, thickness - 1) // 2
        while True:
            self.rect(x0 - radius, y0 - radius, x0 + radius + 1, y0 + radius + 1, color)
            if x0 == x1 and y0 == y1:
                break
            twice = error * 2
            if twice > -dy:
                error -= dy
                x0 += sx
            if twice < dx:
                error += dx
                y0 += sy

    def polygon(self, points, color, outline=None):
        if len(points) < 3:
            return
        min_y = max(0, min(y for _, y in points))
        max_y = min(self.height - 1, max(y for _, y in points))
        for y in range(min_y, max_y + 1):
            scan_y = y + 0.5
            intersections = []
            for i, (x0, y0) in enumerate(points):
                x1, y1 = points[(i + 1) % len(points)]
                if (y0 <= scan_y < y1) or (y1 <= scan_y < y0):
                    intersections.append(x0 + (scan_y - y0) * (x1 - x0) / (y1 - y0))
            intersections.sort()
            for i in range(0, len(intersections) - 1, 2):
                self.rect(int(intersections[i]), y, int(intersections[i + 1]) + 1, y + 1, color)
        if outline:
            for i, (x0, y0) in enumerate(points):
                x1, y1 = points[(i + 1) % len(points)]
                self.line(x0, y0, x1, y1, outline)

    def ellipse(self, cx: int, cy: int, rx: int, ry: int, color, outline=None):
        for y in range(max(0, cy - ry), min(self.height, cy + ry + 1)):
            for x in range(max(0, cx - rx), min(self.width, cx + rx + 1)):
                nx = (x - cx) / max(rx, 1)
                ny = (y - cy) / max(ry, 1)
                d = nx * nx + ny * ny
                if d <= 1.0:
                    if outline is None or d > 0.69:
                        self.set(x, y, color)

    @staticmethod
    def _chunk(kind: bytes, payload: bytes) -> bytes:
        crc = zlib.crc32(kind + payload) & 0xFFFFFFFF
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", crc)

    def save_png(self, path):
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        stride = self.width * 4
        raw = b"".join(b"\x00" + self.pixels[y * stride:(y + 1) * stride]
                       for y in range(self.height))
        header = struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0)
        png = (b"\x89PNG\r\n\x1a\n" + self._chunk(b"IHDR", header)
               + self._chunk(b"IDAT", zlib.compress(raw, 9))
               + self._chunk(b"IEND", b""))
        path.write_bytes(png)
        return path


def _icon_badge():
    c = Canvas(32, 32)
    brass, dark = (132, 111, 70, 255), (36, 40, 37, 255)
    c.polygon([(10, 3), (22, 3), (25, 8), (24, 20), (16, 29), (8, 20), (7, 8)], brass, dark)
    c.polygon([(16, 8), (18, 13), (23, 13), (19, 16), (21, 21),
               (16, 18), (11, 21), (13, 16), (9, 13), (14, 13)], dark)
    c.rect(11, 5, 21, 7, (183, 157, 101, 255))
    return c


def _icon_photo():
    c = Canvas(32, 32)
    c.rect(4, 3, 28, 29, (31, 34, 33, 255))
    c.rect(7, 6, 25, 24, (125, 128, 114, 255))
    c.rect(8, 7, 24, 22, (83, 96, 88, 255))
    c.ellipse(12, 14, 3, 4, (155, 137, 113, 255))
    c.ellipse(20, 14, 3, 4, (151, 132, 108, 255))
    c.rect(10, 18, 15, 22, (45, 51, 48, 255))
    c.rect(17, 18, 22, 22, (51, 55, 51, 255))
    c.line(17, 9, 22, 20, (25, 24, 23, 255), 2)
    c.line(8, 26, 24, 26, (165, 160, 142, 255))
    return c


def _icon_key():
    c = Canvas(32, 32)
    metal, shadow = (155, 126, 76, 255), (45, 39, 29, 255)
    c.ellipse(9, 10, 6, 6, shadow)
    c.ellipse(9, 10, 3, 3, metal)
    c.line(13, 14, 25, 26, metal, 3)
    c.line(22, 23, 26, 19, metal, 2)
    c.line(24, 25, 28, 21, metal, 2)
    return c


def _icon_file():
    c = Canvas(32, 32)
    paper, ink = (160, 157, 137, 255), (46, 49, 46, 255)
    c.polygon([(8, 3), (20, 3), (25, 8), (25, 29), (7, 29), (7, 4)], paper, ink)
    c.line(19, 4, 19, 9, ink)
    c.line(19, 9, 24, 9, ink)
    c.line(11, 13, 21, 13, (85, 88, 80, 255))
    c.line(11, 17, 21, 17, (85, 88, 80, 255))
    c.line(11, 21, 18, 21, (85, 88, 80, 255))
    c.line(12, 25, 21, 25, (126, 69, 54, 255))
    return c


_FONT = {
    " ": ("00000",) * 7,
    "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
    "B": ("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
    "C": ("01111", "10000", "10000", "10000", "10000", "10000", "01111"),
    "D": ("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
    "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
    "F": ("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
    "G": ("01111", "10000", "10000", "10111", "10001", "10001", "01111"),
    "H": ("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
    "I": ("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
    "J": ("00111", "00010", "00010", "00010", "10010", "10010", "01100"),
    "K": ("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
    "L": ("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
    "M": ("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
    "N": ("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
    "O": ("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
    "P": ("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
    "Q": ("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
    "R": ("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
    "S": ("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
    "T": ("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
    "U": ("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
    "V": ("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
    "W": ("10001", "10001", "10001", "10101", "10101", "10101", "01010"),
    "X": ("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
    "Y": ("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
    "Z": ("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
    "0": ("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
    "2": ("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
    "3": ("11110", "00001", "00001", "01110", "00001", "00001", "11110"),
    "4": ("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
    "5": ("11111", "10000", "10000", "11110", "00001", "00001", "11110"),
    "6": ("01110", "10000", "10000", "11110", "10001", "10001", "01110"),
    "7": ("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
    "8": ("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
    "9": ("01110", "10001", "10001", "01111", "00001", "00001", "01110"),
    ".": ("00000", "00000", "00000", "00000", "00000", "00110", "00110"),
    ",": ("00000", "00000", "00000", "00000", "00110", "00110", "00100"),
    "!": ("00100", "00100", "00100", "00100", "00100", "00000", "00100"),
    "?": ("01110", "10001", "00001", "00010", "00100", "00000", "00100"),
    ":": ("00000", "00110", "00110", "00000", "00110", "00110", "00000"),
    "-": ("00000", "00000", "00000", "11111", "00000", "00000", "00000"),
    "+": ("00000", "00100", "00100", "11111", "00100", "00100", "00000"),
    "'": ("00100", "00100", "00010", "00000", "00000", "00000", "00000"),
    "/": ("00001", "00010", "00010", "00100", "01000", "01000", "10000"),
}


def bitmap_font(path, descriptor_path):
    glyphs = list(_FONT)
    columns, cell = 16, 8
    rows = (len(glyphs) + columns - 1) // columns
    canvas = Canvas(columns * cell, rows * cell)
    entries = {}
    for index, char in enumerate(glyphs):
        col, row = index % columns, index // columns
        ox, oy = col * cell + 1, row * cell
        for y, line in enumerate(_FONT[char]):
            for x, bit in enumerate(line):
                if bit == "1":
                    canvas.set(ox + x, oy + y, (194, 190, 169, 255))
        entries[char] = {"x": ox, "y": oy, "width": 5 if char != " " else 3,
                         "height": 7, "advance": 6}
    atlas = canvas.save_png(path)
    descriptor = {
        "format": "voron-bitmap-font-1",
        "atlas": atlas.name,
        "width": canvas.width,
        "height": canvas.height,
        "cell": [cell, cell],
        "glyphs": entries,
        "fallback": "?",
        "note": "Original uppercase 5x7 bitmap glyphs; transparent atlas, point filtering recommended.",
    }
    descriptor_path = Path(descriptor_path)
    descriptor_path.parent.mkdir(parents=True, exist_ok=True)
    descriptor_path.write_text(json.dumps(descriptor, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
    return atlas, descriptor_path


def generate_pixel_art_assets(art_root):
    art_root = Path(art_root)
    icons = art_root / "UI" / "Icons"
    icon_files = {
        "PoliceBadge_32.png": _icon_badge(),
        "OldPhotograph_32.png": _icon_photo(),
        "ApartmentKey_32.png": _icon_key(),
        "MedicalFile_32.png": _icon_file(),
    }
    icon_paths = []
    for name, canvas in icon_files.items():
        icon_paths.append(str(canvas.save_png(icons / name)))

    font_path = art_root / "UI" / "Font" / "PS1Bitmap5x7.png"
    font_descriptor = font_path.with_suffix(".json")
    bitmap_font(font_path, font_descriptor)
    return {
        "icons": icon_paths,
        "font_atlas": str(font_path),
        "font_descriptor": str(font_descriptor),
    }
