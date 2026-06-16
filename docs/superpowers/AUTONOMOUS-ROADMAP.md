# Autonomous Build Roadmap & Log

**Started:** 2026-06-15 (user away ~5–6h; "keep working, best judgment, ask 'how did StarCraft do it'").
**Self-paced `/loop`.** Each cycle: pick next item → quick design → foreground TDD subagents → determinism gate (Debug==Release; golden `1571756151672809223UL` unchanged or counterfactual re-pin) → `git status` → commit + push + merge → update memory + the LOG below.

**Guardrails:** verifiable-only (headless tests + golden + art review; Godot changes compile-checked + additive — no playtest available, don't break the build). Determinism sacred. Foreground subagents only (no Workflow tool). Push every feature. When unsure → StarCraft analog + the safe path; keep going, don't block.

---

## Design north star: faction asymmetry (how StarCraft did it)

StarCraft's 3 races are *asymmetric* — different units, economies, and one signature mechanic each (Terran build/repair/lift-off; Zerg larva/creep/swarm; Protoss shields/warp-in/expensive). We already have **Reference / "Vanguard"** = the balanced human-military all-rounder (Terran-like): Fabber/Trooper/Outrider/Tank, Depot/Barracks/Supply Silo/Sentry Turret, no special mechanic. Goal: 3 more factions, each a distinct *feel*, balanced to roughly the same point budget (the `PackValidator` BudgetWeights), so matchups are fair-but-different.

Two ways to add a faction: (a) **pure pack JSON** (`packs/<id>/faction.json`, loaded by `PackCatalog`, validated by `PackValidator`) — 100% golden-safe, no code; (b) **+ a new mechanic** (new `MechanicKind` + a hashed-safe hook) — needs code, do golden-safe (no-op for other factions, no new always-folded state). Prefer (a); reach for (b) only for a faction's signature.

### Faction A — "The Concord" (synthetic / energy — Protoss-like) · pure pack (existing shields mechanic)
Few, expensive, durable, **shielded** units (reuse `MechanicKind.RegeneratingShields`). Low unit count, high per-unit value; losing a unit hurts. Mechanic already exists → pure content.
- Worker **Lumen** (assembler drone). Units: **Aegis** (shielded line infantry, tanky/pricey), **Lance** (energy skirmisher, ranged), **Monolith** (heavy walker, very expensive, tier 2 req Core). Buildings: **Core** (HQ/worker+supply), **Conclave** (combat production), **Capacitor** (cheap supply), **Ward** (shielded defense tower).
- Balance: ~1.3–1.6× Vanguard unit cost, fewer units, shields ≈ +30–50% effective HP; keep DPS/cost in the validator band.

### Faction B — "The Driftborn" (nomad scavengers — no SC equivalent) · pure pack (stat-differentiated, no mechanic)
Cheap, fast, fragile; cheap fast-building structures; hit-and-run. No signature mechanic — identity is *speed + low cost + low build times*. Economy-flexible, snowbally, punished by static defense.
- Worker **Picker**. Units: **Runner** (cheap fast short-range), **Slinger** (mobile ranged), **Buggy** (very fast raider, low HP), **Hauler** (mobile heavy, still faster than a Tank). Buildings: cheaper/faster-building Depot/Barracks/Supply/Turret analogs.
- Balance: ~0.7–0.85× Vanguard HP & cost, ~1.3–1.5× speed; validator keeps DPS/cost fair.

### Faction C — "The Mycel" (fungal swarm — Zerg-like) · NEW mechanic: Regeneration
Very cheap, numerous, fragile, and **regenerate HP when out of combat** (signature). Overwhelm with numbers; disengage to heal. New `MechanicKind.Regeneration` — heals when `TicksSinceDamaged` (already on every unit, already hashed) exceeds a threshold; reuses existing hashed state → **golden-safe, no re-pin** (no-op for non-Mycel units). Heal capped at MaxHp.
- Worker **Spore**. Units: **Crawler** (dirt-cheap melee swarm), **Spitter** (ranged), **Brood** (fast), **Behemoth** (heavy bio, tier 2). Organic buildings: **Hollow** (HQ), **Pit** (production), **Bladder** (supply), **Barb** (defense tower).
- Balance: lowest cost/HP; regen is the payoff for kiting. Validate carefully (regen isn't in the budget model — treat as a small efficiency bonus, keep raw stats slightly under-budget).

---

## Backlog (priority order)

- [ ] **R1 — Faction design** (this doc) — DONE inline above.
- [x] **R2 — Concord pack** — DONE. `packs/concord/faction.json` (shields, validates clean, no budget warnings) + ConcordPackTests + 4 building sprites (core/conclave/capacitor/ward, synthetic-energy look). Commits 198d9bd + adfaf7d. Golden untouched (375 tests).
- [x] **R3 — Driftborn pack** — DONE. `packs/driftborn/faction.json` (cheap/fast, no mechanic, validates clean) + DriftbornPackTests + 4 building sprites (roost/garage/cache/scrapgun, salvaged scrap look). Commits 3da72f4 + cbbf5f9. Golden untouched (376 tests).
- [x] **R4 — Regeneration mechanic** — DONE. `MechanicKind.Regeneration=2` + `Unit.MaxHp` (spawn-set, unhashed) + generalized `UpdateShields` switch (heal Hp out of combat, reuses RegenPerTick/RegenDelayTicks). No pack-DTO change (by-name enum). 383 tests, golden untouched (no re-pin). Commit 3cec3c9.
- [x] **R5 — Mycel pack** — DONE (data). `packs/mycel/faction.json` (Regeneration mechanic) + MycelPackTests (asserts mechanic round-trips). Validates clean: zero errors, zero budget warnings (tuned crawler 30→40 min to sit in the ±40% band). 384 tests, golden untouched. Commit c2c7774. Art (R5-ART) pending below.
- [x] **R5-ART — Mycel building sprites** — DONE. 4 fungal-organic sprites (hollow/pit/bladder/barb) via nano-banana Pro. Palette kept PURPLE-FREE (chitin-brown + bioluminescent teal-green) so the magenta chroma-key stays clean — purple structures would be keyed out. Reviewed each; re-rolled barb once (came out as a 2-panel concept sheet → tightened prompt to "single object, one view, no panels"). keysprite + headless import + reverted .import churn. Commit 805cd2e.
- [x] **R6 — Packs-dir runtime fix** — DONE. `PackCatalog.ResolvePacksDir(startDir)` walks up from the binary dir to the nearest ancestor with a `packs/` subdir (dev: repo root; shipped: beside exe), + `LoadAuto`. Both Godot `PacksDir()` (Main.cs + MenuScreen.cs) now use it → Concord/Driftborn/Mycel become selectable in the menu/lobby. 387 tests (3 new resolver tests), golden untouched, Godot C# project builds clean (0/0). PLAYTEST-FLAG: pick a non-Reference faction in the menu and confirm it loads. Commit pending.
- [x] **R7 — Unit static-sprite fallback** — DONE (code + first faction's art). `UnitView` now prefers `assets/units/<defId>.png` (per-def faction art) → else the reference animation sheet (by stat heuristic, unchanged) → else colored circle. Reads `u.DefId` directly (no ViewSync change). Static units get a small team-color ground pad for 4-player ownership; FlipH on facing E. Godot build 0 errors. PROOF: authored all 5 Mycel unit sprites (spore/crawler/spitter/brood/behemoth) — fungal creatures, purple-free for the key. Commit pending. (Remaining unit art for Concord/Driftborn/Vanguard-distinct = future R7-ART cycles; until authored those units keep the heuristic sheet.)
- [ ] **R8 — Gameplay depth** (StarCraft-guided, golden-safe): e.g. a second tier-2 tech path, an area/anti-air consideration, more defensive variety. Pick the highest-value safe item each cycle.
- [ ] **R9 — Balance pass** — run `PackValidator` across all factions; tune to the budget band; document matchup intent.
- [x] **R10 — Menu faction descriptions** — DONE (UI, render-only). MenuScreen shows an identity+playstyle blurb under the player's faction picker (FactionBlurbs table keyed by id: Vanguard/Concord/Driftborn/Mycel; custom packs get a generic line), updates on selection. Godot build clean. Commit pending. (Remaining UI ideas: build-button tooltips, lobby descriptions — future.)

(Reprioritize each cycle by value × verifiability. New ideas append here.)

---

## LOG (most recent last)

- 2026-06-15 — Roadmap + 3 faction designs written (R1). Loop started.
- 2026-06-15 — R2 Concord faction shipped: pack JSON (validates clean, no budget warnings) + ConcordPackTests + 4 building sprites. 375 SimCore tests, golden 1571756151672809223UL untouched. Pure data + render = golden-safe. (Note: headless import re-touches existing .import files with line-ending-only churn — `git diff` empty; `git checkout` them before commits to keep tree clean.) Next: R3 Driftborn.
- 2026-06-16 — R3 Driftborn faction shipped: pack JSON (validates clean) + DriftbornPackTests + 4 building sprites (salvaged scrap look). 376 tests, golden untouched. Three visually-distinct factions now (Vanguard industrial / Concord energy / Driftborn scrap). Next: R4 Regeneration mechanic (code — new MechanicKind, golden-safe via reusing TicksSinceDamaged).
- 2026-06-16 — R4 Regeneration mechanic shipped (CODE, golden-safe): `MechanicKind.Regeneration=2` + `Unit.MaxHp` (spawn-set, unhashed like DefId) + generalized `UpdateShields` switch (heals Hp out of combat once TicksSinceDamaged>=RegenDelayTicks, capped at MaxHp; byte-identical for shields + no-mechanic units). Reuses already-hashed Hp+TicksSinceDamaged → no new always-folded state → golden 1571756151672809223UL UNCHANGED, no re-pin. By-name enum → no pack-DTO change. 383 tests (7 new). Commit 3cec3c9. Confirms the "golden-safe keystone" pattern extends cleanly to a 2nd mechanic.
- 2026-06-16 — R5 Mycel faction shipped (DATA, golden-safe): `packs/mycel/faction.json` — fungal swarm using Regeneration. spore/crawler/spitter/brood/behemoth + hollow/pit/bladder/barb. Validates with ZERO errors AND zero budget warnings (validator's UnitPower gives regen 0 power → regen is "free", so raw stats sit slightly under-budget as designed; tuned crawler mineralCost 30→40 to pull its efficiency into the ±40% band). MycelPackTests asserts Regeneration round-trips through pack load. 384 tests, golden untouched. Four distinct factions now. Commit c2c7774. Next: R5-ART (Mycel sprites) then R6 (packs-dir runtime so factions are selectable in-game).
- 2026-06-16 — R5-ART shipped: 4 Mycel building sprites (hollow/pit/bladder/barb) via nano-banana Pro, fungal-organic (chitin + bioluminescent teal). KEY LESSON: kept the palette PURPLE-FREE because the magenta chroma-key removes magenta/purple hues — a purple "spore" palette would be cut along with the background. Re-rolled barb once (2-panel concept-sheet → "single object, one view" prompt). Commit 805cd2e. (Reusable art rule added: avoid magenta-adjacent purples on sprites destined for the magenta key.)
- 2026-06-16 — R6 packs-dir runtime fix shipped (golden-safe, the bridge to playability): `PackCatalog.ResolvePacksDir` walks up from `AppContext.BaseDirectory` (godot/.godot/.../bin/Debug at runtime) to the nearest ancestor `packs/` — so the menu/lobby now actually list Concord/Driftborn/Mycel instead of just Reference. +`LoadAuto`. Both Godot `PacksDir()` call it. 387 tests (3 resolver tests), golden untouched, Godot build 0/0. PackCatalog isn't in the sim hash → golden-safe. PLAYTEST-FLAG: confirm a non-Reference faction loads from the menu. Next: R7 (unit static-sprite fallback so new-faction units aren't all circles) then R8 gameplay depth.
- 2026-06-16 — R7 unit static-sprite fallback shipped (render-only, golden-irrelevant) + Mycel unit art: `UnitView` prefers `assets/units/<defId>.png` over the reference animation sheet (reads u.DefId directly; team-color ground pad + FlipH for static units). Authored all 5 Mycel unit sprites via nano-banana Pro (spore/crawler/spitter/brood/behemoth — fungal chitin+teal creatures, crawler as the unit-style anchor; purple-free for the magenta key). Godot build clean. So a Mycel army now renders as fungal creatures, not Vanguard troopers. Commit pending push. Next: R8 (gameplay depth — a StarCraft-guided golden-safe mechanic, e.g. a tier-2 path or anti-air), then more faction unit art (Concord/Driftborn) and R9 balance.
