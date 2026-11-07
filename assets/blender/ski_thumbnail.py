"""
ski_thumbnail.py generates the Paradox Mods thumbnail (Properties/Thumbnail.png): the hub's launcher
glyph, white on the icon family's sky.

Square, full bleed, deterministic, no bpy. It runs on Blender's bundled python.exe:

    "<blender>\\<ver>\\python\\bin\\python.exe" ski_thumbnail.py --out <file.png> [--size 1024]

Glyph: ski_launcher_glyph.py candidate (c), the one the hub ships as SkiLauncherGlyph.svg. Sky:
ski_icons.py SKY_TOP / SKY_BOT.
"""

import argparse
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ski_launcher_glyph as G  # noqa: E402

SKY_TOP, SKY_BOT = (44, 100, 172), (128, 186, 234)   # ski_icons.py
GLYPH_SHARE = 0.76                                   # of the side. The listing crops corners on some pages


def render(size):
    t = ((np.arange(size) + 0.5) / size)[:, None, None]
    img = np.asarray(SKY_TOP, float) * (1 - t) + np.asarray(SKY_BOT, float) * t
    img = np.broadcast_to(img, (size, size, 3)).copy()
    gs = int(round(size * GLYPH_SHARE))
    alpha = G.render_alpha(G.cand_c(), gs)
    alpha = np.asarray(alpha, float)
    if alpha.max() > 1.0:
        alpha = alpha / 255.0
    o = (size - gs) // 2
    a = alpha[..., None]
    img[o:o + gs, o:o + gs] = img[o:o + gs, o:o + gs] * (1 - a) + 255.0 * a
    return np.clip(np.rint(img), 0, 255).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", required=True)
    ap.add_argument("--size", type=int, default=1024)
    args = ap.parse_args()
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    G.write_png(args.out, render(args.size))
    print(f"PASS ski_thumbnail: {args.out} {args.size} x {args.size}")


if __name__ == "__main__":
    main()
