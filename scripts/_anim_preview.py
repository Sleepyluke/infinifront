#!/usr/bin/env python3
"""Throwaway: slice an N-frame animation strip, chroma-key magenta, emit a self-contained
HTML widget fragment that cycles the frames so the animation can be reviewed without Godot."""
import base64, io, sys, json
from PIL import Image
import numpy as np

LO, HI, RB_TOL, DESPILL = 10, 28, 70, 40  # match scripts/keysprite.py


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


def main():
    strip_path, n, out_html = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    im = Image.open(strip_path).convert("RGBA")
    W, H = im.size
    cw = W // n
    mx = int(cw * 0.04)  # trim cell-divider margins equally (preserves registration)
    my = int(H * 0.04)
    uris = []
    for i in range(n):
        cell = im.crop((i * cw + mx, my, (i + 1) * cw - mx, H - my))
        keyed = key(cell)
        keyed.thumbnail((140, 140), Image.LANCZOS)
        buf = io.BytesIO()
        keyed.save(buf, "PNG")
        uris.append("data:image/png;base64," + base64.b64encode(buf.getvalue()).decode())
    with open(out_html, "w", encoding="utf-8") as f:
        f.write(WIDGET.replace("/*FRAMES*/", json.dumps(uris)))
    print(f"WROTE {out_html} ({n} frames, ~{sum(len(u) for u in uris)//1024}KB)")


WIDGET = """<h2 class="sr-only">Live preview of the generated crawler walk-cycle frames playing as a looping animation, with play/pause and a speed control.</h2>
<div style="padding:0.5rem 0;">
  <div style="display:flex; align-items:center; gap:16px; flex-wrap:wrap; margin-bottom:12px;">
    <button id="pp"><i class="ti ti-player-pause" aria-hidden="true"></i> Pause</button>
    <div style="display:flex; align-items:center; gap:8px;">
      <label style="font-size:13px; color:var(--color-text-secondary);">Speed</label>
      <input type="range" id="fps" min="2" max="16" value="8" step="1" style="width:130px;" />
      <span id="fpsv" style="font-size:13px; font-weight:500; min-width:46px;">8 fps</span>
    </div>
    <label style="display:flex; align-items:center; gap:6px; font-size:13px; color:var(--color-text-secondary);"><input type="checkbox" id="step" /> show frames side-by-side</label>
  </div>
  <div id="stage" style="display:flex; align-items:flex-end; justify-content:center; gap:24px; height:200px; background:var(--color-background-secondary); border:0.5px solid var(--color-border-tertiary); border-radius:var(--border-radius-lg);">
    <div style="text-align:center;"><img id="play" style="height:150px; image-rendering:auto;" /><div style="font-size:11px; color:var(--color-text-tertiary); margin-top:4px;">in-game ~46px</div></div>
    <div style="text-align:center;"><img id="playbig" style="height:46px;" /><div style="font-size:11px; color:var(--color-text-tertiary); margin-top:4px;">actual size</div></div>
  </div>
  <div id="row" style="display:none; gap:10px; justify-content:center; margin-top:12px;"></div>
</div>
<script>
(function(){
  const F=/*FRAMES*/;
  const play=document.getElementById('play'), big=document.getElementById('playbig');
  const pp=document.getElementById('pp'), fps=document.getElementById('fps'), fpsv=document.getElementById('fpsv');
  const step=document.getElementById('step'), row=document.getElementById('row'), stage=document.getElementById('stage');
  F.forEach((u,i)=>{ const im=new Image(); im.src=u; im.style.cssText='height:120px;border:0.5px solid var(--color-border-tertiary);border-radius:8px;background:var(--color-background-primary);'; im.title='frame '+(i+1); row.appendChild(im); });
  let i=0, playing=true, t=null;
  function show(){ play.src=F[i]; big.src=F[i]; i=(i+1)%F.length; }
  function loop(){ if(!playing) return; show(); t=setTimeout(loop, 1000/parseInt(fps.value)); }
  pp.onclick=()=>{ playing=!playing; pp.innerHTML = playing ? '<i class="ti ti-player-pause" aria-hidden="true"></i> Pause' : '<i class="ti ti-player-play" aria-hidden="true"></i> Play'; if(playing) loop(); };
  fps.oninput=()=>{ fpsv.textContent=fps.value+' fps'; };
  step.onchange=()=>{ const on=step.checked; row.style.display=on?'flex':'none'; stage.style.display=on?'none':'flex'; playing=!on; if(playing) loop(); pp.disabled=on; };
  show(); loop();
})();
</script>"""

if __name__ == "__main__":
    main()
