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
- [ ] **R2 — Concord pack** — author `packs/concord/faction.json` (shields mechanic) + validate clean + building art (nano-banana) + selectable. Golden-safe.
- [ ] **R3 — Driftborn pack** — author `packs/driftborn/faction.json` (stat-diff, no mechanic) + validate + art. Golden-safe.
- [ ] **R4 — Regeneration mechanic** — `MechanicKind.Regeneration` + heal hook (golden-safe, reuse TicksSinceDamaged) + pack DTO/mapper support + tests + gate.
- [ ] **R5 — Mycel pack** — author `packs/mycel/faction.json` (regeneration) + validate + art.
- [ ] **R6 — Packs-dir runtime fix** — make `PackCatalog` find repo `packs/` at runtime so new factions are selectable in-game (the 5e caveat). Compile-checked; flag for playtest.
- [ ] **R7 — Unit static-sprite fallback** — `UnitView` draws a static `<defId>.png` if present (else animated sheet, else circle), so new-faction units can use single-frame art without full animation sheets. Golden-safe render-layer; generate per-faction unit art.
- [ ] **R8 — Gameplay depth** (StarCraft-guided, golden-safe): e.g. a second tier-2 tech path, an area/anti-air consideration, more defensive variety. Pick the highest-value safe item each cycle.
- [ ] **R9 — Balance pass** — run `PackValidator` across all factions; tune to the budget band; document matchup intent.
- [ ] **R10 — Low-risk UI** (compile-checked): faction descriptions in the menu, build-button tooltips, etc.

(Reprioritize each cycle by value × verifiability. New ideas append here.)

---

## LOG (most recent last)

- 2026-06-15 — Roadmap + 3 faction designs written (R1). Loop started.
