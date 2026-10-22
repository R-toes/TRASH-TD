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
- On the draft screen, **"View Map & Deck"** (minimize, M) lets the player rearrange, discard **and deploy** deck cards during the pick, the same as in Preparation. Deploying frees a deck slot for the card being picked. Restore (M / RESUME) to pick. The rule lives in `StageBootstrapper.IsDeploymentAllowed` (Sandbox, Preparation, or Card Pick while `CardDraftOverlayUI.IsMinimized`) and `GameplayHUDUI.IsDeckDeployPhase`.
- **HP persists through retreat.** A retreated operator's exact HP is saved on its returned card (`DraftCard.savedHP`, −1 = full) and restored on redeploy (`OperatorBase.RestoreSavedHP`, called after `Initialize`). Nothing heals a benched card. Merging copies never heals: the upgraded card keeps the lowest HP fraction among them.
- At most **8 operators** can be deployed at once (`StageData.squadSizeLimit`; unlimited in Sandbox).
- **Deploying:** pick a deck card, click a tile, then hold and drag from the operator to set its facing. Release to confirm. Esc cancels. **Right-click** cancels placement or undeploys the selected operator. Keyboard shortcuts are listed in §7.6.
- **Preparation refund:** an operator deployed and retreated between waves (Card Pick or Preparation), before it has fought (`DeployedDuringCurrentPreparation && !HasFoughtInWave`), returns with **no recall cooldown**. Every other retreat uses the normal class cooldown. Always refunded in Sandbox.
- The draft overlay has card flip and shuffle animations. Card frame sprites exist in `Resources/UI/cards/` but are **not wired up yet**.

---

## 3. Data Architecture Mandate

Per GDD section 1.10 and the course's software-reuse objective:

- **Creature data** → `ScriptableObject` assets (`.asset` files), designer-editable without code changes
- **Enemy data** → `ScriptableObject` assets
- **Stage data** → Separate config files (ScriptableObjects or JSON) containing wave timing, spawn points, path nodes
- **Adding a new creature or enemy must not require modifying existing code** — only creating a new data asset and (optionally) a new prefab
- Architecture patterns (inheritance, interfaces, composition) are part of the assignment deliverable — favor reusable, extensible patterns over one-off implementations
- **Stage data lives in `Assets/Resources/Stages/*.asset`** — the single source of stage data.
- **Known deviation:** operators with a unique kit (e.g. Ignisaur, Proxishot, Chillpath, Shadeslice) currently need a subclass *and* a name check in `OperatorManager` (`ShouldReplaceWithSpecializedComponent` / `AddClassComponent`); 4-frame sprite sheets need a name check in `OperatorSpriteAnimation`; `OperatorData.GetTalentRangePattern` also checks for `"Shadeslice"` by name. Plain operators still need only a data asset + prefab. Prefer moving these name checks into `OperatorData` fields over adding more of them.
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

## 7. Current Implementation Notes (as of 2026-10-22)

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
- **Shadeslice** (Guard, reworked into `ShadesliceOperator`): 2.0× damage when no ally stands in its talent pattern (12-tile diamond, `OperatorData.ShadesliceTalentPattern`). After 5 s without being attacked, it regenerates 2.5% max HP per second.
- **Stag-ger** (Guard): hits every blocked enemy on its tile and in range ahead, including enemies blocked by allies. With nothing blocked, it hits one target. 20% dodge; a successful dodge gives +50% ATK on the next attack.
- **Drawgoo** (Defender): on death, draws 2 random cards. An upgraded Drawgoo instead draws 3★ cards: 1 card (60%) or 2 (40%) (`CardDraftSystem.DrawRandomCardsToDeck(count, targetRarity)`).
- **Mossmo** (Defender): doesn't attack. It **taunts** (`HasTaunt`): ranged enemies on a surrounding tile target it first. When it takes damage, it heals up to 2 operators in range, itself included.
- **Talent range overlay:** `OperatorData.talentRangePattern` (optional) is drawn in orange under the green attack range in the placement preview. Use `GetTalentRangePattern()`.

**Enemies (10):** Grunt (Sludge, Sand Plastic), Rusher (Toxic), Tank (Pollution, Sand Hulk), Caster (Sand Poncho, Smog), Air (Flying Melee, Flying Ranged, Acid Flyer). Acid Flyer and Smog Caster now have sprites but are **not yet used in waves** — confirm they're fully configured before adding them.

**Stages:** STAGE_01 through STAGE_06 (Stage 5 = "Boulevard"), plus STAGE_SANDBOX (development sandbox with multiple maps; cards can be deployed mid-wave and the squad limit is lifted).

**Stage environments** (each has its own component created by `StageBootstrapper`, and an environment briefing in `GameplayHUDUI`; the constants in each script are authoritative, and some doc comments are out of date):
- **Stage 2 — falling items** (`Stage2FallingItemsHazard`): every 10 s an item drops on a random operator (40%), deployable tile (40%) or enemy (20%). Glass: 2 true damage/s for 5 s. Metal: 15 true damage. Tools: 15 true damage + 2 s stun. Thornchin doesn't retaliate. The hit pops Bubblets' shield, triggers Mossmo's heal, and can be dodged by Stag-ger. Proxishot still gains camo. Sprites come from `StageData.fallingItemSprites`.
- **Stage 3 — sandstorm:** attack intervals ×0.95, 10% miss chance, wind/dust particles, animated background.
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
- `CombatProjectileVisual.Fire(start, EnemyBase target, ...)` follows a moving target — prefer it over the fixed-position overload.
- `OperatorBase.IgnoresMissMechanic`, `IsCamouflaged`, `IsTargetable` — enemies skip operators that aren't targetable.
- **Nothing happens between waves.** `OperatorBase.IsCombatPaused` is true in every phase except `WaveActive` (Card Pick and Preparation; never in Sandbox). While paused, operators take no damage of any kind (enemy, trap, weather), receive no healing, gain/lose no shields, and run no attacks, heals or abilities. Any operator with its own `Update`/`TakeDamage` override must respect it.
- **True damage:** `DamageType.True` ignores DEF and RES (minimum 1). Hazards use `OperatorBase.TakeTrueDamage(dmg, ignoreShieldNegation, canDodge)` / `EnemyBase.TakeTrueDamage(dmg)`, not the ATK-based formula. Operator overrides of damage handling must also override `TakeTrueDamage`.
- **Stun:** `OperatorBase.ApplyStun(duration)` releases blocked enemies. While stunned, `GetBlockCount()` returns 0 and the operator can't attack. Use `GetBlockCount()`, not `data.blockCount`.
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
- UI is still built in code (uGUI). Style it through the helpers in `GameplayHUDUI`, not flat `Image.color` fills:
  - `ApplyButtonContainer(button, ButtonContainerType)`: Primary, Secondary, Dark, Danger, Warning, and the `Icon*` variants (`UI/buttons/btn_<type>_normal`).
  - `ApplyPanelContainer(go, PanelContainerType, tint?)`: Dark, Metal, Glass, Modal, Rust, Teal, Tooltip (`UI/panels/panel_<type>`). Modal = Metal panel plus hazard-stripe decor.
  - Both set `Image.Type.Sliced` and cache the sprite. If a panel sprite has no 9-slice border, a per-type border is applied at load time, so set borders in the importer for buttons. If the sprite is missing, the old flat colour stays.
- **Bottom-right panel** (`Resources/UI/Bottom Right Panel BG`, 3 frames of 140×113): frame `_0`/`_1`/`_2` = 1×/2×/3× game speed (the gauge needle). `GameplayHUDUI.CreateBottomRightPanel` builds it as a child of the deck bar, at the Deck BG's pixel scale. The speed buttons, squad count and Start Wave button sit on the sprite's screens, positioned in sprite pixels (`PanelPx`). Changing speed crossfades the frames over about 0.2 s on unscaled time. HUD animations must use unscaled time so they don't speed up at 2×/3× or freeze while paused.
- Keep UI textures on Point filtering (pixel art). Add new UI sprites under the matching `Resources/UI/<category>/` folder.
- **Draft range preview:** the card-pick details panel draws each operator's `rangePattern` as a tile grid facing right (`CardDraftOverlayUI.UpdateRangePreview`). It uses the placement-preview colours: cyan for the operator's own tile, green for tiles it can hit. It reads the same data as combat, so never hand-draw or screenshot range previews. A kit that reaches beyond `rangePattern` (e.g. Basurocket's splash) belongs in the ability text.
- `AudioSettingsUI` loads `panel_dark` / `btn_dark_normal` with its own copy of the loader. Prefer the `GameplayHUDUI` helpers for new UI.
- **Top panel** (`Resources/UI/Top Panel BG`, 640×30 frames drawn at 3×) sits behind the top HUD bar. Its dark screens, in frame pixels: x 24–78 (phase), x 112–169 (wave counter + progress bar), x 299–335 (enemy count), x 590–637 (lives).
  - **Wave progress:** `bar_frame` (9-sliced) with a tiled `bar_fill_*` at 2× under the wave counter. The fill sprite follows the phase: yellow = Card Pick, blue = Preparation, red = Wave Active (`SetWaveProgressFillSprite`).
  - **Lives:** `tube_frame` + tiled `tube_liquid` + `tube_surface` at 2×, built upright and rotated −90° so it reads horizontally. Liquid = current/max lives and drains toward the left. The frame flashes red on a lost life. In Sandbox the tube is hidden and "LIVES LOCKED" text shows instead.
  - Load whole UI textures with `GameplayHUDUI.LoadWholeTextureSprite(path, border)`: Point-filtered, ignores importer slicing, cached. Draw pixel art at whole-number scales (`pixelsPerUnitMultiplier = 1 / scale` for Sliced/Tiled images).
- **Pause menu** (`GameplayHUDUI.CreatePausePanel`): `Resources/UI/panels/pause menu panel bg.png` (256×256) at 2×. The title sits in the header screen (art y 37–101) and four buttons in the body screen (y 104–217): Resume, Retry, Settings and Exit to Menu. Settings is added by `AudioSettingsUI.Create` from `Awake` into `PauseSettingsSlotPx`. Give every pause button its own slot: Settings used to sit on top of Retry and hid it.
- **Operator inspect label** shows name, rarity, ATK/HP and SHOW MORE. The skill text appears only in the SHOW MORE details panel.

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
| Main menu | Esc | Close stage details, or return from stage select to the main menu |

- **Esc is handled once per frame.** Whatever handles it calls `GameplayHUDUI.ConsumeEscapeKey()`. Other handlers check `HasConsumedEscapeThisFrame` first. `GameplayHUDUI.HandleEscapeKey()` is the fallback that ends in pause/resume. Follow the same pattern for any new Esc-closable panel.
- Gameplay input (deploy hotkeys, draft keys) must do nothing while `GameplayHUDUI.IsPaused`.
- **Overlays above the HUD must hide while paused.** The draft overlay's canvas (sort order 200) sits above the HUD canvas (100) that holds the pause menu, so `CardDraftOverlayUI` disables its canvas while paused. Otherwise it stayed clickable, and confirming during pause added a card without leaving Card Pick, which allowed several picks. Confirm also only runs during an active Card Pick, once per draft (`pickConfirmed`).

### 7.7 Main Menu Stage Map

- Stage select is a Pip-Boy style map, built in code by `MainMenuController.BuildStageButtons`. It replaced the old stage grid. The background is `Art/Levels/Stage Selector Map.png`. Clicking a dot opens the stage details modal (difficulty selector, then play). A locked dot wobbles and plays the denied sound.
- Each stage is a `StageMapNode` placed at a **normalized position** (0–1 across the map art, origin top-left) from `MainMenuController.StageNodePositions`. Index *i* is Stage *i*+1, so add an entry there when adding a stage. `StageMapLayout` (on the map art) rescales and repositions the nodes whenever the art resizes, so they stay in place at any 16:9 resolution.
- Node badges are currently procedural circle sprites (`CreateCircleBadgeSprite`). The pixel-art `Resources/StageNodes/Node_N_{Idle,Selected,Locked}.png` sprites are **not wired up yet**.
- **Locking is not progression.** A node is locked only if `IsStagePlayable(stageId)` is false (a hard-coded list of stage IDs). "X / 6 UNLOCKED" counts that list, not cleared stages. Don't add unlock-on-clear progression without asking Raim (see "Persistence model", §4).
- **Terminal styling** (stage selector and dossier): the `Terminal*` colours in `MainMenuController`, `AddCornerBrackets` for L-shaped corner frames, and `StyleTerminalButton`. `MenuButtonFeedback` owns each button's `Outline` as its hover glow, so resting borders are brackets, not outlines. The selector has a title plate, a blinking cursor, and a status ticker that types out the hovered stage (`StageMapNode.HoverChanged`).
- **Stage dossier** (the details modal, 1300×720): an animated `StageMapPreview` on the left, and stage info, stats, difficulty cards, a waves/lives briefing, Back and Play on the right.
- **`StageMapPreview`** draws the stage art in UI with gameplay's tile math (`StageBootstrapper.SpawnVisualGridTiles`). A layer several map-widths wide is treated as an animation sheet (Stage 3's background). It shows one enemy route per spawn, chosen like `WaveManager.PrecomputePaths`: explicit `enemyPaths`, otherwise A* on a throwaway `GridManager`. Change both together if path rules change. Each route has its own colour (`StageMapPreview.GetRouteColor`, also used for its spawn marker and the legend) and its own lane, offset sideways and mitred at corners so shared tiles show routes side by side. Routes are dark casing + faint core + dashes (a RawImage scrolling its uvRect) that flow from spawn to exit. Animation: boot scan reveal → routes draw from spawn → dashes keep flowing; spawn/exit pings; CRT scanlines (`StageMapScanlines.SourceRows` ≈ height / 4). No enemy blips.
- The dossier's texts use best-fit (`MainMenuController.FitText`) so they shrink instead of clipping. The selector's ticker plate is sized to the full message (`FitStageTicker`, 820–1360 px wide, font 15→10).
- **CRT / old-TV look for the whole game** (`CrtDisplay`, `CrtScreen`, `Resources/Shaders/CrtScreen.shader` "TrashTD/UI/CRT"):
  - **`CrtDisplay`** is created before the first scene (`RuntimeInitializeOnLoadMethod`, DontDestroyOnLoad). Every frame it does three things:
    1. Points the scene camera at a screen-sized picture texture.
    2. Turns every Screen Space - Overlay canvas into Screen Space - Camera on a UI camera stacked on the scene camera (URP camera stack; sorting orders keep working).
    3. Draws the picture full-screen through the CRT shader with `CrtScreen.Television`.
    The runtime asmdef references URP for the camera stack.
  - **Shader:** soft horizontal pixel blending; scanline beams that widen on bright pixels (~270 lines, `UpdateFrame` scales with resolution); a full-strength RGB slot mask (or aperture grille, `maskType`) that brightens each colour's own phosphor; halation glow; plus curvature, rounded bezel, fringing, grain, rolling bar, flicker, jitter, vignette and glare. Tune it in the `Television` / `Monitor` presets in `CrtScreen.cs`.
  - **Input rule (important):** the glass is curved, so the picture is not at the raw mouse position. UI raycasts are bent automatically (`CrtGraphicRaycaster` replaces each canvas's `GraphicRaycaster`). **Any code that uses a mouse or `PointerEventData` position with a camera or canvas must convert it first with `CrtDisplay.ScreenToPicture(...)`** (see `StageBootstrapper`, `DeckSlotDragHandler`). For screen ↔ canvas conversions, pass the canvas camera (`canvas.worldCamera`), not `null`, because canvases are no longer overlays.
  - **Finding a canvas** to build UI on: use `CrtDisplay.FindGameCanvas()`, not `FindFirstObjectByType<Canvas>()`, which could return the CRT's own canvas.
  - **Monitor preview:** the dossier's map preview is its own small `CrtScreen.Monitor` (offscreen canvas + camera + RenderTexture). It has stronger curve and no scanlines or mask, because the TV supplies those and two line patterns would moiré.
  - `CrtScreen.Distort` / `IsOnScreen` must stay in sync with `Distort` / `ScreenMask` in the shader.
- `StageMapScanlines` (now unused by the menu; superseded by the CRT shader) is the old CRT overlay: scanlines, a rolling refresh band, a vignette and a faint flicker, all tunable in the Inspector. It builds its layers at runtime and runs on unscaled time.
- The map scripts' file names don't match their class names (`Stagemapnode.cs` → `StageMapNode`, `Stagemaplayout.cs` → `StageMapLayout`, `Stagemapscanlines.cs` → `StageMapScanlines`). That works because they are only added with `AddComponent`. Rename the files before attaching any of them in the Inspector.
- `MapSelectUI` (an older stage-select component that also uses `StageMapNode`) isn't used in any scene. Its `IsStageUnlocked` always returns true.
