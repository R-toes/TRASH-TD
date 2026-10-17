# PROJECT_GUIDELINES.md — TRASH TD

> **Agent directive:** Re-read this file at the start of every session on this project.
> It is the condensed, authoritative reference for design decisions and working agreements.

---

## 1. Core Mechanics Quick Reference

### 1.1 Operator Classes (5 total)

| Class | Role | Position | Block Count | Notes |
|---|---|---|---|---|
| Guard | Melee DPS | Melee | 1–2 | High single-target damage |
| Defender | Tank | Melee | 2–4 | High DEF/HP, low damage |
| Sniper | Ranged DPS | Ranged | 0 | High single-target damage, no blocking |
| Caster | Ranged AoE | Ranged | 0 | Arts damage (bypasses DEF, hits RES) |
| Medic | Healer | Ranged | 0 | Restores HP to allies in range |

### 1.2 Damage Formula (exact — do not alter)

```
Final Damage = max(ATK - DEF, ATK * 0.05)
```

- **Physical damage** is mitigated by **DEF**
- **Arts damage** is mitigated by **RES**
- The formula applies identically in both cases, substituting the relevant mitigation stat

### 1.3 Tile Types

| Tile Type | Deployable By | Notes |
|---|---|---|
| Low Ground | Melee operators | Ground enemy path tiles |
| High Ground / Ranged | Ranged operators (sometimes melee) | Elevated positions |
| Blocked / Hazard | Nobody | Spikes, water, unwalkable |

### 1.4 Rarity & Upgrade Rules

- Rarities: **1★ through 5★**
- Card draft only offers **1★, 2★, or 3★** base creatures
- 4★ and 5★ are reached **only** via upgrading
- **3 duplicate copies** of a creature → upgrades rarity by one tier
- **Upgrades merge cards in the deck only.** Deployed operators are never upgraded on the field — recall an operator to return its card to the deck, where it can merge. The upgraded card keeps the recall cooldown (at least 1 round when a recall triggered it, Guards included). A recall may merge into a full deck.
- Each round offers **3 card choices**; **no two cards share the same class**

---

## 2. Card-Draft Deployment Rule (critical — easy to accidentally revert to "squad select")

This game does **NOT** use a pre-built squad/roster selection screen. Instead:

- Each round, the player is offered **3 random cards** from a pool
- Each card represents an operator with a rarity (1★–3★)
- The 3 cards must have **no duplicate classes** among them
- The player picks one card to deploy (or potentially skip — TBD)
- This is a **draft mechanic** that happens during gameplay, not a pre-stage screen

Any UI, system, or flow that involves "select your squad before the stage" is **wrong** unless Raim explicitly changes this rule.

- Drafted cards go into the player's **deck** (`PlayerDeck`); operators are deployed from the deck bar.
- On the draft screen, **"View Map & Deck"** lets the player rearrange and discard deck cards during the pick (no deploying from there).
- At most **8 operators** can be deployed at once (`StageData.squadSizeLimit`; unlimited in Sandbox).

---

## 3. Data Architecture Mandate

Per GDD section 1.10 and the course's software-reuse objective:

- **Creature data** → `ScriptableObject` assets (`.asset` files), designer-editable without code changes
- **Enemy data** → `ScriptableObject` assets
- **Stage data** → Separate config files (ScriptableObjects or JSON) containing wave timing, spawn points, path nodes
- **Adding a new creature or enemy must not require modifying existing code** — only creating a new data asset and (optionally) a new prefab
- Architecture patterns (inheritance, interfaces, composition) are part of the assignment deliverable — favor reusable, extensible patterns over one-off implementations
- **Stage data lives in `Assets/Resources/Stages/*.asset`** — the single source of stage data.
- **Known deviation:** operators with a unique kit (e.g. Ignisaur, Proxishot, Chillpath) currently need a subclass *and* a name check in `OperatorManager` (`ShouldReplaceWithSpecializedComponent` / `AddClassComponent`); 4-frame sprite sheets need a name check in `OperatorSpriteAnimation`. Plain operators still need only a data asset + prefab. Prefer moving these name checks into `OperatorData` fields over adding more of them.
- **Draft pool:** `StageBootstrapper` fills `operatorPool` from `Assets/_Project/Data/Operators` **in the Editor only**. A player build only offers operators serialized in the scene's `operatorPool` — add new operators there before building.

---

## 4. "Do Not Invent" List

The following areas are **undefined in the source GDD**. Do not fill them in with invented content.
Use clearly marked `// TODO:` comments and placeholder implementations instead.

| Area | Status |
|---|---|
| **Narrative / Lore** | Not defined — no story, factions, or world-building |
| **Art / Visual Style** | **Superseded** — the team is adding real pixel-art sprite sheets for operators, enemies and UI (e.g. `Resources/UI/Deck BG.png`) and a pixel-art UI kit (see §7.5). Use placeholders only where no art exists yet |
| **Audio Direction** | **Superseded** — per-unit SFX exist (see §7.4). Don't invent audio direction beyond that |
| **Economy beyond card-draft** | Not defined — no currencies, shops, or meta-progression beyond the duplicate-upgrade system |
| **Persistence model** | Ambiguous — unclear if progression persists between runs or resets per map (could be roguelite). **Do not assume either way** — flag and ask Raim |
| **Skill specifics** | **Partly superseded** — the team defines per-operator traits/skills (written in each `OperatorData.skillDescription`; see §7.1). Implement skills the team specifies; still don't invent new ones |
| **Boss mechanics** | No separate boss format defined — treat bosses as wave entries |

---

## 5. Working Agreement (Agent ↔ Project Owner)

1. **Match the GDD exactly.** Do not add features, systems, or scope beyond what's specified without asking Raim first.
2. **Prefer minimal, targeted changes** over broad rewrites when modifying existing code.
3. **If an implementation choice isn't determined by the GDD** (e.g., exact stat numbers, exact Unity packages, specific skill effects), flag it as a decision point rather than picking silently.
4. **Keep this file updated** if Raim makes design decisions in later sessions that supersede a "not yet defined" placeholder.
   **Every major commit must update this file in the same commit** — new operators/enemies/stages, rule changes, new systems, reworks or balance passes. Update the affected sections (especially §7) and mention it in the commit description. If you pull someone else's major commit that skipped this, update the file before your next commit.
5. **Pathfinding must use A\*** — this is an explicit technical requirement, not a suggestion.
6. **All maps are grid-based** and selected non-linearly (Bloons TD style), with 3 difficulty variants each:

   | Difficulty | Waves | Life points |
   |---|---|---|
   | Easy | 10 | 3 |
   | Normal | 20 | 3 |
   | Hard | 30 | 1 |

   *(Supersedes the earlier 10/5/1 life points — current values are in `StageData.lifePoints*` and all stage assets. Raim to confirm.)* Difficulties differ only in wave count and lives; **enemy stats are not scaled by difficulty**, and wave N is the same on every difficulty.

---

## 6. Technical Stack (locked in)

| Aspect | Value |
|---|---|
| Unity version | 6000.6.2f1 (Unity 6 LTS) |
| Render pipeline | URP with 2D Renderer |
| Input system | New Input System |
| UI framework | uGUI (Canvas-based) |
| Grid rendering | Unity Tilemap (visuals) + custom GridManager (game logic) |
| Pathfinding | Custom A* implementation |
| 2D packages | Tilemap, Tilemap Extras, 2D Animation, SpriteShape, Aseprite |

---

## 7. Current Implementation Notes (as of 2026-10-17)

### 7.1 Roster

**Operators (19)** — draft rarity in brackets:

| Class | Operators |
|---|---|
| Guard | Chi Paw (1★), Scrapper (1★), Echosquire (2★), Ignisaur (2★), Stag-ger (2★), Shadeslice (3★) |
| Defender | Bulkhead (1★), Drawgoo (1★), Thornchin (2★), Mossmo (3★) |
| Sniper | Deadeye (1★), Proxishot (2★), Basurocket (3★) |
| Caster | Chillpath (1★), Pyrolite (1★), Castiel (2★) |
| Medic | Coalesce (1★), Progeny (2★), Bubblets (3★) |

Each asset's `skillDescription` is the authoritative description of that operator's kit. Recent changes:
- **Ignisaur** (Guard): Physical melee on its own tile / blocked enemies, Arts projectiles on forward range tiles, can hit air. *Protean*: faster attacks when not blocking, +25% ATK when blocking.
- **Proxishot** (Sniper): attacks never miss (ignores enemy and stage miss chance); when hit, gains 5 s Camouflage (enemies can't target it). Replaces the old "+3% ATK per enemy type killed" trait.
- **Chillpath** (Caster): chill → slow → freeze; faster attacks after freezing; every 4th attack hits one extra target (or the same one again).

**Enemies (10):** Grunt (Sludge, Sand Plastic), Rusher (Toxic), Tank (Pollution, Sand Hulk), Caster (Sand Poncho, Smog), Air (Flying Melee, Flying Ranged, Acid Flyer). Acid Flyer and Smog Caster now have sprites but are **not yet used in waves** — confirm they're fully configured before adding them.

**Stages:** STAGE_01, STAGE_02, STAGE_03, STAGE_06, plus STAGE_SANDBOX (development sandbox with multiple maps; cards can be deployed mid-wave and the squad limit is lifted).

**Stage environments:** Stage 3 = sandstorm (attack intervals ×0.95, 10% miss chance, wind/dust particles, animated background). Stage 6 = acid rain (3 physical damage to operators and enemies every 2 s while a wave runs, rain particles). The sandbox runs the environment of whichever map is selected — key environment checks on `StageBootstrapper.EnvironmentStageId`, not `stageData.stageId` (which is `SANDBOX` there).

### 7.2 Wave Design Rules

- Each wave is harder than the last, with a **spike every 5 waves**; the **final wave is the hardest**.
- Wave 1 must be beatable by a single 1★ operator (the first draft pick). Each stage starts slightly harder than the previous one (`StageData.extraStartEnemies`).
- Difficulty is weighed by **threat**, not raw HP: speed, air (only ranged operators/casters can hit it), DEF/RES and enemy ATK. Basic enemies count less (×0.7) because AoE, multi-block and healing handle crowds; basic enemies per wave are capped and extra budget buys elites.
- From wave 11, waves are **themed** (`WaveCurveBuilder.WaveTheme`): Standard, Rush, Air Raid, Armored Push, Air & Armor, Blitz, Siege. The final wave is tanks + air + rusher packs.
- Enemies arrive in packs / short windows (big waves within ~10–18 s), split across **all** spawn points from wave 1.
- Regenerated waves must be saved into `Assets/Resources/Stages/*.asset` — the Wave Planner / rebalance menu items are editor helpers and change nothing until run.

### 7.3 Combat Hooks (`OperatorBase` / `EnemyBase`)

- `EnemyBase.VisualPosition` — centre of the visible sprite. Aim visuals (projectiles, swipes, damage numbers, health bars) at it, not `transform.position`; game logic (range, grid cells) still uses `transform.position`.
- `CombatProjectileVisual.Fire(start, EnemyBase target, ...)` follows a moving target — prefer it over the fixed-position overload.
- `OperatorBase.IgnoresMissMechanic`, `IsCamouflaged`, `IsTargetable` — enemies skip operators that aren't targetable.
- **Nothing happens between waves.** `OperatorBase.IsCombatPaused` is true in every phase except `WaveActive` (Card Pick and Preparation; never in Sandbox). While paused, operators take no damage of any kind (enemy, trap, weather), receive no healing, gain/lose no shields, and run no attacks, heals or abilities. Any operator with its own `Update`/`TakeDamage` override must respect it.
- Enemy attack timers are "time since last attack", capped at one interval. Ground melee enemies spawn ready and keep charging while walking, so they **hit their blocker immediately on contact**; air combat enemies and Sand Hulk's frontal slam work the same way. Enemy casters keep their wind-up before the first shot.

### 7.4 Audio

- Per-unit clips load from `Assets/_Project/Audio/Resources/SFX/Operators/<Class>/<Name>/` (and `SFX/Enemies/<Archetype>/<Name>/`) via `UnitSfx`.
- Operators should call `PlayAttackSound()` / `PlayImpactSound()` so their own clips play, with the shared damage-type sound as fallback. Don't call `AudioManager.PlaySfx` directly for attacks without trying `UnitSfx.Play(data, UnitSfxEvent.Attack)` first.

### 7.5 UI Kit

- Pixel-art UI sprites live in `Assets/Resources/UI/` by category: `panels`, `buttons`, `icons`, `bars`, `cards`, `controls`, `decor`, `minis`. `ui_kit_atlas.json` lists each sprite's rect and category. No code reads it; it is reference only.
- UI is still built in code (uGUI). Style it through the helpers in `GameplayHUDUI`, not flat `Image.color` fills:
  - `ApplyButtonContainer(button, ButtonContainerType)`: Primary, Secondary, Dark, Danger, Warning, and the `Icon*` variants (`UI/buttons/btn_<type>_normal`).
  - `ApplyPanelContainer(go, PanelContainerType, tint?)`: Dark, Metal, Glass, Modal, Rust, Teal, Tooltip (`UI/panels/panel_<type>`). Modal = Metal panel plus hazard-stripe decor.
  - Both set `Image.Type.Sliced` and cache the sprite. If a panel sprite has no 9-slice border, a per-type border is applied at load time, so set borders in the importer for buttons. If the sprite is missing, the old flat colour stays.
- Keep UI textures on Point filtering (pixel art). Add new UI sprites under the matching `Resources/UI/<category>/` folder.
- **Draft range preview:** the card-pick details panel draws each operator's `rangePattern` as a tile grid facing right (`CardDraftOverlayUI.UpdateRangePreview`). It uses the placement-preview colours: cyan for the operator's own tile, green for tiles it can hit. It reads the same data as combat, so never hand-draw or screenshot range previews. A kit that reaches beyond `rangePattern` (e.g. Basurocket's splash) belongs in the ability text.
- `AudioSettingsUI` loads `panel_dark` / `btn_dark_normal` with its own copy of the loader. Prefer the `GameplayHUDUI` helpers for new UI.
