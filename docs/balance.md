# Cross-Faction Balance Analysis

> Data-driven balance pass over the 6 factions, computed from the **real**
> `PackValidator` power formula (`src/SimCore.Packs/PackValidator.cs`). Every number
> below is reproduced bit-for-bit from the validator's own arithmetic — confirmed by
> running `PackValidator.Validate` against each loaded pack. All 6 factions validate
> **clean** (0 errors, 0 warnings). This document is analysis + intent, not a tuning
> mandate; the engine never reads it.

## Design intent

### The point-budget model

`PackValidator` scores each unit with a linear **power** function and then a per-unit
**efficiency** = `power / cost`. The weights live in `BudgetWeights.Default`:

| Term | Weight | Contribution |
|------|-------:|--------------|
| HP | `Hp = 1.0` | `1.0 × MaxHp` |
| DPS | `Dps = 8.0` | `8.0 × (Damage × RefCooldown / max(1, CooldownTicks))`, `RefCooldown = 10` |
| Range | `Range = 4.0` | `4.0 × tiles` |
| Speed | `Speed = 30.0` | `30.0 × tiles/tick` |
| Sight | `Sight = 2.0` | `2.0 × SightRange` |
| Harvester | `Harvester = 40.0` | flat `+40` if the unit can gather |
| Shield | `Shield = 1.5` | `1.5 × MaxShield` — **only** for the `RegeneratingShields` mechanic |
| Supply (cost side) | `Supply = 25.0` | `cost = MineralCost + 25 × SupplyCost` |
| Tolerance | `Tolerance = 0.40` | the ±40% band (below) |

So **DPS** is `damage-per-10-ticks × 8`, **cost** folds supply in at 25 minerals per
supply point, and **efficiency** is power per effective mineral.

### The ±40% intra-faction band

The validator computes the **mean efficiency** of a faction's cost-bearing units (free
units with `cost ≤ 0` are excluded), then flags any unit whose efficiency falls
**outside ±40% of that faction's own mean**:

- `eff > mean × 1.40` → `BUDGET_OVERPOWERED` (warning)
- `eff < mean × 0.60` → `BUDGET_UNDERPOWERED` (warning)

This is a purely **intra-faction** check. It never compares one faction to another, and
it is most meaningful with ≥ 3 priced units (with exactly two divergent units, both can
fall outside the band).

### Mechanics priced at 0 — so raw stats must compensate

This is the load-bearing balance principle. The budget model assigns **zero power** to
three of the four faction mechanics:

| Mechanic | Faction | Priced in budget? |
|----------|---------|-------------------|
| `RegeneratingShields` | Concord | **Partially** — `1.5 × MaxShield` is added to every unit |
| `Regeneration` | Mycel | **No** — worth 0 power |
| `Lifesteal` | Sanguine | **No** — worth 0 power |
| `Splash` | Kiln | **No** — worth 0 power |
| (none) | Reference, Driftborn | n/a |

Consequence: a faction whose mechanic is free is getting real combat value the budget
model can't see. For that faction to be **cross-faction fair**, its raw-stat efficiency
should sit **at or below** the no-mechanic baselines (Reference / Driftborn). If a
free-mechanic faction also has the *highest* raw efficiency, it is double-dipping. The
cross-faction section below tests exactly this.

---

## Per-faction tables

Efficiency = `power / cost`, `cost = mineral + 25×supply`. The **harvester** (worker)
is included in the validator's mean (it bears a cost), but is broken out below because
its `+40` harvester flat term makes its efficiency a different animal from combat units.
Signature mechanic and identity noted per faction.

### Reference — "Vanguard" (no mechanic, the calibration baseline)

Identity: **balanced generalist** — the yardstick every other faction is measured against.

| Unit | HP | Dmg | Rng | CD | Speed | Sight | Cost | Power | Eff |
|------|---:|----:|----:|---:|------:|------:|-----:|------:|----:|
| fabber (worker) | 40 | — | — | — | 0.250 | 6 | 75 | 99.5 | 1.327 |
| trooper | 45 | 6 | 4 | 8 | 0.200 | 7 | 75 | 141.0 | 1.880 |
| outrider | 60 | 4 | 3 | 5 | 0.500 | 9 | 125 | 169.0 | 1.352 |
| tank | 150 | 20 | 6 | 20 | 0.125 | 7 | 225 | 271.8 | 1.208 |

**Mean eff 1.442** · band [0.865, 2.018] · all units inside.

### The Concord — shields (`RegeneratingShields`, MaxShield 30)

Identity: **durable shield-wall** — every unit carries a regenerating 30-shield buffer,
so it trades tempo for staying power and wins attrition fights.

| Unit | HP | Dmg | Rng | CD | Speed | Sight | Cost | Power | Eff |
|------|---:|----:|----:|---:|------:|------:|-----:|------:|----:|
| lumen (worker) | 50 | — | — | — | 0.250 | 6 | 85 | 154.5 | 1.818 |
| aegis | 70 | 9 | 4 | 9 | 0.188 | 7 | 140 | 230.6 | 1.647 |
| lance | 55 | 7 | 5 | 7 | 0.313 | 9 | 145 | 227.4 | 1.568 |
| monolith | 220 | 28 | 7 | 22 | 0.125 | 7 | 340 | 412.6 | 1.213 |

**Mean eff 1.562** · band [0.937, 2.186] · all units inside. *Note:* every Concord power
above includes a flat `+45` from the priced shield (`1.5 × 30`), so its listed mean is
shield-inflated, not free raw power — see cross-faction caveat.

### The Driftborn — fast & cheap (no mechanic)

Identity: **fast-cheap raider** — the cheapest units in the game, highest top-end speed
(buggy 0.625), built to swarm and harass; no special mechanic, pure tempo.

| Unit | HP | Dmg | Rng | CD | Speed | Sight | Cost | Power | Eff |
|------|---:|----:|----:|---:|------:|------:|-----:|------:|----:|
| picker (worker) | 35 | — | — | — | 0.313 | 6 | 70 | 96.4 | 1.377 |
| runner | 35 | 5 | 2 | 7 | 0.375 | 6 | 65 | 123.4 | 1.898 |
| slinger | 40 | 5 | 4 | 8 | 0.313 | 8 | 80 | 131.4 | 1.642 |
| buggy | 50 | 4 | 3 | 5 | 0.625 | 9 | 120 | 162.8 | 1.356 |
| hauler | 120 | 18 | 5 | 18 | 0.188 | 7 | 205 | 239.6 | 1.169 |

**Mean eff 1.488** · band [0.893, 2.084] · all units inside.

### The Mycel — regen-swarm (`Regeneration`, free)

Identity: **regenerating swarm** — cheapest combat bodies (crawler 65, behemoth only 250),
high speed, and free out-of-combat regeneration that rewards hit-and-run and re-engaging.

| Unit | HP | Dmg | Rng | CD | Speed | Sight | Cost | Power | Eff |
|------|---:|----:|----:|---:|------:|------:|-----:|------:|----:|
| spore (worker) | 30 | — | — | — | 0.313 | 6 | 70 | 91.4 | 1.305 |
| crawler | 35 | 5 | 1 | 7 | 0.438 | 6 | 65 | 121.3 | 1.866 |
| spitter | 30 | 5 | 4 | 8 | 0.313 | 8 | 75 | 121.4 | 1.618 |
| brood | 45 | 5 | 2 | 6 | 0.563 | 9 | 115 | 154.5 | 1.344 |
| behemoth | 160 | 20 | 5 | 18 | 0.188 | 7 | 250 | 288.5 | 1.154 |

**Mean eff 1.457** · band [0.874, 2.040] · all units inside.

### The Sanguine — lifesteal-predator (`Lifesteal`, free)

Identity: **vampiric predator** — tankier-than-cheap bodies (fang 75 HP) that heal from
the damage they deal, so they snowball any fight they're winning and sustain through
chip damage; pays for it with the lowest raw efficiency in the game.

| Unit | HP | Dmg | Rng | CD | Speed | Sight | Cost | Power | Eff |
|------|---:|----:|----:|---:|------:|------:|-----:|------:|----:|
| leech (worker) | 40 | — | — | — | 0.313 | 6 | 75 | 101.4 | 1.352 |
| fang | 75 | 7 | 1 | 8 | 0.313 | 6 | 115 | 170.4 | 1.482 |
| quill | 50 | 6 | 4 | 8 | 0.250 | 8 | 120 | 149.5 | 1.246 |
| prowler | 55 | 6 | 2 | 6 | 0.500 | 9 | 125 | 176.0 | 1.408 |
| maw | 200 | 22 | 4 | 16 | 0.188 | 7 | 300 | 345.6 | 1.152 |

**Mean eff 1.328** · band [0.797, 1.859] · all units inside.

### The Kiln — splash-artillery (`Splash`, free)

Identity: **molten-forge artillery** — slow (0.125–0.25), expensive, durable forged
constructs whose *every* attack splashes nearby foes for half damage. Zone control and
anti-swarm: devastating against clustered armies, helpless against nothing in particular
except its own slowness. Pays for the free splash with the **lowest raw efficiency in the
game**.

| Unit | HP | Dmg | Rng | CD | Speed | Sight | Cost | Power | Eff |
|------|---:|----:|----:|---:|------:|------:|-----:|------:|----:|
| cinder (worker) | 45 | — | — | — | 0.250 | 6 | 80 | 104.5 | 1.306 |
| ember | 60 | 7 | 4 | 12 | 0.188 | 8 | 120 | 144.3 | 1.202 |
| slag | 70 | 9 | 5 | 14 | 0.188 | 8 | 140 | 163.1 | 1.165 |
| bombard | 60 | 12 | 7 | 20 | 0.125 | 9 | 185 | 157.8 | 0.853 |
| furnace | 240 | 26 | 6 | 22 | 0.125 | 7 | 330 | 376.3 | 1.140 |

**Mean eff 1.133** · band [0.680, 1.586] · all units inside. `bombard` (0.853) is by design
the least cost-efficient — a long-range (7) glass artillery piece you pay a premium for;
its splash + reach is the value the budget can't see.

---

## Cross-faction comparison

The six factions side by side (the in-code "Vanguard" reference is byte-identical to
`packs/reference/faction.json`, so it shares the reference row):

| Faction | Mechanic | Priced? | Mean eff (all priced) | Mean eff (combat only) | Identity |
|---------|----------|:-------:|----------------------:|-----------------------:|----------|
| **Reference / Vanguard** | none | n/a | **1.442** | 1.480 | balanced baseline |
| **Driftborn** | none | n/a | **1.488** | 1.516 | fast-cheap raider |
| **Concord** | RegeneratingShields | partial (+45/unit) | **1.562** | 1.476 | shield-wall |
| **Mycel** | Regeneration | **no (0)** | **1.457** | 1.496 | regen-swarm |
| **Sanguine** | Lifesteal | **no (0)** | **1.328** | 1.322 | lifesteal-predator |
| **Kiln** | Splash | **no (0)** | **1.133** | 1.090 | molten splash-artillery |

("Combat only" strips the harvester — for Concord it also still contains the +45 shield
on each combat unit.)

### Verdict: broadly fair, with one deliberate outlier

The raw means cluster tightly in a **1.44–1.49** band for the four "honest" comparisons
(reference, driftborn, mycel, and Concord-after-removing-shields). That is well inside
normal RTS tolerance. Reading each against the mechanic-pricing principle:

- **Concord (1.562 listed)** — *looks* highest, but ~45 of every unit's power is the
  **priced** shield. Strip it and Concord's combat mean is **1.476**, dead-on the
  reference baseline. So Concord is **not** double-dipping: its durability is paid for in
  the budget. The flat +45 does favor its cheapest unit (lumen, 1.818), but that stays
  inside the ±40% band. **Fair.**
- **Mycel (1.457, free regen)** — raw efficiency is right at the reference baseline
  (1.442) and *below* Driftborn (1.488). It gets free regen on top of baseline-fair
  stats — i.e. very mildly favorable, but the surplus is within noise (~1% over
  reference). **Borderline-fair; lean slightly strong** because regen is free.
- **Sanguine (1.328, free lifesteal)** — by far the **lowest** raw efficiency, ~8% under
  reference and ~11% under Driftborn. This is the **intended** direction: lifesteal is
  free, so its raw stats are discounted to pay for it. The open question is whether a
  ~10% raw discount is the *right* price for lifesteal — that is a playtest call, not a
  validator call. **Fair in direction; magnitude unverified.**
- **Driftborn (1.488)** — highest of the no-mechanic factions, which fits its
  fast-cheap-raider identity (you pay full raw efficiency but get no sustain). **Fair.**
- **Kiln (1.133, free splash)** — the **lowest** raw efficiency of all six, ~21% under
  Driftborn and ~14% under Sanguine. This is the **strongest** application of the
  free-mechanic principle: splash (AoE every hit) is worth a great deal against grouped
  armies, so Kiln's raw stats are heavily discounted *and* slowed (0.125–0.25 speed) to
  pay for it. Direction is correct (well below baseline); like Sanguine, the magnitude is a
  playtest question — splash value scales with how clumped the enemy fights, so against
  spread-out micro Kiln could feel underpowered, against deathballs overpowered. **Fair in
  direction; magnitude the most variance-prone of any faction.**

### The mechanic-pricing caveat (explicit)

The budget model **cannot see** the value of Regeneration or Lifesteal — it prices them
at 0. So the efficiency numbers **understate** Mycel's and Sanguine's true power, and the
"correct" target for those two is to sit *below* the no-mechanic baselines. Mycel sits
*at* the baseline (slightly generous); Sanguine sits *below* it (correct). Because we
have no playtest data, the validator's ±40% intra-faction check is the only automated
gate, and all factions pass it — so any cross-faction nudge here is a judgment call, and
this document errs toward **documenting over re-tuning**.

---

## Matchup intent (rock-paper-scissors)

How a StarCraft designer would frame the intended counters, tied to the stats above.
Nothing here is enforced by code; it's the design target the numbers are meant to create.

- **Driftborn raids beat slow, expensive factions (Concord).** Driftborn fields the
  cheapest, fastest army (buggy 0.625 speed, runner 65 cost) with no sustain. Against
  Concord — whose units are the priciest (monolith 340, aegis 140) and slowest to mass —
  Driftborn's tempo and map control snowball before the shield-wall is online. **Driftborn > Concord.**

- **Sustain factions (Mycel / Sanguine) beat Driftborn's glass cannons.** Driftborn has
  the lowest HP in the game (35 on runner/picker) and *no* recovery. In a grind, Mycel
  regenerates between pokes and Sanguine heals off the damage it deals, so Driftborn's
  one-shot trades stop being favorable once the fight goes long. Driftborn must end
  games early or it folds. **Mycel > Driftborn**, **Sanguine > Driftborn.**

- **Static defense counters Driftborn.** Every faction has a cheap tier-1 turret
  (tower/ward/scrapgun/barb/bramble, dmg 10–12 range 6). Driftborn's whole plan is
  mobility and harass; walking into entrenched range-6 defense erases the tempo edge.
  Turtling is the textbook anti-raid answer. **Turtle > Driftborn.**

- **Burst counters lifesteal and regen.** Lifesteal and Regeneration both reward *long*
  fights. The hard counter is alpha-strike: kill the body before it heals. Concord's
  monolith (28 dmg) and Sanguine's own maw (22 dmg) — and any focus-fire that drops a
  unit in one volley — deny lifesteal/regen its payoff. **Burst > Sanguine/Mycel sustain.**

- **Concord beats the sustain factions in straight attrition.** Regeneration and
  lifesteal are out-DPS'd by raw effective-HP if the fight is unavoidable and prolonged:
  Concord's shield is *always-on* mitigation (it doesn't need to be winning, unlike
  lifesteal, or out-of-combat, unlike regen). A planted Concord line out-attrits a Mycel
  swarm or a Sanguine pack that can't disengage. **Concord > Mycel/Sanguine in a brawl.**

- **Mycel vs Sanguine — swarm vs predator.** Mycel is cheaper bodies + regen (win by
  numbers and re-engaging); Sanguine is fewer, tankier bodies + lifesteal (win by not
  dying once ahead). Mycel wants many short engagements (regen resets between them);
  Sanguine wants one decisive sustained brawl (lifesteal compounds). Whoever forces their
  preferred fight cadence wins — micro-dependent, intended to be close.

- **Kiln splash hard-counters the swarms (Mycel / Driftborn).** Splash damage on every
  hit is precisely the answer to cheap, numerous, clustered bodies — the more units the
  swarm packs into a fight, the more total damage each Kiln volley does. A Mycel crawler
  ball or a Driftborn buggy swarm melts walking into ember/slag/bombard fire. **Kiln >
  Mycel/Driftborn when they clump.**

- **Kiln is checked by spread-out micro, flankers, and its own slowness.** Splash rewards
  enemy clumping, so the counter is the opposite: spread units, attack from multiple
  angles, and exploit Kiln's crippling speed (0.125–0.25) and the bombard's fragility
  (60 HP at the back). Fast factions (Driftborn done right, Sanguine prowler, Mycel brood)
  can flank and reach the soft artillery before it sieges. Burst that one-shots a unit
  also denies splash its value-over-time. **Spread + flank + burst > Kiln.**

- **Reference / Vanguard is the honest midpoint.** No mechanic, balanced stats — it has
  no free win or hard loss; it tests whether the player out-executes, which is the point
  of a reference faction.

**Intended cycle (simplified):** Driftborn raids → punish slow Concord → but fold to
Mycel/Sanguine sustain and to Kiln splash → which fold to Concord attrition + burst, and
(for Kiln) to spread-out flanking that punishes its slowness → and static defense checks
Driftborn from the side. No faction sits at the top of every axis.

---

## Tuning recommendations

We have **no playtest data**, the validator's only automated gate is the ±40%
intra-faction band, and **all 6 factions already pass it clean**. Cross-faction tuning is
therefore a judgment call, and the policy here is **document, don't aggressively re-tune**.
A change is only applied if it is conservative *and* keeps the pack validating clean
(0 errors / 0 warnings) *and* was re-confirmed by re-running the validator.

### Applied

**None.** No stat changes were applied. Every faction already validates clean, and no
single conservative tweak clearly improves cross-faction fairness without risking the
intra-faction band or overriding a deliberate identity choice. Touching stats blind (no
playtest) would be more likely to harm than help.

### Recommendations (not applied — pending playtest)

1. **Sanguine — verify the lifesteal discount, don't change it yet.** Sanguine's raw mean
   (1.328) is ~10% under Driftborn (1.488). That under-pricing is *intended* (lifesteal is
   free), but the magnitude is a guess. If playtests show Sanguine underperforming, the
   conservative nudge is **+5 HP on `quill`** (50 → 55, its eff 1.246 is the faction's
   lowest combat unit) — small, stays in-band, raises the mean toward ~1.34. **Do not**
   buff `maw` or `fang`; their lifesteal already snowballs.

2. **Mycel — watch for over-performance.** Mycel's raw mean (1.457) is *at* the reference
   baseline while also getting free regen, so it is the most likely to be quietly too
   strong. If playtests confirm, the conservative lever is a **small cost bump on
   `crawler`** (40 → 45 mineral; its 1.866 eff is the faction's highest combat unit) to
   pull the mean toward Sanguine's. Leave stats alone otherwise — regen value is fight-
   length-dependent and only playtests can size it.

3. **Concord — leave as is; the +45 shield term is doing its job.** Concord's high listed
   mean is the *priced* shield, not free power. No change recommended. If anything, the
   flat-+45 model slightly over-rewards its cheapest unit (lumen 1.818) — but that's a
   **model** artifact, not a balance bug, and would be addressed by making the shield term
   proportional rather than flat (a `BudgetWeights`/`UnitPower` change, out of scope for a
   data-only pass).

4. **Model improvement (not a pack change): price Regeneration and Lifesteal.** The
   biggest source of cross-faction blind spots is that two of three mechanics are worth 0
   power. A future `UnitPower` revision could add small terms (e.g. regen → a function of
   `regenPerTick × MaxHp`, lifesteal → a function of `damage`), which would let the
   validator catch free-mechanic double-dipping automatically. This is a code change to
   `PackValidator`, deliberately **out of scope** for this data-only analysis and flagged
   here as the highest-leverage follow-up.

---

## Reproducing these numbers

The figures above were produced by loading each pack with
`FactionPackLoader.LoadAndValidate` and running the real `PackValidator`, then computing
`power`/`eff` with the exact `UnitPower` formula and `BudgetWeights.Default` from
`src/SimCore.Packs/PackValidator.cs`. All five packs report **0 load errors and 0
findings**; the in-code `ReferenceFaction.Def` is byte-identical to
`packs/reference/faction.json` (guarded by `ReferencePackTests`), so the sixth faction
matches the reference row. The reference faction's documented calibration target
("efficiencies ~1.21–1.88, mean ~1.44") reproduces exactly, confirming the formula
transcription.
