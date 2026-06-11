# InfiniFront

**The unlimited-faction RTS.** Describe a faction to an LLM — its units, tech,
and strategic identity — drop the result into the game, and command it in
battle. The engine supplies a fixed vocabulary of RTS mechanics; language
models compose that vocabulary into factions nobody has played before.

> Status: early development. The deterministic sim core and a playable
> sandbox are done; the faction-pack import pipeline is the next milestone.

## What works today

- Deterministic fixed-point C# sim: pathfinding (flow fields), combat,
  economy (harvest/build/train), fog of war, unit collision, stances
  (auto-attack / defend / passive), patrol, rally points
- A playable Godot 4 sandbox: two factions on a 40×40 map with full RTS
  controls — box select, attack-move, build, train, minimap, the lot
- AI-generated pixel-art sprites (via image-gen prompts) with a slicer tool
  that converts raw AI sheets into engine-ready sprite sheets
- 169 tests including a 500-tick golden-hash determinism guardrail that runs
  every sim mechanic under a replay net (multiplayer-lockstep-ready)

## Run it

Requires [Godot 4.6 .NET](https://godotengine.org/download) and the
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build godot/LlmRts.Godot.csproj
godot --path godot        # or open godot/ in the Godot editor and hit Play
```

Controls: left-click/drag select · right-click move/attack/harvest ·
A+click attack-move · P+click patrol · Tab switch player · Space pause ·
F3 toggle fog · Del scuttle · full table in [godot/README.md](godot/README.md).

## How it's built

- `src/SimCore` — headless deterministic sim. No engine references, no
  floats; every mutable field is folded into a state hash, and CI replays a
  scripted 500-tick match asserting a pinned trajectory hash.
- `godot/` — presentation layer. Reads sim state, interpolates at 60 fps,
  and issues commands; floats exist only at the render boundary.
- `tools/SpriteSlicer` — turns messy AI-generated sprite sheets into
  strict-grid sheets via hand-authored sidecar files.
- `docs/superpowers/` — the spec and per-feature implementation plans the
  project is built from.

## Roadmap

1. **Faction packs** — JSON schema, point-budget validator, and the prompt
   template that lets any LLM forge a balanced faction (the whole point)
2. CPU opponent + match flow (win/loss)
3. Multiplayer lockstep (the determinism work is the foundation)
