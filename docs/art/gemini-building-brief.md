# Sprite Generation Brief — Buildings & World Objects (for Gemini image generation)

Companion to `gemini-sprite-brief.md` (units). **Same faction, same style, same
magenta-key + red-accent workflow.** This brief covers the things that currently
render as flat colored boxes (buildings) and plain shapes (resource nodes).

> Loop: paste a prompt into Gemini → save the raw output into
> `assets/raw-sprites/<name>-vN.png` → Claude reviews it visually and critiques →
> iterate the prompt → once approved, chroma-key + downscale + wire into the game.

---

## 1. Style (identical to the unit brief — paste into EVERY prompt)

```text
STYLE: 16-bit pixel art game sprite, classic RTS (StarCraft-era), 3/4 top-down
view, clean 1px dark outline, flat cel shading (max 3 tones per material), no
anti-aliasing, no gradient, no blur. Light source upper-left. Uniform pure
magenta #FF00FF background filling the entire image, nothing else in the
background. Faction-color accents in pure red #FF0000 ONLY on the designated
accent areas (keep red off everything else). Match the look of my existing
human-military-sci-fi unit sprites: gunmetal-gray and industrial-yellow painted
metal, scuffed welded plating, blue-collar space army.
```

## 2. Building contract

| Property | Value |
|---|---|
| Footprint (reference depot + barracks) | 2×2 cells → **128×128 px** final |
| View | ¾ top-down, structure sits flat on the ground, "front" (doors) toward the **bottom** of the image (toward the camera) |
| Frames | **one static intact frame** for v1 (construction shading + HP bar + selection are drawn by the engine on top; a rubble/destroyed frame can come later) |
| Background | uniform magenta `#FF00FF` (keyed to transparent after) |
| Faction accent | pure red `#FF0000` on a roof trim / door banner / stripe only — the engine recolors it per player |
| Margin | leave ~6 px of empty magenta inside the 128 on all sides so the building doesn't visually collide with neighbors |
| Generate big | request **512×512** (single image), downscale to 128×128 nearest-neighbor |

Keep each building's **silhouette distinct** at 128 px — a player should tell the
depot from the barracks at a glance.

## 3. Buildings

### 3.1 DEPOT (HQ / worker factory / supply — `IsDepot`, `CanTrain`)

```text
SUBJECT: top-down ¾ view of a "Fabrication Depot" — the faction's squat command
and worker-production hub. A heavy industrial-yellow prefab block with welded
gunmetal-gray reinforcement plates, a large open assembly bay with a roll-up
door at the bottom (south) edge, an ore-intake hopper and conveyor on one side,
a stubby control tower with a dish antenna and a blinking beacon in one corner,
rooftop vents and pipe runs, magnetic clamp pads at the base. Worn, scuffed,
hard-working. Faction-red #FF0000 accents: the roof trim band and the chevron
stripe above the bay door. SINGLE IMAGE 512x512, the building centered and
filling most of the frame with a small magenta margin, magenta #FF00FF
background, soft short drop shadow toward lower-right.
```

### 3.2 BARRACKS (troop production — `CanTrain`)

```text
SUBJECT: top-down ¾ view of a "Muster Bay" barracks — a blocky reinforced
gunmetal-gray bunker for producing infantry. Heavy armored walls, a big armored
blast door at the bottom (south) edge with caution stripes, a small ready-room
annex, rooftop air-handling units and a comms mast, sandbag/ammo crates stacked
along one wall, floodlights at the corners. Military, utilitarian, no yellow
industrial paint (that's the depot's look — keep this one gray/steel to
contrast). Faction-red #FF0000 accents: a chevron over the blast door and a
thin roofline stripe. SINGLE IMAGE 512x512, centered with a small magenta
margin, magenta #FF00FF background, soft short drop shadow toward lower-right.
```

## 4. World objects

### 4.1 MINERAL NODE (neutral resource — NOT faction-colored)

```text
SUBJECT: top-down ¾ view of a mineral resource node — a cluster of 5-7 glowing
translucent crystal shards (cyan-to-teal) of varying heights jutting from a
small dark rock base, faint inner light, a few loose shards scattered around the
base. NO red accents (this is neutral terrain, not a faction object). SINGLE
IMAGE 256x256, centered, magenta #FF00FF background, soft short drop shadow.
Downscale target 64x64.
```

## 5. Assembly (after Gemini, same as units)

1. Chroma-key `#FF00FF` → transparent (`magick in.png -fuzz 8% -transparent "#FF00FF" out.png`).
2. Downscale nearest-neighbor: buildings → 128×128, mineral → 64×64.
3. Save: buildings to `godot/assets/buildings/<id>.png` (`depot.png`, `barracks.png`);
   mineral to `godot/assets/world/mineral.png`.
4. Visual check at 100 % on dark and light ground; confirm the red accents are a
   clean separated region (the importer recolors them).

## 6. Wiring (engine side — after art is approved)

`BuildingView` gains a sprite path like `UnitView`/`SheetAnimator`: if
`godot/assets/buildings/<defId>.png` exists, draw it (with the existing
construction-progress dimming, HP bar, selection ring, queue pips, rally flag on
top); else fall back to the current colored box. Same `ResourceLoader.Exists`
pattern noted in `BuildingView.cs`. Resource nodes get the same treatment in
their view. No SimCore / determinism impact — pure render layer.
