"""
Report per-channel statistics for texture PNGs.

The script helped find the CS2 MaskMap channel packing by experiment. It compared our generated maps
with the official ExampleAssets. The real-world material of each example is known: a wooden fence is
not metal, and a tree is not metal. The channel that sits near zero on a known dielectric is the
metallic channel.

    blender --background --factory-startup --python inspect_texture.py -- --img a.png b.png
"""

import bpy
import sys
import os
import argparse

import numpy as np


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--img", nargs="+", required=True)
    return ap.parse_args(argv)


def stats(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    px = buf.reshape(-1, 4)

    print(f"\n=== {os.path.basename(path)}  ({w}x{h}) ===")
    for i, ch in enumerate("RGBA"):
        c = px[:, i]
        print(f"  {ch}: min {c.min():.3f}  mean {c.mean():.3f}  "
              f"median {np.median(c):.3f}  max {c.max():.3f}  std {c.std():.3f}")
    bpy.data.images.remove(img)


def main():
    args = parse_args()
    for p in args.img:
        if os.path.exists(p):
            stats(p)
        else:
            print(f"\n=== MISSING: {p} ===")


if __name__ == "__main__":
    main()
