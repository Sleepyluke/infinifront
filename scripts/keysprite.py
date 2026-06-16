#!/usr/bin/env python3
"""Chroma-key a magenta (#FF00FF) background to transparency, autocrop, downscale.

For the game's Gemini sprite pipeline (see docs/art/*.md). The key is HUE-based, so it
removes both the bright magenta field AND its darkened drop-shadow, while leaving red
faction accents (#FF0000), cyan crystals, and yellow/gray plating untouched.

Usage:
  python scripts/keysprite.py in.png --out godot/assets/buildings/depot.png --size 256
"""
import argparse, os, sys
from PIL import Image

try:
    import numpy as np
except ImportError:
    np = None

LO, HI, RB_TOL, DESPILL = 10, 28, 70, 40  # aggressive cut: kill magenta + its soft drop-shadow penumbra


def key(im):
    im = im.convert("RGBA")
    if np is None:
        return _key_slow(im)
    a = np.asarray(im).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = np.minimum(r - g, b - g)                      # high for magenta incl. dark shadow
    magenta = (m > LO) & (np.abs(r - b) < RB_TOL)
    feather = np.clip(255 * (HI - m) / (HI - LO), 0, 255)
    alpha = np.where(m >= HI, 0, feather)
    out = a.copy()
    tint = magenta & (m < HI)                         # partly-magenta edge → despill the fringe
    out[..., 0] = np.where(tint, np.minimum(r, g + DESPILL), r)
    out[..., 2] = np.where(tint, np.minimum(b, g + DESPILL), b)
    out[..., 3] = np.where(magenta, alpha, 255)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def _key_slow(im):
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, _ = px[x, y]
            m = min(r - g, b - g)
            if m > LO and abs(r - b) < RB_TOL:
                al = 0 if m >= HI else int(max(0, min(255, 255 * (HI - m) / (HI - LO))))
                px[x, y] = (min(r, g + DESPILL), g, min(b, g + DESPILL), al)
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("inp")
    ap.add_argument("--out", required=True)
    ap.add_argument("--size", type=int, default=256, help="max width/height of the output (aspect kept)")
    ap.add_argument("--pad", type=int, default=2, help="transparent padding px after autocrop")
    a = ap.parse_args()

    im = key(Image.open(a.inp))
    bbox = im.getchannel("A").getbbox()
    if bbox:
        im = im.crop(bbox)
    if a.pad:
        from PIL import ImageOps
        im = ImageOps.expand(im, border=a.pad, fill=(0, 0, 0, 0))
    im.thumbnail((a.size, a.size), Image.LANCZOS)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    im.save(a.out)
    print(f"WROTE {a.out} {im.size}")


if __name__ == "__main__":
    main()
