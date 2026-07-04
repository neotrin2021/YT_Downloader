"""Pack the per-frame light-group EXRs into Unity six-way flipbook sheets.

Inputs  (out/exr):  px_/py_/pz_/nx_/ny_/nz_/alpha_ NNNN.exr, 512x512, 64 frames
Outputs (out/tex):  FogSixWay_Positive_8x8.png  RGB = +X,+Y,+Z lit, A = density
                    FogSixWay_Negative_8x8.png  RGB = -X,-Y,-Z lit, A = density
                    FogPreview.gif              front-lit turntable preview
Runs inside the bpy venv (bpy loads EXR; numpy + PIL do the packing).
"""
import bpy
import numpy as np
import os
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
EXR = os.path.join(BASE, "out", "exr")
TEX = os.path.join(BASE, "out", "tex")
os.makedirs(TEX, exist_ok=True)

FRAMES, SRC, CELL, GRID = 64, 512, 256, 8

def load_gray(path):
    """Load one EXR, return HxW float array (channel 0), flipped to top-down."""
    img = bpy.data.images.load(path)
    w, h = img.size
    ch = img.channels
    buf = np.empty(w * h * ch, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return buf.reshape(h, w, ch)[::-1, :, 0]   # blender rows are bottom-up

def box_half(a):
    """2x downscale by box filter."""
    return a.reshape(a.shape[0] // 2, 2, a.shape[1] // 2, 2).mean(axis=(1, 3))

def build_sheet(channel_names):
    sheet = np.zeros((GRID * CELL, GRID * CELL, 4), dtype=np.float32)
    for f in range(FRAMES):
        cells = []
        for name in channel_names + ["alpha"]:
            p = os.path.join(EXR, f"{name}_{f+1:04d}.exr")
            cells.append(box_half(load_gray(p)))
        px = np.stack(cells, axis=-1)                     # 256x256x4
        r, c = divmod(f, GRID)
        sheet[r*CELL:(r+1)*CELL, c*CELL:(c+1)*CELL] = px
        if f % 16 == 0:
            print(f"  frame {f+1}/{FRAMES}"); import sys; sys.stdout.flush()
    return sheet

def save_png(sheet, path):
    # tiny dither hides 8-bit banding in the soft gradients
    noise = (np.random.default_rng(7).random(sheet.shape, dtype=np.float32) - 0.5) / 255.0
    q = np.clip((sheet + noise) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(q, "RGBA").save(path, optimize=True)
    print("wrote", path, os.path.getsize(path) // 1024, "KB")

print("packing positive sheet (+X +Y +Z / alpha)")
pos = build_sheet(["px", "py", "pz"])
print("packing negative sheet (-X -Y -Z / alpha)")
neg = build_sheet(["nx", "ny", "nz"])

# Normalize all six light channels by ONE shared factor: dense volumes
# transmit little light, so raw values hug zero. A single factor keeps the
# directional ratios (the whole point of six-way) while using the full
# 8-bit range; re-scale with the light intensity in Unity.
peak = max(pos[..., :3].max(), neg[..., :3].max())
k = 0.95 / peak if peak > 0 else 1.0
pos[..., :3] *= k
neg[..., :3] *= k
print(f"normalized light channels: peak {peak:.4f} -> 0.95 (x{k:.2f})")

save_png(pos, os.path.join(TEX, "FogSixWay_Positive_8x8.png"))
save_png(neg, os.path.join(TEX, "FogSixWay_Negative_8x8.png"))

print("building preview gif")
frames = []
for f in range(FRAMES):
    r, c = divmod(f, GRID)
    cp = pos[r*CELL:(r+1)*CELL, c*CELL:(c+1)*CELL]
    cn = neg[r*CELL:(r+1)*CELL, c*CELL:(c+1)*CELL]
    # plausible studio lighting: mostly top, some right, a little camera fill
    lit = 0.55 * cp[..., 1] + 0.25 * cp[..., 0] + 0.20 * cn[..., 2]
    a = cp[..., 3]
    bg = 0.08
    comp = lit * a + bg * (1 - a)
    frames.append(Image.fromarray(
        np.clip(comp * 255, 0, 255).astype(np.uint8), "L").convert("P"))
frames[0].save(os.path.join(TEX, "FogPreview.gif"), save_all=True,
               append_images=frames[1:], duration=66, loop=0)
print("wrote FogPreview.gif")
