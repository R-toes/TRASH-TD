# PROJECT_GUIDELINES.md — TRASH TD

> **Agent directive:** Re-read this file at the start of every session on this project.
> It is the condensed, authoritative reference for design decisions and working agreements.

---

## 1. Core Mechanics Quick Reference

### 1.1 Operator Classes (6 total)

| Class | Role | Position | Block Count | Notes |
|---|---|---|---|---|
| Guard | Melee DPS | Melee | 1–2 | High single-target damage |
| Defender | Tank | Melee | 2–4 | High DEF/HP, low damage |
| Sniper | Ranged DPS | Ranged | 0 | High single-target damage, no blocking |
| Caster | Ranged AoE | Ranged | 0 | Arts damage (bypasses DEF, hits RES) |
| Medic | Healer | Ranged | 0 | Restores HP to allies in range |
| Specialist | Utility / mimic | Any | per operator (Framiliar 1) | **Team addition, not in the GDD** (2026-10-24, with Framiliar). `SpecialistOperator`; recall cooldown 0 like Guard. Raim to confirm |

- **`OperatorPosition.Any`** deploys on Low Ground, Enemy Path, High Ground and Trap tiles (`GridCell.SupportsPosition`). `GridCell.CanDeploy` = not occupied + `SupportsPosition`; use `SupportsPosition` for "would this tile fit?" checks on occupied cells. An `Any` operator can hit air (`OperatorBase.CanHitAir`).
- A new class must be added to every class switch: `OperatorManager.AddClassComponent` and the recall cooldown table, `InitialAssetGenerator`, the class colours/labels in `CardDraftOverlayUI` and `GameplayHUDUI`, and air-targeting rules such as `RangedFlyingEnemy.IsTargetable`.

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
| Hole (`TileType.Hole`) | Nobody | Paired ladder/pipe tiles (Stage 5). A ground enemy entering one hides for 1.5 s (untargetable, invulnerable, unblockable) and comes out at the linked hole (`GridManager.GetLinkedHoleCell`) |

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
- On the draft screen, **"View Map & Deck"** (minimize, M) lets the player rearrange, discard **and deploy** deck cards during the pick, the same as in Preparation. Deploying frees a deck slot for the card being picked. Restore (M / RESUME) to pick. While minimized, RESUME sits on the deck bar between PREVIEW PATH and the bottom-right panel. The HUD reports that slot (`GameplayHUDUI.TryGetDeckBarFreeSlot`), and the draft re-places the button every frame, because the right-anchored panel moves with the aspect ratio. The rule lives in `StageBootstrapper.IsDeploymentAllowed` (Sandbox, Preparation, or Card Pick while `CardDraftOverlayUI.IsMinimized`) and `GameplayHUDUI.IsDeckDeployPhase`.
- **HP persists through retreat.** A retreated operator's exact HP is saved on its returned card (`DraftCard.savedHP`, −1 = full) and restored on redeploy (`OperatorBase.RestoreSavedHP`, called after `Initialize`). Nothing heals a benched card. Merging copies never heals: the upgraded card keeps the lowest HP fraction among them.
- At most **8 operators** can be deployed at once (`StageData.squadSizeLimit`; unlimited in Sandbox).
- **Deploying:** pick a deck card, click a tile, then hold and drag from the operator to set its facing. Release to confirm. Esc cancels. **Right-click** cancels placement or undeploys the selected operator. Keyboard shortcuts are listed in §7.6.
- **Preparation refund:** an operator deployed and retreated between waves (Card Pick or Preparation), before it has fought (`DeployedDuringCurrentPreparation && !HasFoughtInWave`), returns with **no recall cooldown**. Every other retreat uses the normal class cooldown. Always refunded in Sandbox.
- **No DP / deployment cost.** Deploying is free: the only limits are the deck, the squad cap and the recall cooldown. The recall cooldown is counted in **waves**, by class (`OperatorManager.GetRetreatCooldownRounds`: Guard 0, Sniper 1, Caster 1, Defender 2, Medic 3, Specialist 0), and ticks down when a wave ends (`PlayerDeck.AdvanceRedeployCooldownsOneRound`). `OperatorData.dpCost`, the seconds-based `redeployCooldown` and `IDeployable.GetDPCost` were removed on 2026-10-28. Don't bring back a DP cost or a cooldown in seconds.
- The draft overlay has card flip and shuffle animations. Card frame sprites exist in `Resources/UI/cards/` but are **not wired up yet**.
  - **Skip card animation** (a checkbox under the deck count, top-right; `CardDraftOverlayUI.SkipCardAnimation`, PlayerPrefs `TrashTD.SkipCardAnimation`, off by default): no shuffle, deal or flip. Cards land straight in their slots face-up, rerolls included. Turning it on mid-draft finishes the current animation and reveals the hand.
  - **Header:** "CHOOSE YOUR CARD" (y 94–154 from the top), and under it a plain-text wave counter "WAVE 1/20" (next wave / total, no icon). Both are centred over the card column. The cards and details panel sit at y 230–870 (`ContentCenterY` −10).

---

## 3. Data Architecture Mandate

Per GDD section 1.10 and the course's software-reuse objective:

- **Creature data** → `ScriptableObject` assets (`.asset` files), designer-editable without code changes
- **Enemy data** → `ScriptableObject` assets
- **Stage data** → Separate config files (ScriptableObjects or JSON) containing wave timing, spawn points, path nodes
- **Adding a new creature or enemy must not require modifying existing code** — only creating a new data asset and (optionally) a new prefab
- Architecture patterns (inheritance, interfaces, composition) are part of the assignment deliverable — favor reusable, extensible patterns over one-off implementations
- **Stage data lives in `Assets/Resources/Stages/*.asset`** — the single source of stage data.
- **Known deviation:** operators with a unique kit (e.g. Ignisaur, Proxishot, Chillpath, Shadeslice, Resicochet, Castiel, Echosquire, Framiliar) currently need a subclass *and* a name check in `OperatorManager` (`ShouldReplaceWithSpecializedComponent` / `AddClassComponent`); `OperatorSpriteAnimation` still special-cases Resicochet's multi-row sheet by name; `OperatorData.GetTalentRangePattern` also checks for `"Shadeslice"` by name. On the enemy side, `EnemyManager` checks for `"Askalaser"` and `"Hextension"` by name. Plain operators still need only a data asset + prefab. Prefer moving these name checks into `OperatorData` fields over adding more of them.
  - The checks compare `operatorName`, not the file name. The `"QiFu"` check never matches: `OP_Guard_QiFu.asset` is named **Chi Paw**, so Chi Paw relies on its prefab already having `QiFuOperator`.
- **Draft pool:** `StageBootstrapper` fills `operatorPool` from `Assets/_Project/Data/Operators` **in the Editor only**. A player build only offers operators serialized in the scene's `operatorPool` — add new operators there before building.

---

## 4. "Do Not Invent" List

The following areas are **undefined in the source GDD**. Do not fill them in with invented content.
Use clearly marked `// TODO:` comments and placeholder implementations instead.

| Area | Status |
|---|---|
| **Narrative / Lore** | **Partly superseded** — the team writes stage lore: the NERC corporation, the Calder Basin / NERC Development Zone, and the stage names and `StageData.shortDescription` texts (§7.1), plus `MainMenuController.StageZoneLore`. Use the team's text as written; don't invent story, factions or world-building beyond it |
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

**Keep the Console clean** (deprecated Unity 6.6 APIs warn on every compile):
- Use `FindAnyObjectByType<T>()`, not `FindFirstObjectByType<T>()` (deprecated).
- Use the `FindObjectsByType<T>()` / `FindObjectsByType<T>(FindObjectsInactive)` overloads without a `FindObjectsSortMode`.
- Use `#if UNITY_EDITOR || DEBUG`, not `DEVELOPMENT_BUILD` (deprecated); `Debug.isDebugBuild` at runtime.
- **Domain reload is off** (Enter Play Mode options), so static fields survive from one play session to the next, but textures/sprites/materials created at runtime are destroyed when play mode ends. Check a cached object with Unity's `== null`. For a cached **array or list** of them, check the elements too (e.g. `Array.TrueForAll(frames, f => f != null)`). Otherwise the second play reuses destroyed sprites and draws nothing (this hid the Stage 1 mist).
- A camera's target RenderTexture needs a depth buffer (e.g. `new RenderTexture(w, h, 24, ...)`). Without one, URP's render graph logs a warning every frame (`CrtScreen` used to flood the Console this way).

---

## 7. Current Implementation Notes (as of 2026-10-28)

### 7.1 Roster

**Operators (21)** — draft rarity in brackets:

| Class | Operators |
|---|---|
| Guard | Chi Paw (1★), Scrapper (1★), Echosquire (2★), Ignisaur (2★), Stag-ger (2★), Shadeslice (3★) |
| Defender | Bulkhead (1★), Thornchin (2★), Mossmo (3★) |
| Sniper | Deadeye (1★), Proxishot (2★), Basurocket (3★) |
| Caster | Chillpath (1★), Pyrolite (1★), Castiel (2★), Resicochet (3★) |
| Medic | Coalesce (1★), Progeny (2★), Bubblets (3★) |
| Specialist | Drawgoo (1★), Framiliar (2★) |

Each asset's `skillDescription` is the authoritative description of that operator's kit. Recent changes:
- **Ignisaur** (Guard): Physical melee on its own tile / blocked enemies, Arts projectiles on forward range tiles, can hit air. *Protean*: faster attacks when not blocking, +25% ATK when blocking.
- **Proxishot** (Sniper): attacks never miss (ignores enemy and stage miss chance); when hit, gains 5 s Camouflage (enemies can't target it). Replaces the old "+3% ATK per enemy type killed" trait.
- **Chillpath** (Caster): chill → slow → freeze; faster attacks after freezing; every 4th attack hits one extra target (or the same one again).
- **Shadeslice** (Guard, reworked into `ShadesliceOperator`): 2.0× damage when no ally stands in its talent pattern (12-tile diamond, `OperatorData.ShadesliceTalentPattern`). After 5 s without being attacked, it regenerates 2.5% max HP per second.
- **Stag-ger** (Guard): hits every blocked enemy on its tile and in range ahead, including enemies blocked by allies. With nothing blocked, it hits one target. 20% dodge; a successful dodge gives +50% ATK on the next attack.
- **Drawgoo** (Specialist since 2026-10-26, `OP_Specialist_Drawgoo.asset`, `SpecialistOperator`; it used to be `OP_Guard_Drawgoo`): on death, draws 2 random cards. An upgraded Drawgoo instead draws 3★ cards: 1 card (60%) or 2 (40%) (`CardDraftSystem.DrawRandomCardsToDeck(count, targetRarity)`).
- **Mossmo** (Defender): doesn't attack. It **taunts** (`HasTaunt`): ranged enemies on a surrounding tile target it first. When it takes damage, it heals up to 2 operators in range, itself included.
- **Castiel** (Caster, `CastielOperator`): continuous light laser locked on one target (`LightLaserVisual`, `LaserPalette.Holy`: white core, pale yellow body, gold glow). Ticks every 0.375 s; damage ramps every 1.5 s: 5 → 15 → 30 → 80 → 100 → 150. Its halo animation speeds up (frame 0.10 s → 0.04 s) as the beam ramps.
- **Resicochet** (Caster, `ResicochetOperator`): high DEF, low RES. Stores a charge every 5 s (max 3, starts with 1). Each attack fires 1 projectile per charge, 0.5 s apart. Each projectile deals Arts damage and bounces to up to 4 targets in total, losing 25% per bounce. Every hit applies **Sluggish**. The sprite row follows the charge count (`Resicochet_0–11`, 4 frames per row).
- **Framiliar** (Specialist, `FramiliarOperator`, position `Any`, blocks 1): after deploying, select it to open the copy menu (`GameplayHUDUI.ShowFramiliarCopyMenu`). The menu mirrors the deck slots; Framiliar cards are left out. Picking a card calls `OperatorManager.TransformFramiliar`:
  - The copy replaces Framiliar on its tile, in grayscale (`FramiliarOperator.ApplyGrayscale`, `Resources/Shaders/GrayscaleSprite.shader`).
  - The copied operator must fit the tile (`SupportsPosition`).
  - It keeps facing, HP fraction, heat and the fought/prep flags. `IsCopiedFromFramiliar` / `OriginalFramiliarData` / `OriginalFramiliarRarity` remember the original.
  - **Retreat:** retreating it in the same prep phase before it fights returns a Framiliar card at its original rarity with no cooldown. After it has fought, it returns as the copied operator for good. `WaveManager` calls `OperatorManager.MarkAllDeployedOperatorsAsFought()` whenever a wave starts (normal and sandbox).
- **Talent range overlay:** `OperatorData.talentRangePattern` (optional) is drawn in orange under the green attack range in the placement preview. Use `GetTalentRangePattern()`.

- **Deadeye** (Sniper): `DeadeyeSpriteAnimation` plays the 8-frame `Deadeye-Sheet` at 0.125 s per frame. The prefab lists all 8 frames; the component accepts any count of 2 or more.
- **Basurocket** (Sniper): its rocket blasts a circle of `BasurocketOperator.BlastRadiusTiles` (1.75 tiles, a bit bigger than the 1.5-tile caster splash) around where the rocket lands. It used to blast the 3×3 tiles around the target cell. The circle drawn is the area hit. The blast is yellow-orange (`AoeStyle.JunkRocket`), so it never looks like THE ERADICATOR's crimson blast.

**Enemies (20):** Grunt (Sludge, Sand Plastic, Oculite Spawn, Robozombie, Spoilcupine), Rusher (Toxic, Exploding Bug), Tank (Pollution, Sand Hulk, Oculoid Carrier, THE ERADICATOR, Gooelist), Caster (Sand Poncho, Smog, Askalaser, Hextension, Roboflame), Air (Talon Rat = `Enemy_Flying_Melee`, Skyshard = `Enemy_Flying_Ranged`, Acid Flyer).
- **Sprite animations play every frame of their sheet.** Operator sheets are one row of square frames; `OperatorSpriteAnimation.GetSheetFrameCount` = width ÷ height (Basurocket 5, Pyrolite 4, Chillpath / Echosquire / Proxishot 8...), and the prefab `frames` lists hold the same count so the UI previews (`UnitSpriteFrames`) match gameplay. Resicochet is the exception: 4 frames per charge row. Enemy `animationFrames` / prefab frame lists also hold the whole sheet. A sheet must be sliced in its `.meta` into one sprite per frame (Proxishot's was re-sliced from 2 to 8).
- **Talon Rat** (`MeleeFlyingEnemy`) and **Skyshard** (`RangedFlyingEnemy`): 8-frame sheets (`Talon Rat Sheet.png`, `Skyshard Sheet.png`) through `EnemyData.animationFrames` at 8 fps, drawn 0.75 above their path (`visualOffset`) with no tint. Air units draw at sorting order 12 (above the foreground art); `AirCombatEnemy.CreateAirborneVisuals` applies that to the visible `EnemyVisual` child too. Talon Rat swipes from its talons. Skyshard throws a glass shard (`ProjectileStyle.Glass`) that shatters on impact (`AoeStyle.Glass`); the blast radius (1.05 tiles, half ATK to others) is unchanged. **Movement while fighting:** flyers stop on each attack (`AirCombatEnemy.GetAttackPause`) and move at `EngagedSpeed` while a target is in range. Talon Rat hovers for 70% of its attack interval, then creeps at 0.5x. Skyshard stops only for the 0.15 s throw, then keeps flying at 0.5x between throws (full speed with nothing in range).
- **Not in any wave yet:** Acid Flyer, Smog Caster, Askalaser, Oculoid Carrier, Exploding Bug, Robozombie, Hextension, Roboflame, THE ERADICATOR and Gooelist. In the Editor the sandbox lists every enemy asset, so all of them can be spawned there. Oculite Spawn is only spawned by Oculoid Carrier. Confirm a unit is fully configured before adding it to waves.
- **Casters are blockable** (block weight 1). This overrides GDD 1.4.4 / 1.5, which made them immune; the team changed it on 2026-10-26. Every caster asset has `isUnblockable: 0`.
  - **While blocked**, a caster stops in front of its blocker and keeps using its own attacks on its usual targets. It never melees the blocker: `EnemyBase.AttackBlocker` skips casters, and `UpdateRangedBehavior` holds the blocked position.
  - **Per caster:** Askalaser keeps its State 1 / State 2 attacks, Hextension binds and then shoots, and Roboflame holds and flames instead of creeping on.
- **Roboflame** (`RoboflameEnemy`, Caster, `Enemy_Caster_Roboflame.asset`): Arts damage, 400 HP, 20 ATK per tick, DEF 20 / RES 25, speed 0.7.
  - **Flame cone:** relative to its path direction, the cone covers its own tile, 1 tile ahead, and 3 tiles across at 2 tiles ahead (`GetConeCells`).
  - **Attack:** it walks until an operator is in the cone, then sprays fire for 3.5 s, sweeping side to side. Each sweep (0.7 s) is one damage tick of ATK Arts damage to every operator in the cone, so a burst is 5 ticks. A 1.5 s cooldown follows. If the cone empties mid-burst, the burst ends early.
  - **Movement:** while flaming it creeps forward at 15% speed (it holds in front of a blocker instead) (`BurstMoveSpeedMultiplier`), and the flame follows its path direction. During the cooldown it walks at 75% speed (`CooldownMoveSpeedMultiplier`). Otherwise it walks at full speed.
  - **Stage 4:** every 2nd tick that lands on the same operator adds 1 heat stack to it (`Stage4HeatHazard.AddHeatStack`).
  - **Visuals:** `FlamethrowerVisual.Emit` draws the jet: hot puffs, big licking tongues, a flickering nozzle glow, flames curling up off the stream, and embers and smoke at its tip. `FlamethrowerVisual.Ignite` adds a burst of flame on every operator a tick hits. The 4-frame sheet plays through `EnemyData.animationFrames`.
- **THE ERADICATOR** (`EradicatorEnemy`, Tank, `Enemy_Tank_Eradicator.asset`): 1500 HP, 60 ATK, DEF 50 / RES 10, speed 0.4, physical damage, **block weight 2**, life cost 2. It is drawn at 2× (`visualScale: 2`, 64 px frames, like Sand Hulk).
  - **Melee:** it swings at its blocker, or at a ground operator (any tile except High Ground) on its own tile or one of the 4 adjacent tiles, every attack interval (2 s). The first hit on each operator stuns it for 2 s. Melee replaces rockets.
  - **Rockets:** otherwise, every 3 s (1.5 attack intervals) it stops briefly and fires at the lowest-HP operator within 3 tiles (taunt first). Each rocket blasts a 1.75-tile circle around its target (`BlastRadiusTiles`, the same size as Basurocket's).
  - **Blast look:** a crimson heavy-ordnance blast (`AoeStyle.HeavyOrdnance`): a red-tinted fireball, dark shrapnel, a column of black smoke, a second shockwave and a scorch mark left on the ground.
  - **Warning marker:** while a rocket is in the air, `BlastWarningVisual` marks the blast area. It shows a faint fill and a target ring that blinks faster as impact nears, and a second ring closes in on the centre.
  - **Heavy rocket:** every 4th rocket uses its own projectile (`ProjectileStyle.HeavyRocket`) and a "HEAVY!" label, and deals 1.5× damage. Its blast is the same, in molten red-orange instead of crimson. On Stage 4 it also adds 1 heat stack to every operator it hits.
  - Rockets launch from the front of its arm cannon (`EnemyData.projectileOrigin`, mirrored with facing; see Projectile origins in §7.3). Its melee uses `MeleeStyle.Smash`.
- **Askalaser** (`AskalaserEnemy`): Arts damage, low DEF / high RES, Grunt speed, blockable (weight 1). It switches between two states:
  - **State 1:** global range. Every 5 s it stops and deals 20 Arts damage to the deployed operator with the lowest DEF.
  - **State 2:** starts when an operator is inside its pattern: a 12-tile diamond (distance ≤ 2) for every unit, widened to 5×5 minus the corners for ranged units. It stops and fires a Castiel-style ramping laser at the lowest-DEF target. The laser lasts until that target dies, camouflages or leaves the pattern. The laser uses `LaserPalette.Crimson` (pink-white core, red body, dark crimson glow), so it never looks like Castiel's.
  - **Sprites:** the top row of the sheet is the walk cycle and the bottom row (8 × 32 px) is the laser animation, used in State 2. The laser starts from a `LaserMuzzle` child (`muzzleOffset`, mirrored with facing).
- **Hextension** (`HextensionEnemy`, Caster): Arts damage, 550 HP, 50 ATK, DEF 5 / RES 40, blockable (weight 1).
  - **Before it binds:** ranged operators can't target it (`CanBeTargetedBy`), and it is drawn at 45% alpha.
  - **Bind:** it casts this once, on the first operator inside Askalaser's pattern. Taunt wins; otherwise it picks the lowest RES, then the nearest.
  - **While binding:** it stops and channels for 15 s, dealing 25 Arts damage per second. The target is bound (see Bind in §7.3) and tethered by a wire of a random colour: red, blue or yellow (`BindingLineVisual`, `WireColorPalette`).
  - **Wire look:** pixel art rasterised on the stage-art grid (1/32 world unit) into a mesh of 1 px quads, rebuilt each frame.
    - **Tether:** a sagging, swaying cable with a 2 px core and 1 px outline, shaded with a highlight row on top and a shade row underneath. Bright pulses run toward Hextension (the drain). It lashes out over 0.18 s when the bind starts.
    - **Operator:** three coils tighten one by one from the plug side (ends darkened where they turn behind the body), with a plug where the tether connects. These draw inside the operator's sorting group.
    - **Sparks** crackle at the plug about once a second and flash the coils. When the bind ends, the wire snaps with a spark burst.
  - **Breaking the bind:** the bind ends early if the target is Proxishot, camouflages, dies or leaves the field, or if Hextension is stunned, frozen, pushed back, falls into a hole or dies. A Bubblets bubble that absorbs a tick does not break it.
  - **After the bind** (whether it finished or broke), Hextension can be targeted by everyone, gets ATK ×1.6 and fights as a normal ranged caster for the rest of its life.
- **Oculoid Carrier** (`OculoidCarrierEnemy`): high DEF, low RES. Immune to Stage 6 acid rain and acid/trap tiles. While blocked, it reduces its blocker's block count by 1. On death it spawns 2 **Oculite Spawns** (`OculiteSpawnEnemy`: same DEF/RES profile and immunities), which continue to the exit.
- **Exploding Bug** (`ExplodingBugEnemy`, a Rusher with Toxic Rusher stats): on death it deals its ATK as physical damage to operators on the 4 adjacent tiles.
- **Gooelist** (`GooelistEnemy`, Tank, `Enemy_Tank_Gooelist.asset`): physical damage, 650 HP, 45 ATK, DEF 120 / RES 0, speed 0.9, block weight 1. 8-frame sheet (`Gooelist Sheet.png`) played through `EnemyData.animationFrames` (`GooelistSpriteAnimation` only runs as a fallback when the data has no frames).
  - **Duel:** while blocked, only its blocker can damage it (`CanReceiveDamageFrom`). Other operators skip it through `CanAttackTarget`, and splash/AoE hits from them are deflected with a "DUEL!" popup. Stage hazards call `TakeDamage` directly and still hit it.
  - **Armor shred:** while blocked, its DEF drops 75% and the blocker's DEF drops 50% (`SelfDefReductionPercent` / `OperatorDefReductionPercent`, applied in the `CurrentDEF` getters). Bulkhead's frenzy `CurrentDEF` override skips the blocker shred.
- **Robozombie** (`RobozombieEnemy`, a Grunt with Sludge Grunt stats): has a 25% chance on death to detonate like an Exploding Bug, dealing 3× ATK true damage to operators on the 4 adjacent tiles.

**Stages:** STAGE_01 Blackwater Facilities, STAGE_02 Milestone 9, STAGE_03 Railyard Drain, STAGE_04 NERC Works Plant, STAGE_05 New Calder Basin, STAGE_06 The Pale. The stages were renamed to the team's lore on 2026-10-24; the old names were Landfill Outskirts, Scrapyard Junction, Railyard Crossing, Industrial Factory, Boulevard and Meltdown. There is also STAGE_SANDBOX, a development sandbox with multiple maps, where cards can be deployed mid-wave and the squad limit is lifted.

**Stage environments** (each has its own component created by `StageBootstrapper`, and an environment briefing in `GameplayHUDUI`; the constants in each script are authoritative, and some doc comments are out of date):
- **Stage 1 — mist & rain** (`Stage1WeatherVisuals`, visual only, no gameplay effect):
  - **Mist:** volumetric puffs (3 generated variants, lit from above, slowly billowing and bobbing) drift left in two layers (`MistLayers`):
    - **Back** at sorting order 3: above the map art (1), beneath ground units (4–5).
    - **Front** at order 11: above ground units and the foreground art (10), below air units (12). It may roll over operators and enemies.
    - A gradient fog bank (order 11) covers the backdrop row above the grid.
  - **Mist density:** thickness comes from a per-tile density field: full off the grid and along the edges, 0.55 on High Ground, 0.3 on the enemy lane and other walkable tiles. The front layer raises it to the power 2.2, so it is nearly invisible over the path (≈0.07) but heavy on the backdrop. Keep the path clear when tuning.
  - **Rain:** it rains almost all the time. The forecast queue alternates light (30–60 s) and medium (25–50 s) spells. After a medium spell there is a 30% chance of a short dry break (6–12 s), so it's dry about 3% of the time. The stage opens already raining, and intensity eases between states.
  - **Rain visuals:** slanted pixel streaks at order 24 (above units, below projectiles), splash dashes on the ground at order 3, a darkening tint at order 2, and up to 35% thicker mist.
  - A `SpriteMask` clips the mist and rain to the map artwork. Runs on scaled time, so it freezes while paused.
- **Stage 2 — falling items** (`Stage2FallingItemsHazard`): every 10 s an item drops on a random operator (40%), deployable tile (40%) or enemy (20%). Glass: 2 true damage/s for 5 s. Metal: 15 true damage. Tools: 15 true damage + 2 s stun. Thornchin doesn't retaliate. The hit pops Bubblets' shield, triggers Mossmo's heal, and can be dodged by Stag-ger. Proxishot still gains camo. Sprites come from `StageData.fallingItemSprites`.
- **Stage 3 — sandstorm:** attack intervals ×0.95 and a 10% miss chance (`StageCombatModifiers`), plus an animated background.
  - **Visuals** (`Stage3SandstormVisuals`, visual only): pixel-art dust clouds blow left to right in two layers, like the Stage 1 mist. They are point-filtered sprites in 3 sand tones with dithered edges; they never scale and are snapped to the art grid.
    - **Back** layer at order 3, beneath the units.
    - **Front** layer at order 11. A per-tile density field (full off the grid, 0.6 on High Ground, 0.3 on walkable tiles, squared for the front layer) keeps the enemy lane readable.
  - **Wind:** 1 px wind streaks fly above the units at order 24, and sand grains hop along the ground at order 3.
  - **Gusts:** the wind cycles between a breeze (8–16 s) and gusts (4–8 s), with a 30% chance of a short lull afterwards. Gusts arrive fast and die down slowly.
  - Gust strength drives cloud speed and thickness, streak and grain counts, and a warm haze at order 2. A `SpriteMask` clips everything to the map art. Runs on scaled time.
- **Stage 4 — factory heat** (`Stage4HeatHazard`): each wave adds 1 heat stack to deployed operators. At 5 stacks they burn for 15 damage/s. Only retreating ends the burn. Heat is stored on the `DraftCard` (`heatStacks`) and drops by 2 per round while benched. Merges keep the highest heat. `OperatorHeatBar` displays it.
- **Stage 5 — traffic** (`Stage5CarEncounter` / `Stage5TrafficCar`): from wave 6, 5 rolls per wave at 20% each. Each success queues a car on lane y=4 or y=5, 5–10 s apart. A car deals true damage to ground enemies and operators it passes: 40, plus 20 for every 2 tiles travelled. It ignores shield negation but pops bubbles. The first operator hit (if it survives) is stunned for 5 s. Stage 5 also uses Hole tiles (§1.3).
- **Stage 6 — acid rain:** 3 physical damage to operators and enemies every 2 s while a wave runs, rain particles.
- The sandbox runs the environment of whichever map is selected — key environment checks on `StageBootstrapper.EnvironmentStageId`, not `stageData.stageId` (which is `SANDBOX` there).
- Hazards must respect `IsCombatPaused` (§7.3): no hazard damage, drops or car spawns outside `WaveActive`.

### 7.2 Wave Design Rules

- Each wave is harder than the last, with a **spike every 5 waves**; the **final wave is the hardest**.
- Wave 1 must be beatable by a single 1★ operator (the first draft pick). Each stage starts slightly harder than the previous one (`StageData.extraStartEnemies`).
- Difficulty is weighed by **threat**, not raw HP: speed, air (only ranged operators/casters can hit it), DEF/RES and enemy ATK. Basic enemies count less (×0.7) because AoE, multi-block and healing handle crowds; basic enemies per wave are capped and extra budget buys elites.
- From wave 11, waves are **themed** (`WaveCurveBuilder.WaveTheme`): Standard, Rush, Air Raid, Armored Push, Air & Armor, Blitz, Siege. The final wave is tanks + air + rusher packs.
- Enemies arrive in packs / short windows (big waves within ~10–18 s), split across **all** spawn points from wave 1.
- Regenerated waves must be saved into `Assets/Resources/Stages/*.asset` — the Wave Planner / rebalance menu items are editor helpers and change nothing until run.

### 7.3 Combat Hooks (`OperatorBase` / `EnemyBase`)

- `EnemyBase.VisualPosition` — centre of the visible sprite. Aim visuals (projectiles, swipes, damage numbers, health bars) at it, not `transform.position`; game logic (range, grid cells) still uses `transform.position`.
- **Projectile origins:** shots start at the unit's `ProjectileOrigin` (`OperatorBase` / `EnemyBase`), never at `transform.position` (the tile centre).
  - It reads `OperatorData.projectileOrigin` / `EnemyData.projectileOrigin`: an offset in world units from the sprite's centre, measured on the art as drawn, and mirrored with the sprite. Operator art faces **left** (flipped only when facing Right). Enemy art faces **right** (flipped when walking left). For enemies the centre is the visible `EnemyVisual`, so flyers raised by `visualOffset` shoot from their body, not the ground below. Zero = sprite centre.
  - Current points: Basurocket's launcher front, Deadeye's eyes, Proxishot's leading eye-light, Pyrolite's head flame, Resicochet's floating charge orb, Progeny's leading heart, Bubblets' front mouth, Ignisaur's mouth, Echosquire's sword, Shadeslice's raised blade, Sand Poncho's sleeve cannon, Smog's chimney, Hextension's head, Skyshard's glass body, Talon Rat's talons (its swipe), THE ERADICATOR's arm cannon (front of the gun, between its two barrels).
  - The offset is in the sprite's own units and is multiplied by the unit's scale, so THE ERADICATOR's `(0.47, 0)` lands on the muzzle of its 2× sprite. (Its old `launcherOffset` was measured from the path position, ignoring the scale and `visualOffset`.)
  - Units with their own muzzles keep them: Askalaser (`muzzleOffset`), Roboflame (`flameOriginOffset`), Hextension's wire (`wireOriginOffset`). They have no `visualOffset`, so these match the sprite. Castiel's laser starts at its crystal (the sprite centre).
  - A new shooting unit sets `projectileOrigin` on its data asset; no code change.
- **Melee hits:** `MeleeSwipeVisual.Play(attacker, target, color, size, MeleeStyle)` draws pixel-art hits with pooled `PixelParticles` (no GameObject per hit). The effect is centred on the target; the attacker only sets the direction. A size of 1.5 or more draws a heavier version (slashes get a second echo arc). Styles:

  | Style | Look | Used by |
  |---|---|---|
  | Slash (default) | crescent swept across the target, bright edge leading | Guards, Specialists, Stag-ger, Echosquire (1.8, heavy) |
  | FlameSlash | double arc trailing fire, small fireball on the hit, spray of embers | Ignisaur's own-tile slash (size 1.35) |
  | ScrapSlash | double arc, impact ring, shower of falling sparks, junk knocked loose (rust, steel, paint, plastic) | Scrapper (size 1.3) |
  | Claw | three raking marks | enemy melee, Talon Rat |
  | Bash | impact ring, white flash, sparks, dust, impact streaks | Defenders, tank enemies |
  | Spikes | spines bursting out of the target | Thornchin, Spoilcupine |
  | Smash | huge thick double arc swung into the target, then (0.07 s later, as it lands) a white flash, shockwave, debris and dust | THE ERADICATOR's melee (size 1.4) |
  | Impact | flash, falling sparks, dust | Stage 2 falling items |

  - `Edge()` brightens the colour for the cutting edge, so near-black colours still read.
  - A Guard picks its basic-attack effect by overriding `GuardOperator.MeleeSlashStyle` / `MeleeSlashSize` (Scrapper does).
  - **Shadeslice's thrust** (`MeleeSwipeVisual.PlayStab`, the Guard isolation strike) keeps its original crimson line-and-glow look in `MeleeStabVisual` (LineRenderers, one GameObject per stab). The team preferred it to a pixel version.
- **Hit reactions** (`HitReaction.Play`, called from `EnemyBase.TakeDamage` and `OperatorBase.TakeDamage` / `TakeTrueDamage` next to the damage numbers):
  - The unit's sprite glows **white** for 0.12 s (`FlashOpacity` 0.5, `FlashColor`): a `HitFlashOverlay` child drawn with `Resources/Shaders/SpriteFlash.shader` (`TrashTD/SpriteFlash`, a solid-colour silhouette). Only the root sprite and the `EnemyVisual` child flash; auras and bubbles don't.
  - It **shakes** side to side for 0.16 s, up to 2.5 art pixels, snapped to whole pixels. The offset is added in `LateUpdate` and removed in `Update`, which runs first (`[DefaultExecutionOrder(-10000)]`), so movement, range, targeting and projectile origins always see the true position.
  - An **impact burst** (white star and sparks; heavy hits add a big flash and a ring) pops on the sprite. Bursts don't repeat within 0.08 s, so rapid laser ticks don't stack them.
  - Strength = damage ÷ max HP, full at 12%. Hits under 2% of max HP (acid rain, burning, trap ticks) don't react; those keep their own coloured flashes. Play mode only, so EditMode tests never create one.
- `CombatProjectileVisual.Fire(start, EnemyBase target, ...)` follows a moving target — prefer it over the fixed-position overload.
- **Combat effects are pixel art** at the stage-art scale (32 texels per unit, point-filtered, positions snapped to that pixel grid).
  - `CombatFxArt` generates the sprites, shaded from one colour.
  - `PixelParticles` is a pooled particle system that runs on scaled time.
  - `CombatFx` holds reusable effects (sparks, flash, smoke, explosion, crosses, droplets, pop).
- **Projectiles:** pass a `ProjectileStyle` as the last argument of `Fire`. The default is `Bolt`; the `size`/`trailWidth`/`burstOnImpact` arguments no longer change the look. Current styles:

  | Style | Used by |
  |---|---|
  | Bullet | snipers, Deadeye |
  | Pellet | Proxishot (via `FireShotgunSpread`, one muzzle blast) |
  | ArtsOrb | casters, Specialist, Resicochet, Askalaser |
  | FireOrb | Ignisaur |
  | Frost | Chillpath / chill casters |
  | Rocket | Basurocket, THE ERADICATOR |
  | HeavyRocket | THE ERADICATOR's every 4th rocket (bigger body, glowing halo, fiery exhaust) |
  | Quill | Thornchin |
  | Heal | Progeny |
  | Bubble | Bubblets |
  | Sludge | enemy shots |
  | Glass | Skyshard: a see-through glass shard that glints in flight (`CombatFxArt.GlassShard`); the shatter is the AoE |
  | Sand | `FireSand` |
  | Bolt | everything else |

  - Lobbed styles (Sludge, Sand, Heal, Bubble) arc visually only, so `onImpact` timing is unchanged.
- **AoE:** `AoeBlastVisual.PlayCircle` / `PlayCells` always draw the exact area hit: a dithered fill with a crisp rim (cells get corner brackets and ripple out from the centre), plus a shockwave for circles. Pass an `AoeStyle`:

  | Style | Used by |
  |---|---|
  | Explosion | Exploding Bug, Robozombie |
  | JunkRocket | Basurocket: one bright yellow-orange fireball and a burst of colourful scrap (`CombatFx.JunkRocketBlast`) |
  | HeavyOrdnance | THE ERADICATOR: crimson fireball, shrapnel, black smoke column, second shockwave, scorch mark (`CombatFx.HeavyOrdnanceBlast`) |
  | Arts | caster splash |
  | Heal | Mossmo |
  | Bubble | Bubblets' shield pop and splash |
  | Glass | Skyshard's blast: white flash, glinting glass fragments thrown out and falling, sparkles (`CombatFx.GlassShatter`) |
  | Acid | corrosive splash (no current user) |
  | Pulse | default |

  - The area fill's alpha is capped at 0.75 so units stay readable.
- **Sorting orders:**

  | Layer | Order |
  |---|---|
  | AoE area | 25 |
  | Shockwave | 26 |
  | Explosions | 30 |
  | Trails | 38 |
  | Orb halo | 39 |
  | Projectiles | 40 |
  | Impacts | 41–42 |
- `OperatorBase.IgnoresMissMechanic`, `IsCamouflaged`, `IsTargetable` — enemies skip operators that aren't targetable.
- **Nothing happens between waves.** `OperatorBase.IsCombatPaused` is true in every phase except `WaveActive` (Card Pick and Preparation; never in Sandbox). While paused, operators take no damage of any kind (enemy, trap, weather), receive no healing, gain/lose no shields, and run no attacks, heals or abilities. Any operator with its own `Update`/`TakeDamage` override must respect it.
- **True damage:** `DamageType.True` ignores DEF and RES (minimum 1). Hazards use `OperatorBase.TakeTrueDamage(dmg, ignoreShieldNegation, canDodge)` / `EnemyBase.TakeTrueDamage(dmg)`, not the ATK-based formula. Operator overrides of damage handling must also override `TakeTrueDamage`.
- **Sluggish:** `EnemyBase.ApplySluggish(duration, speedMult, attackIntervalMult)`. Resicochet uses 3 s, ×0.6 move speed and ×1.5 attack interval. It stacks multiplicatively with chill slow, and repeat applications keep the strongest values and the longest timer. Enemy attack timing must read `EnemyBase.GetEffectiveAttackInterval()` (stage modifier + sluggish), not `data.attackInterval`.
- **Stun:** `OperatorBase.ApplyStun(duration)` releases blocked enemies. While stunned, `GetBlockCount()` returns 0 and the operator can't attack. Use `GetBlockCount()`, not `data.blockCount`.
- **Block weight:** each blocked enemy takes `EnemyData.blockWeight` slots (THE ERADICATOR takes 2). `GetCurrentBlockCount()` returns the slots in use, not the number of enemies, and `OperatorBase.GetBlockWeight(enemy)` gives one enemy's weight.
- **Bind:** `OperatorBase.ApplyBind(duration, source)` / `RemoveBind()`. Bind is a separate status from Stun, with its own timer: it releases blocked enemies, makes `GetBlockCount()` return 0, and stops attacks, blocking and abilities (charges, heals, Mossmo's reactive heal). The operator is tinted purple and shows "BOUND!".
  - Any operator with its own `Update`, attack or ability code must check `isBound` as well as `isStunned`. `BlockingSystem.CanBlock` already checks it.
- **Enemy targetability:** `EnemyBase.CanBeTargetedBy(OperatorPosition)` is virtual and returns true by default (Hextension overrides it). `EnemyBase.CanReceiveDamageFrom(OperatorBase)` is also virtual (Gooelist overrides it). `CanAttackTarget` and `TryTakeAttackDamage` both check it.
- **DEF reads:** `EnemyBase.CurrentDEF` and `OperatorBase.CurrentDEF` are virtual. Damage code must read `CurrentDEF`, not the `currentDEF` field, so modifiers such as the Gooelist shred apply. Subclass overrides should build on `base.CurrentDEF`.
  - `OperatorBase.CanAttackTarget` (now public) and `EnemyManager.GetEnemiesInCells` / `GetEnemiesInRadius` (the overloads that take an `OperatorPosition`) respect it.
  - Custom targeting loops should filter their candidates through `CanAttackTarget`, as Sniper and Caster do.
- **Sprite layering (all stages):** sorting layers, back to front: `Ground` (map art, tiles, ground weather, scorch marks), `Units` (operators and enemies), `Default` (everything else). Names and helpers are in `GridSorting`.
  - `GridSpriteSorter` (added by `StageBootstrapper`) puts a `SortingGroup` on every deployed operator and active enemy and sets its order each frame. Tiles are counted from the top-left tile (0, 0), x rightward and y downward (grid y is flipped: row = `Height - 1 - gridY`). Depth = x + y, and deeper tiles draw on top. Units on the same tile draw in arrival order, the first to arrive on top (`SlotsPerTile` = 16).
  - An enemy's tile switches when its `transform.position` crosses into the next tile. Off-grid positions clamp to the edge.
  - Flyers use the same rule in their own band (`AirBandStart` = 10000), above every ground unit. Their shadow is a child sprite, so it draws with the flyer.
  - Child sprites of a unit (enemy visual, Guard aura, burn/acid flashes, bubble, bind wraps) move with the unit's group. Overlays parented to a unit that must stay above all units (health bar, heat bar, upgrade badge) call `GridSorting.SortAboveUnits`.
  - Anything new drawn beneath units must use `GridSorting.PlaceOnGround` (or `PixelParticle.OnGround`). A Default-layer renderer at a low order now draws above every unit.
  - Stage 5 cars, Stage 2 falling items, the foreground art strip, FX and floating text stay on `Default`, above all units.
- **Enemy animation:** `EnemyBase.UpdateSpriteAnimation` / `ApplyAnimationFrame` / `UpdateFacing` are `protected virtual`, so an enemy with several animation rows (Askalaser) can swap frame sets.
- **Custom enemy behaviour:** an enemy with its own `Update` should start with `if (!TickStatusEffects(Time.deltaTime)) return;`. That runs the hole stay, animation, trap damage, chill/stun timers and pushback, and returns false when the enemy can't act.
  - Then use `HoldBlockedPosition`, `GetPathForwardDirection`, `MoveAlongPath` (or `MoveAlongPathBy(distance)` for a custom pace) and `CurrentGrid` as needed. Roboflame and THE ERADICATOR put their per-frame logic in a public `Step(deltaTime)` so EditMode tests can drive it.
- **Ramping lasers** (`LightLaserVisual`, used by Castiel and Askalaser): a pixel-art beam built from three stretched 1-texel layers (core, body, glow), snapped to whole art pixels. `Fire(source, target, LaserPalette, tickInterval, onTick)`.
  - **Driving it:** the owner calls `SetStage(0–5)` as its damage ramps (one stage per step: 5/15/30/80/100/150) and `OnDamageTick()` on every hit.
  - **Stages:** each stage widens and brightens the beam (core 1→3 px, body 2→7 px, glow 0→14 px). Stage 1 adds the glow. Stage 2 makes the beam pulse. Stage 3 streams motes along it, with sparkles (Holy) or crackling arcs (Crimson). Stage 4 adds a width flicker and a halo ring (Holy) or heat smoke (Crimson) at the impact. Stage 5 adds a burst on every tick.
  - **Ends and pulses (every stage, scaled by it):**
    - Release point: a glowing orb with a hot core (`MuzzleGlowPx` / `MuzzleCorePx`), with energy motes streaming into it.
    - Impact: a splash of light with a twinkling star flare (`ImpactGlowPx` / `ImpactCorePx` / `FlarePx`, built from `CombatFxArt.GlowDisc` / `Disc` / `Flare`), with sparks spraying back toward the source. From stage 1 it also throws off rising motes of light (Holy) or molten drips (Crimson).
    - Both ends throb by a pixel. Bright pulses run down the beam, faster each stage, with a second pulse from stage 3.
  - **Feedback:** a stage-up flashes and pops both ends and sends a shockwave out from the target. Six pips above the source show the current stage.
  - Castiel's beam shows the power of its *next* tick: it steps up right after the tick that crosses a 1.5 s mark. Askalaser's steps up continuously with its lock time (`GetLaserStageForDuration`).
- **Heat from enemies:** `Stage4HeatHazard.AddHeatStack(op)` is public. Enemy fire attacks call it only when `StageCombatModifiers.IsHeatHazardActive` is true (Stage 4, or the sandbox on the Stage 4 map).
- **Blocking position:** a blocked enemy stops on the boundary in front of its blocker (`EnemyBase.BlockedPosition`). Range checks for blocked enemies count both the blocker's tile and the adjacent path tile. Defenders also attack enemies in their range pattern when they aren't blocking.
- Skip enemies with `IsInsideHole` in any targeting, blocking or status code.
- Enemy attack timers are "time since last attack", capped at one interval. Ground melee enemies spawn ready and keep charging while walking, so they **hit their blocker immediately on contact**; air combat enemies and Sand Hulk's frontal slam work the same way. Enemy casters keep their wind-up before the first shot.

### 7.4 Audio

- **`Assets/_Project/Audio/SFX_GUIDE.md` is the list of every sound file name and folder.** Update it whenever you add a sound trigger.
- Per-unit clips load from `Assets/_Project/Audio/Resources/SFX/Operators/<Class>/<Name>/` (and `SFX/Enemies/<Archetype>/<Name>/`) via `UnitSfx`. Every operator and enemy needs a folder there (with a `.keep` so git tracks it).
- Operators should call `PlayAttackSound()` / `PlayImpactSound()` so their own clips play, with the shared damage-type sound as fallback. Don't call `AudioManager.PlaySfx` directly for attacks without trying `UnitSfx.Play(data, UnitSfxEvent.Attack)` first. An enemy's `TryTakeAttackDamage(dmg, type, this)` plays the attacker's Impact. Pass `this`.
- Special triggers call `PlayAbilitySound()`. When an operator has more than one trigger, use `PlayAbilitySound("Name")` (file `Ability_Name`, falls back to `Ability`).
- Shared sounds are `SfxId` values mapped to files in `AudioManager.SfxPaths`, in `SFX/UI`, `Gameplay`, `Combat`, `Tiles` and `Stages/StageNN`. Add a new shared sound there, never as a loose file in `SFX/`. Stage ambience is `SFX/Stages/StageNN/Ambience` for any stage (`AudioManager.PlayStageAmbience`). Stage music is `Music/Stages/StageNN`, falling back to `Music/GameplayMusic` (`AudioManager.PlayStageMusic`).
- Per-clip SFX volume is set in `Audio/Resources/SfxVolumeSettings.asset` (Inspector), applied in `AudioManager.PlaySfx`. Every sound plays on its own pooled voice; `PlaySfx` returns it so it can be stopped with `StopSfx`. An operator's/enemy's running Attack sound is cut when its Impact plays (not within 0.1 s, so melee swings survive).
- Sounds tied to the game state (victory, defeat, wave cleared, card upgrade, deploy/retreat, deaths) are raised from events in `AudioManager`, not from UI code.

### 7.5 UI Kit

- Pixel-art UI sprites live in `Assets/Resources/UI/` by category: `panels`, `buttons`, `icons`, `bars`, `cards`, `controls`, `decor`, `minis`. `ui_kit_atlas.json` lists each sprite's rect and category. No code reads it; it is reference only.
- **Class icons:** `GameplayHUDUI.GetClassIconSprite(OperatorClass)` loads the 6-slice sheet `class-icons.png` (`class-icons_0`–`5` = Sniper, Medic, Caster, Defender, Guard, Specialist). Builds load `Resources/UI/icons/class-icons`; the Editor prefers the copy in `_Project/Art/`. Keep the two copies identical. Icons are drawn white on a clear badge (no class-colored background) in the deck, draft cards, Framiliar copy menu and detail panels. In the detail panels the icon sits left of the class / position / damage chips.
- UI is still built in code (uGUI). Style it through the helpers in `GameplayHUDUI`, not flat `Image.color` fills:
  - `ApplyButtonContainer(button, ButtonContainerType)`: Primary, Secondary, Dark, Danger, Warning, and the `Icon*` variants (`UI/buttons/btn_<type>_normal`).
  - `ApplyPanelContainer(go, PanelContainerType, tint?)`: Dark, Metal, Glass, Modal, Rust, Teal, Tooltip (`UI/panels/panel_<type>`). Modal = Metal panel plus hazard-stripe decor.
  - Both set `Image.Type.Sliced` and cache the sprite. If a panel sprite has no 9-slice border, a per-type border is applied at load time, so set borders in the importer for buttons. If the sprite is missing, the old flat colour stays.
- **Bottom-right panel** (`Resources/UI/Bottom Right Panel BG`, 3 frames of 140×113): frame `_0`/`_1`/`_2` = 1×/2×/3× game speed (the gauge needle). `GameplayHUDUI.CreateBottomRightPanel` builds it as a child of the deck bar, at the Deck BG's pixel scale. The speed buttons, squad count and Start Wave button sit on the sprite's screens, positioned in sprite pixels (`PanelPx`). Changing speed crossfades the frames over about 0.2 s on unscaled time. HUD animations must use unscaled time so they don't speed up at 2×/3× or freeze while paused.
- Keep UI textures on Point filtering (pixel art). Add new UI sprites under the matching `Resources/UI/<category>/` folder.
- **Draft range preview:** the card-pick details panel draws each operator's `rangePattern` as a tile grid facing right (`CardDraftOverlayUI.UpdateRangePreview`). It uses the placement-preview colours: cyan for the operator's own tile, green for tiles it can hit. It reads the same data as combat, so never hand-draw or screenshot range previews. A kit that reaches beyond `rangePattern` (e.g. Basurocket's splash) belongs in the ability text.
- **Settings panel** (`AudioSettingsUI`, shared by the main menu and the pause menu) uses the green terminal style: a full-screen dimmer, a 760×500 card with corner brackets, a header bar, `// SECTION` headers, slider rows with % readouts (each slider has a transparent full-height hit area, so pressing anywhere on the bar jumps the handle there), a 3-way CRT style selector and CLOSE. It pops in on unscaled time, and Esc closes it on both screens. Layout is in card pixels from the top-left (`PlaceTopLeft`). PressStart2P glyphs are as wide as the font size, so size text boxes from the longest string, or the text gets clipped. Only the button that opens the panel still uses `btn_dark_normal`; the creating screen restyles it. Prefer the `GameplayHUDUI` helpers for new UI.
- **Top panel** (`Resources/UI/Top Panel BG`, 640×30 frames drawn at 3×, stretched across the bar) sits behind the top HUD bar. Its dark screens, in frame pixels (inclusive): x 3–20 (pause button), x 26–77 / y 4–24 (phase), x 113–168 / y 6–23 (wave), x 300–334 / y 6–23 (hostiles), x 591–636 / y 6–23 (lives).
  - **Screens:** each readout is a `CreateTopScreen` rect anchored to its screen as a fraction of the bar (`TopArtWidth` / `TopArtHeight`), so it stays on the art at any aspect ratio. Never position top-bar content in fixed pixels from an edge.
  - **Look:** labelled terminal readouts, with a small dim caps label (`CreateScreenLabel`) and a bright value (best-fit pixel font). Each screen gets tiled scanlines (`GetScanlineSprite`, 1 dark line per art row) and a faint glow in the phase colour.
  - **Phase:** PHASE / CARD PICK (amber), PREPARATION (blue), INVADING (red), QA SANDBOX. A status light blinks in the phase colour, faster during a wave.
  - **Wave:** WAVE `07/20` during a wave, NEXT `08/20` between waves (the upcoming wave, `GameManager.GetCurrentWaveIndex() + 1`, same as the draft screen).
  - **Wave progress:** `bar_frame` (9-sliced) with a tiled `bar_fill_*` at 2× on the wave screen's bottom row. The fill sprite follows the phase: yellow = Card Pick, blue = Preparation, red = Wave Active (`SetWaveProgressFillSprite`).
  - **Hostiles:** HOSTILES shows enemies on the field, plus amber `+N` still to spawn (`WaveManager.PendingSpawnCount`). Between waves, with the field empty, it switches to INCOMING and the next wave's size (`WaveManager.CountEnemies(PeekNextWave())`).
  - **Lives:** `tube_frame` + tiled `tube_liquid` + `tube_surface` at 2×, built upright and rotated −90° so it reads horizontally. Liquid = current/max lives and drains toward the left. The count (`18/20`) sits on top of the tube: white, amber at ≤50%, red at ≤25%. The frame flashes red on a lost life. In Sandbox the tube is hidden and "LIVES LOCKED" shows instead.
  - **Next-wave preview:** a Dark-panel tab hanging under the bar, centred under the hostiles screen, shown in Card Pick and Preparation (not Sandbox). It reads "NEXT WAVE 08" ("FINAL WAVE" in red), then each enemy type in the wave with its first sprite frame and count (`x8`), in spawn-list order. Up to 6 types, then `+N`. It doesn't take clicks.
  - Load whole UI textures with `GameplayHUDUI.LoadWholeTextureSprite(path, border)`: Point-filtered, ignores importer slicing, cached. Draw pixel art at whole-number scales (`pixelsPerUnitMultiplier = 1 / scale` for Sliced/Tiled images).
- **Pause menu** (`GameplayHUDUI.CreatePausePanel`): `Resources/UI/panels/pause menu panel bg.png` (256×256) at 2×, grown to 256×290 by `BuildPauseCardArt`. It draws the art in three strips and repeats the 34 px band at art y 146–180 (one LED plus one slot), so the body screen has four indicator rows (art y 129 / 163 / 197 / 231).
  - The title sits in the header screen (art y 37–101). The body screen (y 104–251) holds: row 1 Resume; row 2 Retry plus the square Settings button (`PauseSettingsX`); row 3 INTEL (Reclaimers' Intel); row 4 Exit to Menu.
  - Settings and INTEL are added from `Awake` by `AudioSettingsUI.Create` and `ReclaimersIntelUI.Create`. Give every pause button its own slot: Settings used to sit on top of Retry and hid it.
- **Gameplay camera** (`GameplayCameraController`, added to the main camera by `StageBootstrapper.CenterCameraOnGrid`, which passes the home framing and the map-art bounds). It runs on unscaled time.
  - **Fully zoomed out (1×): look-around.** The camera drifts toward the cursor, smoothly, with a dead zone in the middle of the screen. The drift is only about 3.5% of the view height, enough to peek at edges under the HUD bars. A held touch acts as the cursor. With the cursor on the top bar, the deck bar (including the taller bottom-right speed panel) or outside the window, the view glides slowly back to the default centre (`ReturnSharpness`). It holds still while a mouse button is held, so placing and facing don't drift.
  - **Zoomed in: drag navigation, like the stage-select map.** Drag to pan, with inertia after release (a pause before letting go cancels the fling); the view stays within the map art.
    - A left-drag starts only from the map (not UI) while no card is being placed (`StageBootstrapper.IsPlacementActive`), and only after 8 px of travel, so clicks still select operators and deploy. Right and middle drags always pan.
  - **Zoom:** the mouse wheel zooms from 1× (home) to 2.25×, toward the cursor once zoomed in; two-finger pinch on touch screens (untested on devices). The zoom keeps the playable area between the HUD bars centred.
  - **No input** while paused, during the environment briefing, while the draft screen is showing (`CardDraftOverlayUI.IsShowing`), outside the Playing state, or with the pointer over UI (wheel, and starting a drag).
  - **Shake:** `Shake(strength, duration)`. A lost life shakes 0.2 world units (0.45 on the last life).
  - Never set the main camera's position or size elsewhere; go through the controller.
- **Surveillance feed** (`SurveillanceOverlayUI`, the first child of the HUD canvas so everything else draws over it; it never takes clicks): dressing over the playable area between the bars, in the main menu viewfinder's style.
  - **Frame:** corner brackets and a soft vignette. There is no centre reticle and no rolling band; both were removed on request.
  - **Readouts:** top-left REC + `CAM 03 // MAP NAME`; top-right the real date/time and the mission clock `T+ hh:mm:ss`; bottom-left signal bars and `ZOOM 1.00x`. The bottom-right corner is covered by the speed panel.
  - **Life lost** (`GameplayHUDUI.OnLifeLost`): screen shake, the red danger vignette, and `PlayInterference`: noise tear bars, red brackets, dropped signal and a flashing "SIGNAL INTERFERENCE" caption. A red flash and sparks also burst where the enemy breached (`HandleEnemyBreachFx`).
- **Results screen** (`StageResultsUI`, built in code, canvas order 300, unscaled time): a "MISSION DEBRIEF" card in the terminal style, green for victory and red for defeat. The header reads `STAGE 03 // MAP NAME // DIFFICULTY`.
  - **Victory:** STAGE CLEARED types on behind a scan sweep. Three stars stamp in with their rule (CLEARED / LIVES 50%+ / NO LEAKS, matching `GameManager.CalculateStars`); stars that were earned burst into pixel sparks.
  - **Defeat:** SIGNAL LOST with static and recurring glitch bursts. The subtitle gives the reason (all lives lost / time limit). An after-action report shows the wave reached, hostiles still in play (`ActiveEnemyCount + PendingSpawnCount`) and the enemy that leaked most.
  - **Layout** (960×548 card, constants in `StageResultsUI`): header, title, subtitle with a short accent rule, then one framed panel. The panel holds either the stars (with their rule pills) or the after-action report. Under it is the button row, then the key hint. The panel and the button row share one content column (48 px side margins), with 24–32 px vertical gaps. There are no stat cards. Leaks per enemy type are counted from `EnemyManager.OnEnemyReachedExit` for the report.
  - **Buttons:** NEXT STAGE (victory, when `Stages/STAGE_{n+1}` exists; same difficulty), RETRY, STAGE SELECT. The primary button is filled and pulses. Any click or key skips the intro; then ENTER presses the primary button and R retries.
- **Phase banner** (`GameplayHUDUI.CreatePhaseBanner`): an alert band at 62% screen height. It snaps open, holds about 1.7 s, then collapses, with diagonal stripes marching top and bottom in the banner colour. Its timing runs on unscaled time × `GameManager.GameSpeed`, so at 2×/3× it plays and clears 2–3× faster. It never takes clicks.
  - **Wave start** (`ShowWaveBanner`): "INVADING" (no wave number), "38 HOSTILES INBOUND" and the wave's enemy types (first frame + count, up to 6), with flashing siren icons at both ends. The final wave is red ("FINAL WAVE - 38 HOSTILES INBOUND") with a glitching cyan ghost.
  - **Preparation** (`ShowPhaseBanner`): blue "PREPARATION / DEPLOY YOUR SQUAD", with no sirens or icons.
- **Reclaimers' Intel** (`ReclaimersIntelUI`): an almanac of every operator and enemy, opened from main-menu row 02 (INTEL) and pause row 3. Green terminal style, unscaled time. Esc steps back (gallery → dossier picker → closed); both screens forward Esc to `HandleBack()`.
  - **Dossier picker:** OPERATORS (Coalesce's first frame) and ENEMIES (Toxic Rusher / Drum Rat's first frame, red and hazard-striped). Arrows plus Enter work too.
  - **Gallery:** filter tabs (class or archetype) and a 4-column portrait grid on the left; arrows browse. On the right is the selected file: an animated sprite at a whole-number scale; chips (class / position / damage, or archetype / ground-air / damage / unblockable); rarity stars; the quote; HP/ATK/DEF/RES bars with a tier toggle (operators: base rarity to +2 upgrades via `GetScaled*`; enemies: EASY / NORMAL / HARD = difficulty levels 1–3; enemy RES doesn't scale); fact boxes (operators: BLOCK / ATK SPD / REDEPLOY, the recall cooldown in waves, laid out 3 across; enemies: SPEED / ATK SPD / BLOCK WT / LIFE COST, 4 across; `LayoutFacts`); a range grid (operator `rangePattern` facing right plus the talent pattern in amber; for enemies, a disc of `attackRange`, the 2.5-tile caster default, or `frontAreaAttackTiles`); and scrolling notes (skill/ability, field notes, and for enemies "Sighted at": the stages whose baked waves include them).
  - **Data, no hand-made lists:** units come from `UnitIntelRegistry` (`Resources/Intel/UnitIntelRegistry.asset`). `UnitIntelRegistryUpdater` (Editor) rebuilds it after any `.asset` import, delete or move and after script reloads, so a new OperatorData / EnemyData shows up automatically, builds included. Menu: *TRASH TD → Rebuild Reclaimers' Intel Registry*. In the Editor, the Intel scans the project directly. Commit the registry asset when it changes.
  - **Text lives on the data assets:** `OperatorData.intelQuote` / `intelLore` (the skill text is `skillDescription`) and `EnemyData.abilityDescription` / `intelQuote` / `intelLore`. Missing text shows a placeholder. Every current unit has text drafted 2026-10-27; it stays inside the team's world (NERC, Calder Basin, the stage names) and the team may rewrite it.
  - **Sprites:** `UnitSpriteFrames` resolves a unit's gameplay animation without instantiating the prefab. Enemies: `animationFrames`, else the prefab's `IEnemySpriteFrames`; the title parade uses the same helper. Operators: a prefab `IOperatorSpriteFrames` with assigned frames (Bulkhead, Deadeye), else the portrait sheet sliced by `OperatorSpriteAnimation.SliceSheet` / `ResolveFrameDuration`, the same code gameplay uses.
- **Path preview style** (`PathPreviewArt`, shared by both previews; change the look there, not in either preview):
  - **Routes** are reclamation-plant conveyor lanes: a dark channel with bright rims and ">" chevrons that march spawn → exit at Grunt speed (1 tile/s). The chevrons show which way enemies walk.
  - **Turns** get junction nodes; a **tracer blip** runs each route every few seconds.
  - **Spawns:** a hazard-striped breach frame (marching stripes) with a blinking warning sign, in the route's colour, tagged `SPAWN` or `SPAWN n` when there are several routes.
  - **Exits:** a green shield on a plate inside breathing lock-on brackets, tagged `EXIT`.
  - Markers have square radar pings and terminal-style labels on dark tags.
  - All art is generated white at 1 texel = 1 stage-art pixel, point-filtered, and tinted per route.
  - **Lanes:** 10 art px wide, narrowing to at least 4 px when routes share a path (`LaneWidthPx`, `LaneOffsetTiles`).
  - **Shared geometry:** both previews use `KeepCorners`, `OffsetIntoLane` and `PointAlong`.
  - **Animation:** grid fades in → routes draw in behind a scan head (0.9 s) → junctions and spawns pop in → exits lock on as the routes arrive.
- **In-game path preview** (`GameplayPathPreviewOverlay`, toggled by the HUD's PREVIEW PATH button, Esc closes it):
  - **Look:** draws the `PathPreviewArt` style in world space, using the same routes, colours and lanes as `StageMapPreview` (`ComputeRoutes`, `GetRouteColor`). The stage art dims slightly and a faint phosphor tile grid appears.
  - **Layering:** sorting orders 43–52, above units, the foreground art and weather. Unscaled time.
  - **Shader:** the lanes are LineRenderers using `Resources/Shaders/AnimatedPathDash.shader` ("TrashTD/PathDash"). URP's 2D sprite shader ignores texture tiling and offset, so a material on it can't scroll.
  - **Scrolling:** the lines use `LineTextureMode.Tile` (u = world units along the line), so the lane material's scale is `1 / (LanePeriodPx × art pixel)` and its offset scrolls the chevrons from spawn to exit. If the shader is missing, the lanes fall back to the sprite shader and stand still.
  - **Labels** are `TextMesh` in the pixel font, sized from the measured mesh.
- **Operator inspect label** shows name, rarity, ATK/HP and SHOW MORE. The skill text appears only in the SHOW MORE details panel. The panel also holds the Framiliar copy section (`CreateFramiliarCopySection`), which appears only when an uncopied Framiliar is selected.

### 7.6 Controls & Keybinds

The project uses the **New Input System only** (`activeInputHandler: 1`). Read keys from `Keyboard.current` / `Mouse.current`. Any `UnityEngine.Input` call must sit inside `#if ENABLE_LEGACY_INPUT_MANAGER`, or it throws at runtime.

| Context | Key | Action |
|---|---|---|
| Card draft (`CardDraftOverlayUI`) | 1 / 2 / 3 | Select card |
| | Enter | Confirm pick |
| | R | Reroll |
| | M | Minimize / restore (View Map & Deck) |
| Preparation, Sandbox, or Card Pick with the draft minimized | 1–8 (or numpad) | Quick-deploy that deck slot onto the tile under the cursor (`StageBootstrapper.TryQuickDeployFromDeck`). Ignored over UI, while paused, during the environment briefing, at the squad limit, or if the card is on cooldown |
| Placing an operator | WASD / arrow keys | Set facing |
| | Enter | Confirm placement |
| | Right-click | Cancel placement, or undeploy the selected operator |
| Gameplay | Esc | First match wins: close the developer picker → cancel placement → deselect operator → close audio settings → toggle pause |
| Main menu | Esc | Close settings or stage details, or return from stage select to the main menu |
| Stage select map | Scroll / double-click | Zoom toward the cursor (double-click at max zoom resets) |
| | Drag (from the map or a marker) | Pan, with inertia |
| | Hold and push the joystick (left panel) | Pan that way, faster the further it's pushed (zooms to 1.8× first if at 1×) |
| | + / − (also numpad) | Zoom about the screen centre (hold) |
| | R / Home | Reset the view |

- **Esc is handled once per frame.** Whatever handles it calls `GameplayHUDUI.ConsumeEscapeKey()`. Other handlers check `HasConsumedEscapeThisFrame` first. `GameplayHUDUI.HandleEscapeKey()` is the fallback that ends in pause/resume. Follow the same pattern for any new Esc-closable panel.
- Gameplay input (deploy hotkeys, draft keys) must do nothing while `GameplayHUDUI.IsPaused`.
- **Overlays above the HUD must hide while paused.** The draft overlay's canvas (sort order 200) sits above the HUD canvas (100) that holds the pause menu, so `CardDraftOverlayUI` disables its canvas while paused. Otherwise it stayed clickable, and confirming during pause added a card without leaving Card Pick, which allowed several picks. Confirm also only runs during an active Card Pick, once per draft (`pickConfirmed`).

### 7.7 Main Menu Stage Map

- **Game name:** the player-facing name is **Tiny Reclaimers TD** (formerly TRASH TD). So far it has only been changed on the title screen. The editor menus (`TRASH TD/...`), `productName` (`TRASH-TD`) and this file still use the old name.
- **Title screen** (`MainMenuController.BuildTitleScreen`, all built in code):
  - **`MainMenuBackdrop`** pans across each playable stage's art layers in gameplay order (background sheet, map, upper backgrounds, foreground, the same as `StageMapPreview`). The art is drawn at a whole-number scale and dimmed. It cross-fades to the next stage every 11 s. Size and pan are re-fit to the screen **every frame**, and the pan is stored as fractions of the overflow, so art edges never show at any resolution.
  - **Enemy parade:** small packs (at most 5 enemies on screen) walk left→right along a hazard-striped floor. Every enemy stays under 108 px and flyers fly at 64 px, so the parade stays clear of the menu column. Enemies play their walk cycle (`UnitSpriteFrames.TryGetEnemyAnimation`): `EnemyData.animationFrames`, otherwise the prefab's sheet animation via `IEnemySpriteFrames` (`Grunt/Rusher/TankSpriteAnimation`). Enemies with fewer than 2 frames are left out. The current flyers are a placeholder "+" and are excluded until they get sprite sheets.
  - It only uses art referenced by `Resources/Stages`, so it works in builds. Operator art isn't reachable from Resources, so operators aren't shown.
  - A camera viewfinder (corner brackets) frames the screen, with the version top-left and a blinking REC light plus `CAM NN // MAP NAME` caption top-right.
  - The title is a small hazard-yellow "TINY" over a large "RECLAIMERS" (two-step extruded shadow, occasional RGB-split glitch), with a tilted hazard-yellow "TD" sticker. Above the title, a boot line types out the number of playable stages.
  - Menu rows PLAY 01 / INTEL 02 / SETTINGS 03 / EXIT 04 are styled by `StyleMainMenuButton`: a caret and bracket frame show while a row is selected (`MenuButtonFeedback.IsHot`). **Hovering selects the row**, so mouse and keyboard share one highlight. INTEL is created by `ReclaimersIntelUI.Create` and SETTINGS by `AudioSettingsUI.Create`.
- Stage select is a Pip-Boy style map, built in code by `MainMenuController.BuildStageButtons`. It replaced the old stage grid. The background is `Art/Levels/Stage Selector Map.png`. Clicking a dot opens the stage details modal (difficulty selector, then play). A locked dot wobbles and plays the denied sound.
  - **Radar overlay** (`StageMapAnimOverlay`): a 12-frame sonar pulse (`Art/Levels/StageMap_Anim_Overlay.png`, `Animations/StageMap_Anim_Overlay.*`) stretched over the map art.
  - **Lore card:** hovering the title block shows `StageZoneLore` in a card under the title plate (`StageHeaderLoreHover` / `StageHeaderLoreChildTrigger`, in `MainMenuController.cs`).
- Each stage is a `StageMapNode` placed at a **normalized position** (0–1 across the map art, origin top-left) from `MainMenuController.StageNodePositions`. Index *i* is Stage *i*+1, so add an entry there when adding a stage. `StageMapLayout` (on the map art) rescales and repositions the nodes whenever the art resizes, so they stay in place at any 16:9 resolution.
- **Stage markers** (`StageMapNode.Build`, called from `MainMenuController.CreateStageMapNode`) are built entirely by the node.
  - **Marker:** a chunky pixel target made of procedural sprites drawn small and point-scaled: core badge with the number, a rotating dashed ring, two staggered radar pings, and 4 lock-on ticks.
  - **Callout card:** joined by an elbow leader line; shows `STAGE NN`, the map name and a status (ONLINE / SELECTED / LOCKED). It sits right of the marker, or left when the normalized x > 0.7. Card width comes from its longest line. Clicking the card opens the stage too.
  - **Animation** (unscaled time): `PlayIntro(delay)` runs on every map open (`PlayStageNodeIntros`, staggered by stage). The core pops in with a burst ring, the leader line draws, the card unfolds and the name types out. Idle: slow spin and pings. Lock-on (hover/focus): ticks close in, the spin speeds up, the core swells and the card brightens and nudges outward. Selected: yellow, faster pings and a breathing core. Locked: grey, static, with a padlock badge; a click wobbles and plays the denied sound.
  - **Hovering selects the marker** (EventSystem), so mouse and keyboard share one lock-on. `HoverChanged` follows that selection and drives the status ticker.
  - The node's `MenuButtonFeedback` only handles press, wobble and sounds (no hover scale or outline). The marker's visuals sit under a `Visual` root that `ApplyLayout` scales.
  - The pixel-art `Resources/StageNodes/Node_N_*.png` sprites are still unused.
- **Map navigation** (`StageMapNavigator` on the map container `StageMapContainer`; set up by `MainMenuController.SetUpMapNavigator`):
  - **Zoom and pan:** zoom runs 1–3×. The container is no longer stretched: it is centred and sized to viewport × zoom, offset by the pan. The pan is clamped so the map always covers the screen.
  - **Input:** scroll zooms toward the cursor; dragging pans with inertia. Drags that start on a marker bubble up to the navigator and cancel the click. See §7.6 for the keys. Arrows and WASD stay UI navigation between markers, and keyboard focus moving to an off-screen marker pans to it (`EnsureVisible`, from `OnStageMapNodeHoverChanged`).
  - **Reset and blocking:** the view resets each time stage select opens. Input is ignored while the dossier, settings or loading are open (`InputBlocked`).
  - **Marker size:** markers grow only as zoom^0.35. `StageMapLayout.ZoomCompensation` divides the marker scale.
  - **HUD** (top right, below `stageProgressText`, kept under the dossier in sibling order): a MAP OVERVIEW minimap (map art, stage dots, visible-area frame, cursor grid reference; click or drag to jump), a zoom bar ([−] 1.0X [+] [RESET]) and a controls hint.
  - Pointer positions go through `CrtDisplay.ScreenToPicture` and the canvas camera, like all pointer code.
  - `StageMapMinimapInput` (same file) forwards minimap presses.
- **Side panel** (`StageMapSidePanel`, created in `BuildDefaultUi`, wired to the navigator in `SetUpMapNavigator`):
  - Machine-panel art (`Resources/UI/panels/Stage Selector Map Panel.png`, 183×810 slice of a 1440×810 sheet laid out like the map art) is drawn down the left edge at full screen height. Its width follows the art's aspect (about 244 px at 1080). Children are placed in art pixels under a root scaled to the panel's height.
  - The joystick knob (`... Map Panel joystick.png`, rect 86,331 51×49 on the same sheet) sits in the green dial at art (111.5, 355.5). Moves snap to whole art pixels, and a dark copy left at the dial centre shows as the socket while the knob is tilted.
  - **It is a working stick.** An invisible grab area covers the dial (radius 40 art px, `StageMapStickInput` in the same file). Press there and push:
    - The knob follows the pointer (32 art px of pointer travel = full push, shown as 11 px of tilt).
    - The view travels that way through `StageMapNavigator.StickPan`, at up to 1200 canvas units/s. Speed is push^1.5 past a 0.12 dead zone.
    - Release stops the view (it eases to a stop) and the knob springs back.
    - `BeginStick` refuses while navigation input is blocked. At 1× it first zooms to 1.8×, because the map can't pan at 1×.
    - Grabbing plays the UI click (`Grabbed`). Hover lifts the knob 1 px, and a held knob is tinted darker.
  - **Not held:** the knob shows the navigation, driven by the navigator's `ViewCentre` and `Zoom`. It tilts up to 11 art px toward where the view is moving (drag, throw, minimap jump, zoom toward the cursor), and shrinks and darkens slightly while zooming in. It springs back with a small overshoot.
  - **Scroll wheel** (the ridged strip above the joystick, art x 72–96, rows 218–324 from the top): a 4-row ridge repeat taken from the art (`WheelPattern`). It is redrawn over the art as a 1×4 Repeat texture on a RawImage, and its uvRect rolls by whole art pixels: about 24 px per unit of ln(zoom), so one ridge per mouse-wheel notch. The ridges move up when zooming in and down when zooming out. It follows the smoothed zoom, so it eases to a stop. It is display only. If the art's wheel colours or position change, update `WheelRidges` / `WheelPattern`.
  - The panel slides in with the stage-select entrance. Apart from the dial, it doesn't catch the pointer, so dragging over it still pans the map.
  - Left-side stage-select UI clears the panel: the title block starts at `StageLeftEdge` (280) and the Back button at `StageBackButtonX` (150, clear of the slanted bottom). Keep new left-side elements right of the panel.
- **Locking is not progression.** A node is locked only if `IsStagePlayable(stageId)` is false (a hard-coded list of stage IDs). "X / 6 UNLOCKED" counts that list, not cleared stages. Don't add unlock-on-clear progression without asking Raim (see "Persistence model", §4).
- **Terminal styling** (stage selector and dossier): the `Terminal*` colours in `MainMenuController`, `AddCornerBrackets` for L-shaped corner frames, and `StyleTerminalButton`. `MenuButtonFeedback` owns each button's `Outline` as its hover glow, so resting borders are brackets, not outlines. The selector has a title plate, a blinking cursor, and a status ticker that types out the hovered stage (`StageMapNode.HoverChanged`).
- **Stage dossier** (the details modal, 1300×720): an animated `StageMapPreview` on the left, and stage info, stats, the difficulty picker, Back and Play on the right.
  - **Difficulty picker** (`BuildDifficultySelector` / `BuildDifficultySlider`): "> DIFFICULTY", then a stepped pixel slider made of flat blocks (outlined track, fill, three square notches, a blocky knob with a highlight row) with EASY / NORMAL / HARD under the notches. Fill, knob, notches and the selected label take the difficulty colour.
  - It's a Unity `Slider` (whole numbers 0–2) with a transparent hit area: press anywhere to jump to the nearest notch, drag, or use the arrow keys.
  - Under it, one framed 80 px strip shows `◆ NORMAL` (left 36%) and `WAVES 20 / LIVES 15` (right-aligned in the rest, so the two never touch), with the one-line blurb below. The picker replaced the three tall difficulty cards and the separate briefing plate.
  - **Column spacing** (`Details*` constants): the blocks are name, description (96 px), stats + divider, difficulty label + slider, info strip, and Back / Play. They sit an even 39 px apart (`DetailsGap`) down the 622 px column.
- **`StageMapPreview`** draws the stage art in UI with gameplay's tile math (`StageBootstrapper.SpawnVisualGridTiles`). A layer several map-widths wide is treated as an animation sheet (Stage 3's background). It shows one enemy route per spawn, chosen like `WaveManager.PrecomputePaths`: explicit `enemyPaths`, otherwise A* on a throwaway `GridManager`. Change both together if path rules change. Each route has its own colour (`StageMapPreview.GetRouteColor`, also used for its spawn marker and the legend) and its own lane, offset sideways and mitred at corners so shared tiles show routes side by side. Routes use the shared `PathPreviewArt` style (§7.5): each segment is a RawImage of the lane texture scrolling its uvRect, with junction nodes covering the overlap at corners. The legend's `[!]` / `[+]` tags stand for the spawn warning sign and the exit shield. Animation: boot scan reveal → phosphor tile grid → routes draw in behind a scan head → chevrons march and tracers run; marker pops and pings; CRT scanlines (`StageMapScanlines.SourceRows` ≈ height / 4).
- The dossier's texts use best-fit (`MainMenuController.FitText`) so they shrink instead of clipping. The selector's ticker plate is a **fixed 820 px** and never resizes. Text sits in a `RectMask2D` viewport. A message that doesn't fit types out with the line sliding to keep the cursor visible, holds 1.2 s, then loops as a seamless marquee (message + gap + message, `PrepareStageTickerScroll` / `UpdateStageTicker`).
- **CRT / old-TV look for the whole game** (`CrtDisplay`, `CrtScreen`, `Resources/Shaders/CrtScreen.shader` "TrashTD/UI/CRT"):
  - **`CrtDisplay`** is created before the first scene (`RuntimeInitializeOnLoadMethod`, DontDestroyOnLoad). Every frame it does three things:
    1. Points the scene cameras at a screen-sized game texture.
    2. Turns every Screen Space - Overlay canvas into Screen Space - Camera on its own UI camera, which renders into a separate transparent UI texture (sorting orders keep working).
    3. Draws both through the CRT shader with `CrtScreen.Television` (the shader layers UI over game, `_UiTex` / `_HasUi`) on a screen camera that renders last (depth 1000), so the picture is never a frame behind.
    - **Do not use a URP camera stack for this.** An earlier version stacked the UI camera on the scene camera; with the 2D Renderer the UI never drew (blue/frozen screen).
  - **Shader:** soft horizontal pixel blending; scanline beams that widen on bright pixels (~270 lines, `UpdateFrame` scales with resolution); a full-strength RGB slot mask (or aperture grille, `maskType`) that brightens each colour's own phosphor; halation glow; plus curvature, rounded bezel, fringing, grain, rolling bar, flicker, jitter, vignette and glare. Tune it in the `Television` / `Monitor` presets in `CrtScreen.cs`.
  - **Input rule (important):** the glass is curved, so the picture is not at the raw mouse position. UI raycasts are bent automatically (`CrtGraphicRaycaster` replaces each canvas's `GraphicRaycaster`). **Any code that uses a mouse or `PointerEventData` position with a camera or canvas must convert it first with `CrtDisplay.ScreenToPicture(...)`** (see `StageBootstrapper`, `DeckSlotDragHandler`). For screen ↔ canvas conversions, pass the canvas camera (`canvas.worldCamera`), not `null`, because canvases are no longer overlays.
  - **Finding a canvas** to build UI on: use `CrtDisplay.FindGameCanvas()`, not `FindFirstObjectByType<Canvas>()`, which could return the CRT's own canvas.
  - **Graphics settings** (Settings panel → GRAPHICS; `GameGraphicsSettings`, saved in PlayerPrefs, `Changed` event):
    - **CRT Style** (all styles go through `CrtDisplay` for the whole game; `CrtDisplay.GetActiveSettings`):
      - **Full TV** (default): `CrtScreen.Television`; the preview uses `Monitor`.
      - **Classic:** the first CRT version, kept exactly (`CrtScreen.ClassicTelevision`, `legacy = 1` selects the old shader maths `_Legacy`); the preview uses `ClassicMonitor`.
      - **Off:** no CRT anywhere (`CrtScreen.Clean`, passthrough). Identical to the pre-CRT game at 100% brightness.
    - **Brightness** (50–150%) scales the `CrtDisplay` pass in every style (`Settings.WithBrightnessScale`).
    - The settings panel class is still named `AudioSettingsUI` (Audio + Graphics sections).
  - **Monitor preview:** the dossier's map preview is its own small `CrtScreen.Monitor` (offscreen canvas + camera + RenderTexture). It has stronger curve and no scanlines or mask, because the TV supplies those and two line patterns would moiré.
  - `CrtScreen.Distort` / `IsOnScreen` must stay in sync with `Distort` / `ScreenMask` in the shader.
- `StageMapScanlines` (now unused by the menu; superseded by the CRT shader) is the old CRT overlay: scanlines, a rolling refresh band, a vignette and a faint flicker, all tunable in the Inspector. It builds its layers at runtime and runs on unscaled time.
- The map scripts' file names don't match their class names (`Stagemapnode.cs` → `StageMapNode`, `Stagemaplayout.cs` → `StageMapLayout`, `Stagemapscanlines.cs` → `StageMapScanlines`). That works because they are only added with `AddComponent`. Rename the files before attaching any of them in the Inspector.
- `MapSelectUI` (an older stage-select component that also uses `StageMapNode`) isn't used in any scene. Its `IsStageUnlocked` always returns true.
