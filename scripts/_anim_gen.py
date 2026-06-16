#!/usr/bin/env python3
"""Generate the 4 animation strips (idle/walk/attack/death) for one unit via nano-banana,
anchored on its existing static sprite for character consistency. Reusable across units.

Usage: python scripts/_anim_gen.py <defId> <ref-sprite.png> "<creature description>"
Writes assets/raw-sprites/_anim_<defId>_<anim>.png for each animation.
"""
import subprocess, sys, os, tempfile

GEN = os.path.expanduser("~/.claude/skills/nano-banana/gen.py")

PREAMBLE = (
    "Keep the attached creature/unit EXACTLY — identical design, colors, proportions, and "
    "¾ top-down view facing the BOTTOM of the frame. {creature} "
    "Draw a sprite strip of exactly {n} animation frames arranged as ONE SINGLE HORIZONTAL ROW "
    "(a wide short image, the {n} frames left-to-right, NOT a grid, NOT stacked, NOT 2 rows): "
    "{motion} "
    "All {n} cells the same width; the SAME unit at the SAME size and screen position in every "
    "cell, only the pose changing. Leave a clear empty magenta gap between cells. Plain SOLID "
    "magenta #FF00FF background everywhere including the gaps. Clean dark outline, flat cel "
    "shading, no blur, matching the reference's exact art style and palette. NO dividing lines, "
    "NO borders between cells, NO frame numbers, NO text, NO drop shadows in the gaps."
)

ANIMS = [
    ("idle", 4, "a subtle IDLE loop — the unit nearly still, only a gentle breathing/sway and tiny limb shift; the 4 frames loop smoothly."),
    ("walk", 4, "a WALK CYCLE in 4 key poses — frame 1 left legs forward (contact), frame 2 mid-stride body lifted, frame 3 right legs forward (contact), frame 4 mid-stride body lifted; it loops."),
    ("attack", 4, "an ATTACK in 4 poses — frame 1 neutral, frame 2 wind-up/rear-back, frame 3 lunge/strike forward (mandibles, claws, or weapon firing), frame 4 follow-through."),
    ("death", 4, "a DEATH in 4 poses — frame 1 hit/stagger, frame 2 buckling, frame 3 toppling over, frame 4 a motionless destroyed husk on the ground."),
]


def main():
    def_id, ref, creature = sys.argv[1], sys.argv[2], sys.argv[3]
    for anim, n, motion in ANIMS:
        prompt = PREAMBLE.format(creature=creature, n=n, motion=motion)
        with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8") as tf:
            tf.write(prompt)
            pf = tf.name
        out = f"assets/raw-sprites/_anim_{def_id}_{anim}.png"
        print(f"--- {def_id} {anim} ({n}f) ---")
        r = subprocess.run(["python", GEN, "--prompt-file", pf, "--out", out,
                            "--model", "gemini-3-pro-image", "--ref", ref],
                           capture_output=True, text=True)
        print((r.stdout + r.stderr).strip().splitlines()[-1] if (r.stdout + r.stderr).strip() else "(no output)")
        os.unlink(pf)


if __name__ == "__main__":
    main()
