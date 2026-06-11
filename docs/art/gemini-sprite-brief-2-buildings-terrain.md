# Sprite Generation Brief #2 — Buildings, Terrain & Effects (for Gemini)

Companion to `gemini-sprite-brief.md` (units). Same game, same style, same
workflow. This brief covers everything else the first playable build needs:
two buildings, the mineral resource node, a terrain tile set, and two optional
effect strips.

**How to use:** each numbered request below is ONE Gemini prompt. Paste the
Global Style Block first, then the Character Block (if the request names one),
then the request line. Don't combine requests.

---

## 1. Rules recap (read once)

- **Pixel art**, 16-bit RTS style (StarCraft-era), ¾ top-down, clean 1px dark
  outlines, flat cel shading, no anti-aliasing, no gradients, light from
  upper-left.
- **Background must be PURE MAGENTA `#FF00FF`** filling the whole canvas
  (exception: terrain tiles, §5, which fill their tiles fully). Last batch one
  sheet came back lavender and couldn't be chroma-keyed — if the background
  looks pastel or purple-gray, re-roll.
- **No text, labels, captions, or watermarks anywhere in the image.** Last
  batch had row labels baked into the sheets; they cost manual cleanup.
- **Faction-red accents**: pure `#FF0000` only on the areas each Character
  Block designates. No red anywhere else.
- Buildings are static set-pieces: separate requests do NOT need to match each
  other frame-perfectly. Generate, cherry-pick the best roll, move on.

## 2. Global Style Block (paste into EVERY prompt)

```text
STYLE: 16-bit pixel art game sprite, classic RTS (StarCraft-era), 3/4 top-down
view, clean 1px dark outline, flat cel shading (max 3 tones per material), no
anti-aliasing, no gradient, no blur. Light source upper-left. Uniform pure
magenta #FF00FF background filling the entire image, nothing else in the
background. No text, no labels, no captions, no watermark. Faction-color
accents in pure red #FF0000 only on the designated accent areas.
```

---

## 3. DEPOT (supply & mineral drop-off, 2×2 tiles)

**Character Block (paste verbatim into every Depot request):**

```text
CHARACTER: "Depot" — human military sci-fi supply depot. A squat, wide prefab
bunker with a low trapezoid profile, occupying a square footprint viewed from
3/4 top-down. Wide vehicle loading ramp with yellow-black hazard stripes on
the south (camera-facing) side. Four heavy corner stabilizer feet bolted into
the ground. Rooftop: two cylindrical storage silos on the left, one slim
blinking holo-antenna on the right. Scuffed gunmetal-gray plating with weld
seams and faded stencil markings. Faction-red accent areas: roof trim panels
and the loading door frame. The building fills most of the canvas, centered.
```

| Req | Prompt (after Style + Character blocks) |
|---|---|
| 3.1 | `ANIMATION STRIP: 4 frames side by side in one horizontal row, equal-width cells, identical building in every frame. Idle/operational animation: antenna light blinks on and off, small steam puffs vent from the silo tops, interior door light glow flickers subtly. The building itself does not move. Frame size 512x512 each, total image 2048x512.` |
| 3.2 | `SINGLE IMAGE: the same Depot under construction at roughly 50% completion: exposed orange steel frame girders where upper plating is missing, scaffold poles on two sides, a few welding spark points, building materials crated beside the ramp. 512x512.` |
| 3.3 | `SINGLE IMAGE: the same Depot destroyed: structure collapsed into a rubble heap, charred and bent plating, one silo toppled and split open, thin gray smoke wisps, scorch marks on the ground footprint. 512x512.` |
| 3.4 | `SINGLE IMAGE: 64x64 pixel art UI icon of the Depot, front 3/4 view, slightly simplified silhouette so it reads at small size, magenta background.` |

## 4. BARRACKS (trains infantry, 2×2 tiles)

**Character Block (paste verbatim into every Barracks request):**

```text
CHARACTER: "Barracks" — human military sci-fi infantry barracks. An angular
fortified hangar with a peaked armored roof, occupying a square footprint
viewed from 3/4 top-down. Large south-facing (camera-facing) blast door with
red chevron stripes, sized for soldiers to march out. A narrow horizontal
window slit on the east wall with warm interior light. Rooftop comms mast
with two dish antennas. Gunmetal-gray plating with a painted white unit
insignia (a simple star in a circle) on the roof. Faction-red accent areas:
blast door chevrons and comms mast tip. The building fills most of the
canvas, centered.
```

| Req | Prompt |
|---|---|
| 4.1 | `ANIMATION STRIP: 4 frames side by side in one horizontal row, equal-width cells, identical building in every frame. Idle/operational animation: mast dish rotates one step per frame, window slit light pulses gently, blast door status lamp blinks. The building itself does not move. Frame size 512x512 each, total image 2048x512.` |
| 4.2 | `SINGLE IMAGE: the same Barracks under construction at roughly 50% completion: exposed orange steel frame girders, the blast door not yet fitted (open dark doorway with crane straps), scaffold poles, welding sparks. 512x512.` |
| 4.3 | `SINGLE IMAGE: the same Barracks destroyed: roof caved in, blast door blown outward lying flat on the ground, charred walls partially standing, thin gray smoke, scorch marks. 512x512.` |
| 4.4 | `SINGLE IMAGE: 64x64 pixel art UI icon of the Barracks, front 3/4 view, simplified silhouette, magenta background.` |

## 5. MINERAL NODE (harvestable resource, 1 tile)

**Character Block:**

```text
CHARACTER: "Mineral node" — a cluster of 5-7 jagged crystalline ore shards
jutting at varied angles from a low rocky base mound. Ice-blue translucent
crystals with bright cyan facet highlights and a faint inner glow. Neutral
resource: NO red accents anywhere. Compact single-tile object, centered,
viewed from 3/4 top-down.
```

| Req | Prompt |
|---|---|
| 5.1 | `ANIMATION STRIP: 4 frames side by side in one horizontal row, equal-width cells, identical crystal cluster in every frame. Idle animation: the inner glow slowly pulses brighter and dimmer across the frames. Frame size 256x256 each, total image 1024x256.` |
| 5.2 | `SINGLE IMAGE: the same mineral node nearly depleted: only 2 small crystal stubs remain on the rocky base, surrounded by crystal rubble fragments and drill marks. 256x256.` |

## 6. TERRAIN TILE SET (one request, no magenta)

No Character Block. This is the one request where tiles must fill their cells
completely — no background color at all.

| Req | Prompt |
|---|---|
| 6.1 | `TILE SHEET: 8 terrain tiles in a single horizontal row, each tile exactly 256x256 pixels, total image 2048x256, top-down view, 16-bit pixel art, flat cel shading, no anti-aliasing, no text or labels. Muted tan-gray desert hardpan palette, low contrast so game units pop against it. Tiles 1-4: plain barren ground with subtle variation (faint cracks, scattered pebbles, dust patches) — these four MUST tile seamlessly with each other in any arrangement, no visible seams at tile edges. Tiles 5-7: impassable rock outcrops — clusters of large dark angular boulders sitting on that same ground texture, each variant a different boulder arrangement, edges of the tile matching the plain ground tiles so they blend. Tile 8: decorative cracked-earth variant of the plain ground, same seamless edges.` |

## 7. EFFECTS (optional polish — only if rolls are going well)

No Character Blocks.

| Req | Prompt |
|---|---|
| 7.1 | `ANIMATION STRIP: 6 frames side by side in one horizontal row, equal-width cells. Generic military explosion: frame 1 bright white-yellow flash core, frames 2-3 orange fireball expanding, frames 4-5 fireball darkening into a gray-black smoke ball rising, frame 6 thin dissipating smoke ring. Pixel art, on pure magenta #FF00FF background. Frame size 256x256 each, total image 1536x256.` |
| 7.2 | `ANIMATION STRIP: 4 frames side by side in one horizontal row, equal-width cells. Small bullet impact spark: tiny white-yellow starburst expanding and fading, a few spark flecks. Pixel art, on pure magenta #FF00FF background. Frame size 128x128 each, total image 512x128.` |

---

## 8. Delivery checklist

Drop everything into a folder like before (`Downloads\Sprites2` is fine) and
hand over the path. Before you do, a 10-second self-check per image:

1. Background is *screaming* magenta (except terrain tiles) — not pastel.
2. No text or labels anywhere.
3. Strips: frames are evenly spaced and the subject is identical across frames.
4. Red appears only where the Character Block says.

Priority order if Gemini fights you: **Depot → Barracks → Mineral node →
Terrain → Effects**. The game renders colored-rectangle fallbacks for anything
missing, so partial delivery is fine.
