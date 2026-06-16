#!/usr/bin/env python3
"""Assemble one unit's 4 animation strips (4 frames each) into the 384x640 SheetAnimator sheet.

Each strip is a known 4-frame layout: a single horizontal row (wide) or a 2x2 grid (square),
chosen by aspect ratio. Per cell we chroma-key and keep the LARGEST connected blob (the unit),
which ignores thin divider lines and specks. The 4 frames are padded into the contract rows
(idle 4 / walk 6 / attack 6 / death 6, single facing duplicated across S/W/N), so the existing
SheetAnimator + UnitView animate it with no code change.

Usage: python scripts/_anim_assemble.py <defId>
"""
import sys, io, json, base64
from collections import deque
import numpy as np
from PIL import Image

LO, HI, RB_TOL, DESPILL = 10, 28, 70, 40
CELL = 64
# anim -> (sheet rows, frame-index pattern mapping the 4 detected frames into the row)
PLAN = [("idle", [0, 1, 2], [0, 1, 2, 3]),
        ("walk", [3, 4, 5], [0, 1, 2, 3, 2, 1]),
        ("attack", [6, 7, 8], [0, 1, 2, 3, 3, 3]),
        ("death", [9], [0, 1, 2, 3, 3, 3])]


def key(im):
    a = np.asarray(im.convert("RGBA")).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = np.minimum(r - g, b - g)
    magenta = (m > LO) & (np.abs(r - b) < RB_TOL)
    feather = np.clip(255 * (HI - m) / (HI - LO), 0, 255)
    alpha = np.where(m >= HI, 0, feather)
    out = a.copy()
    tint = magenta & (m < HI)
    out[..., 0] = np.where(tint, np.minimum(r, g + DESPILL), r)
    out[..., 2] = np.where(tint, np.minimum(b, g + DESPILL), b)
    out[..., 3] = np.where(magenta, alpha, 255)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def largest_blob_bbox(cell):
    """bbox of the largest connected alpha blob in a keyed cell (ignores thin lines/specks)."""
    a = np.asarray(cell)
    W, H = cell.size
    sc = max(1, max(W, H) // 220)
    sm = Image.fromarray((a[..., 3] > 50).astype(np.uint8) * 255)
    if sc > 1:
        sm = sm.resize((W // sc, H // sc), Image.NEAREST)
    m = np.asarray(sm) > 80
    h, w = m.shape
    lab = np.zeros((h, w), np.int32); n = 0; best = (0, None)
    for sy in range(h):
        for sx in range(w):
            if m[sy, sx] and lab[sy, sx] == 0:
                n += 1; q = deque([(sy, sx)]); lab[sy, sx] = n
                minx = maxx = sx; miny = maxy = sy; area = 0
                while q:
                    y, x = q.popleft(); area += 1
                    minx = min(minx, x); maxx = max(maxx, x); miny = min(miny, y); maxy = max(maxy, y)
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = y + dy, x + dx
                        if 0 <= ny < h and 0 <= nx < w and m[ny, nx] and lab[ny, nx] == 0:
                            lab[ny, nx] = n; q.append((ny, nx))
                if area > best[0]:
                    best = (area, (minx * sc, miny * sc, (maxx + 1) * sc, (maxy + 1) * sc))
    return best[1]


def four_frames(strip_path):
    keyed = key(Image.open(strip_path))
    W, H = keyed.size
    ar = W / H
    cells = [(0, 0, W // 4, H), (W // 4, 0, W // 2, H), (W // 2, 0, 3 * W // 4, H), (3 * W // 4, 0, W, H)] if ar >= 2.0 \
        else [(0, 0, W // 2, H // 2), (W // 2, 0, W, H // 2), (0, H // 2, W // 2, H), (W // 2, H // 2, W, H)]
    frames = []
    for box in cells:
        c = keyed.crop(box)
        bb = largest_blob_bbox(c) or c.getchannel("A").getbbox()
        if bb:
            c = c.crop(bb)
        c.thumbnail((CELL, CELL), Image.LANCZOS)
        cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        cell.paste(c, ((CELL - c.width) // 2, (CELL - c.height) // 2), c)
        frames.append(cell)
    return frames, ("1x4" if ar >= 2.0 else "2x2")


def main():
    d = sys.argv[1]
    sheet = Image.new("RGBA", (6 * CELL, 10 * CELL), (0, 0, 0, 0))
    preview = {}
    for anim, rows, pattern in PLAN:
        frames, layout = four_frames(f"assets/raw-sprites/_anim_{d}_{anim}.png")
        seq = [frames[i] for i in pattern]
        print(f"{anim}: {layout}, {len(seq)} frames placed")
        for row in rows:
            for f, img in enumerate(seq):
                sheet.paste(img, (f * CELL, row * CELL), img)
        preview[anim] = []
        for img in seq:
            buf = io.BytesIO(); img.resize((44, 44), Image.LANCZOS).save(buf, "PNG", optimize=True)
            preview[anim].append("data:image/png;base64," + base64.b64encode(buf.getvalue()).decode())
    out = f"assets/raw-sprites/_sheet_{d}.png"
    sheet.save(out); print(f"WROTE {out} {sheet.size}")
    with open("assets/raw-sprites/_anim_preview.html", "w", encoding="utf-8") as f:
        f.write(WIDGET.replace("/*DATA*/", json.dumps(preview)).replace("UNIT", d))


WIDGET = """<h2 class="sr-only">Live preview of the assembled UNIT animation sheet — idle, walk, attack and death frames playing as loops, with an animation picker and speed control.</h2>
<div style="padding:0.5rem 0;">
  <div style="display:flex; align-items:center; gap:10px; flex-wrap:wrap; margin-bottom:12px;">
    <span id="tabs"></span><span style="flex:1"></span>
    <label style="font-size:13px; color:var(--color-text-secondary);">Speed</label>
    <input type="range" id="fps" min="2" max="16" value="9" step="1" style="width:120px;" />
    <span id="fpsv" style="font-size:13px; font-weight:500; min-width:46px;">9 fps</span>
  </div>
  <div style="display:flex; align-items:flex-end; justify-content:center; gap:30px; height:210px; background:var(--color-background-secondary); border:0.5px solid var(--color-border-tertiary); border-radius:var(--border-radius-lg);">
    <div style="text-align:center;"><img id="big" style="height:150px; image-rendering:pixelated;" /><div style="font-size:11px; color:var(--color-text-tertiary); margin-top:4px;">zoomed</div></div>
    <div style="text-align:center;"><img id="real" style="height:56px; image-rendering:pixelated;" /><div style="font-size:11px; color:var(--color-text-tertiary); margin-top:4px;">in-game size</div></div>
  </div>
  <div id="strip" style="display:flex; gap:8px; justify-content:center; margin-top:12px; flex-wrap:wrap;"></div>
</div>
<script>
(function(){
  const D=/*DATA*/; const anims=Object.keys(D); let cur=anims[0], i=0;
  const big=document.getElementById('big'), real=document.getElementById('real'), fps=document.getElementById('fps'), fpsv=document.getElementById('fpsv'), strip=document.getElementById('strip'), tabs=document.getElementById('tabs');
  anims.forEach(a=>{ const b=document.createElement('button'); b.textContent=a; b.style.marginRight='6px'; b.onclick=()=>{cur=a;i=0;render();}; tabs.appendChild(b); });
  function render(){ strip.innerHTML=''; D[cur].forEach(u=>{ const im=new Image(); im.src=u; im.style.cssText='height:80px;border:0.5px solid var(--color-border-tertiary);border-radius:6px;background:var(--color-background-primary);image-rendering:pixelated;'; strip.appendChild(im); }); }
  function tick(){ const F=D[cur]; big.src=F[i]; real.src=F[i]; i=(i+1)%F.length; setTimeout(tick,1000/parseInt(fps.value)); }
  fps.oninput=()=>fpsv.textContent=fps.value+' fps';
  render(); tick();
})();
</script>"""

if __name__ == "__main__":
    main()
