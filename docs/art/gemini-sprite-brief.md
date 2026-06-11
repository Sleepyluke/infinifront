# Sprite Generation Brief — Reference Faction (for Gemini image generation)

Self-contained brief for generating unit sprite sheets for an LLM-faction RTS
(StarCraft-style, 2D, Godot 4). Everything an image generator needs is in this
document. Paste the **Global Style Block** plus one **strip prompt** per request.

> **Status:** provisional sheet contract v0. Plan 3 (faction pack
> system/validator) will pin the final contract; if frame counts or cell sizes
> change there, regenerate or re-slice. Don't over-invest in polish yet.

---

## 1. Game & art direction

- Classic real-time strategy, ¾ top-down view (like StarCraft / Red Alert).
- **Pixel art**, SNES-to-late-90s-PC fidelity: clean 1px black-or-dark outlines,
  flat shading with 2–3 tones per material, no anti-aliasing, no gradients.
- Reference faction theme: **human military sci-fi** — utilitarian, industrial,
  vacuum-rated hardsuits, angular armor plates, visible servos and tool mounts.
  Think "blue-collar space army": welds, decals, scuffed paint.
- Light source: upper-left, consistent across every unit and frame.
- **Faction color**: each unit must include clearly separated accent areas
  (shoulder plates, visor glow, chest stripe) painted in **pure saturated red
  (#FF0000)** — the engine recolors these per player at import. Keep red OFF
  every other part of the sprite.
- Readability first: silhouettes must be distinguishable at 100% zoom (64px).
  Exaggerate key features (the marine's gun, the worker's tool arm).

## 2. Sprite sheet contract (provisional v0)

| Property | Value |
|---|---|
| Cell size (small units) | 64×64 px per frame |
| Facings stored | 3 — South (toward camera), West (left profile), North (away) |
| East | NOT drawn — importer mirrors West |
| Animations / frame counts | idle 4 · walk 6 · attack 6 · death 6 |
| Death | South facing only |
| Background | transparent in final PNG (see §3 — generate on magenta, key out) |
| Final sheet layout | one row per (animation, facing), frames left→right, in this order: idle-S, idle-W, idle-N, walk-S, walk-W, walk-N, attack-S, attack-W, attack-N, death-S |

The unit must stay centered in its cell with feet on a consistent baseline
(~8px above cell bottom) in every frame, or animations will jitter.

## 3. Practical workflow (image models can't do everything)

1. **No alpha channel.** Image generators output opaque images. Request a
   **uniform pure magenta (#FF00FF) background** in every prompt, then
   chroma-key it to transparency in post (any editor, or ImageMagick:
   `magick strip.png -fuzz 8% -transparent "#FF00FF" out.png`).
2. **Generate one horizontal strip per request** — one animation, one facing,
   N frames side by side in one image. Asking for the full 10-row sheet in one
   shot reliably fails alignment.
3. **Generate big, downscale later.** Ask for ~256px-per-frame strips (e.g. a
   6-frame walk = 1536×256). Downscale to 64px with **nearest-neighbor** only.
4. **Consistency:** paste the same Global Style Block + the unit's Character
   Block *verbatim* into every request for that unit. Generate the idle-South
   strip first, then attach that image to subsequent requests as the visual
   reference ("match this exact character").
5. **Expect manual cleanup.** AI strips usually need frame re-centering and
   palette unification. Budget for it; the game's importer validates the final
   grid and falls back to silhouettes if a sheet is malformed, so nothing blocks.

## 4. Global Style Block (paste into EVERY prompt)

```text
STYLE: 16-bit pixel art game sprite, classic RTS (StarCraft-era), 3/4 top-down
view, clean 1px dark outline, flat cel shading (max 3 tones per material), no
anti-aliasing, no gradient, no blur. Light source upper-left. Uniform pure
magenta #FF00FF background filling the entire image, nothing else in the
background. Faction-color accents in pure red #FF0000 only on the designated
accent areas. The character is centered with feet on a consistent baseline.
```

## 5. Units

### 5.1 FABBER (worker / harvester)

**Character Block (paste verbatim into every Fabber prompt):**

```text
CHARACTER: "Fabber" — squat human worker in a heavy industrial exosuit, bulky
yellow-painted torso plating with scuffs, dome glass helmet showing a face,
oversized left arm ending in a three-claw loader grip, right arm with a
compact plasma cutter, small thruster backpack with two stub exhausts, heavy
magnetic boots. Faction-red accent areas: helmet rim stripe and both shoulder
pauldrons. Carries a hip-mounted ore satchel.
```

Strip prompts (one request each):

| # | Prompt (append after Style + Character blocks) |
|---|---|
| 1 | `ANIMATION STRIP: 4 frames side by side, horizontal row, equal-width cells. Idle animation, facing SOUTH (toward viewer): subtle breathing bob, claw arm flexing slightly. Frame size 256x256 each, total image 1024x256.` |
| 2 | Same as #1 but `facing WEST (left profile)` |
| 3 | Same as #1 but `facing NORTH (back to viewer, backpack visible)` |
| 4 | `ANIMATION STRIP: 6 frames side by side, horizontal row, equal-width cells. Walk cycle, facing SOUTH: heavy industrial stomp, arms counter-swinging, ore satchel bouncing. Frame size 256x256 each, total image 1536x256.` |
| 5 | Same as #4 but `facing WEST` |
| 6 | Same as #4 but `facing NORTH` |
| 7 | `ANIMATION STRIP: 6 frames side by side, horizontal row, equal-width cells. Work/attack animation, facing SOUTH: raises plasma cutter, bright cutting arc flares for 2 frames, sparks. Frame size 256x256 each, total image 1536x256.` |
| 8 | Same as #7 but `facing WEST` |
| 9 | Same as #7 but `facing NORTH` |
| 10 | `ANIMATION STRIP: 6 frames side by side, horizontal row, equal-width cells. Death animation, facing SOUTH: suit ruptures with small steam bursts, unit crumples forward, ends as a static wreck heap on the ground. Frame size 256x256 each, total image 1536x256.` |
| icon | `SINGLE IMAGE: 64x64 pixel art UI portrait of the Fabber, head and shoulders, facing slightly left, magenta #FF00FF background.` |

### 5.2 TROOPER (basic ranged infantry / marine)

**Character Block:**

```text
CHARACTER: "Trooper" — human marine in mid-weight gunmetal-gray powered armor,
full-face visor with a horizontal slit glow, rifle held two-handed across the
chest: a chunky gauss rifle with a side magazine, segmented armor plates over
joints, low backpack power unit with one antenna. Faction-red accent areas:
visor slit glow, chest chevron stripe, and rifle stock plate.
```

Strip prompts:

| # | Prompt |
|---|---|
| 1–3 | `ANIMATION STRIP: 4 frames... Idle, facing SOUTH/WEST/NORTH: alert stance, slight weapon sway, visor glow pulses.` (4 frames, 1024×256) |
| 4–6 | `ANIMATION STRIP: 6 frames... Walk cycle, facing SOUTH/WEST/NORTH: tactical jog, rifle held ready.` (1536×256) |
| 7–9 | `ANIMATION STRIP: 6 frames... Attack, facing SOUTH/WEST/NORTH: shoulders rifle and fires, 2 frames of bright muzzle flash, slight recoil kick, ejected casing.` (1536×256) |
| 10 | `ANIMATION STRIP: 6 frames... Death, facing SOUTH: staggers back from impact, drops rifle, collapses to the ground, ends as a static fallen body.` (1536×256) |
| icon | `SINGLE IMAGE: 64x64 pixel art UI portrait of the Trooper, head and shoulders, visor glowing, magenta background.` |

### 5.3 OUTRIDER (fast skirmisher — hover bike)

**Character Block:**

```text
CHARACTER: "Outrider" — lone human rider on a low single-seat hover bike, rider
in a light pilot suit with an open-face helmet and goggles, bike is an angular
wedge with two forward vanes, underslung twin light autocannons, glowing
blue-white ground-effect haze beneath (no wheels). Faction-red accent areas:
bike nose cone, rider's helmet stripe, tail fin.
```

Strip prompts:

| # | Prompt |
|---|---|
| 1–3 | `ANIMATION STRIP: 4 frames... Idle (hovering), facing SOUTH/WEST/NORTH: gentle vertical bob, ground-effect haze flickering.` (1024×256) |
| 4–6 | `ANIMATION STRIP: 6 frames... Moving, facing SOUTH/WEST/NORTH: bike leans into motion, haze streaks, rider tucked low.` (1536×256) |
| 7–9 | `ANIMATION STRIP: 6 frames... Attack, facing SOUTH/WEST/NORTH: twin autocannons fire alternating muzzle flashes, slight nose pitch-up from recoil.` (1536×256) |
| 10 | `ANIMATION STRIP: 6 frames... Death, facing SOUTH: bike loses hover, sparks, slams to the ground and tumbles once, ends as a static smoking wreck.` (1536×256) |
| icon | `SINGLE IMAGE: 64x64 pixel art UI portrait of the Outrider rider with helmet and goggles, bike nose visible, magenta background.` |

## 6. Assembly checklist (after generation)

1. Chroma-key #FF00FF → transparency on every strip.
2. Downscale each frame to 64×64, nearest-neighbor.
3. Re-center each frame: feet baseline 8px above cell bottom, consistent across
   the whole unit.
4. Stack rows in the §2 order into one PNG per unit:
   `sprites/unit_fabber.png`, `sprites/unit_trooper.png`,
   `sprites/unit_outrider.png`; icons to `sprites/icons/<id>.png` at 64×64.
5. Pad short rows: every row is the width of the longest (6 frames = 384px
   final); idle rows (4 frames) leave the trailing 2 cells fully transparent.
   Final sheet: 384×640 px (10 rows).
6. Visual check at 100% zoom on a dark and a light background.
