# Sound effects

Drop audio files into the folder shown, with the exact file name below. Nothing else to set up: the game finds them by name. Any format Unity imports works (`.wav`, `.mp3`, `.ogg`); the extension doesn't matter.

All paths are under `Assets/_Project/Audio/Resources/SFX/`.

```
SFX/
├── UI/                                  menu and draft buttons, card animations
├── Gameplay/                            deploy, retreat, waves, lives, victory/defeat, pause
├── Combat/                              shared attack fallbacks, stun, miss
├── Tiles/                               trap and hole tiles (any stage)
├── Stages/Stage01 … Stage06/            stage ambience loops and hazards
├── Operators/{Class}/{OperatorName}/    per-operator sounds
└── Enemies/{Archetype}/{EnemyName}/     per-enemy sounds
```

## Rules

- **Variations:** add `_2`, `_3`, ... to any name (`Attack_2`, `CarHit_2`, `Ability_Dodge_2`) and one is picked at random each time. The plain name counts as one of them and can be left out.
- **Pitch** varies slightly on every unit sound, so even a single file doesn't sound robotic. Each sound gets its own audio voice, so one pitch shift never bends another sound.
- **Missing files are fine.** Each sound marked *fallback* below plays an existing sound until you add its own. Everything else stays silent until a file exists.
- **Repeats are throttled.** The same unit's sound won't restart within 0.08 s, and the same shared sound within 0.05 s, so 40 rushers or 5 burning operators don't stack into noise.
- **Ambience** files loop. Make them seamless loops, 10–30 s. They fade in and out.
- **Attack sounds stop when the impact plays.** If a unit has an `Impact` file, its still-running `Attack` sound is cut (quick 0.04 s fade) the moment the impact plays, so a long launch or charge-up never overlaps its own explosion. Melee swings that hit instantly are left alone.
- **Volume per file:** see "Adjusting volume" below.
- Folder names are the unit's in-game name with spaces and symbols removed ("Chi Paw" → `ChiPaw`, "Stag-ger" → `Stagger`). Renaming a unit means renaming its folder. Operators are grouped by their in-game class (Drawgoo is a Defender).

## Shared sounds

### `UI/`

| File | When it plays |
|---|---|
| `ButtonHover` | ✓ exists. Pointer enters any button. |
| `ButtonClick` | ✓ exists. Any button press. |
| `ButtonBack` | ✓ exists. Back / close buttons. |
| `ButtonConfirm` | ✓ exists. Draft card picked. |
| `ButtonDenied` | ✓ exists. Locked stage, card still on cooldown, squad full, can't deploy on that tile. |
| `CardShuffle` | Draft cards riffle-shuffle (per swap). *Fallback: ButtonHover.* |
| `CardDeal` | Each draft card slides into place. *Fallback: ButtonHover.* |
| `CardFlip` | A draft card flips face-up. *Fallback: ButtonHover.* |
| `Reroll` | Draft reroll button. *Fallback: ButtonClick.* |
| `CardDiscard` | A deck card is discarded (after confirming). |
| `TutorialVoice` | Coalesce's talking blip in the tutorial, played for each typed character (pitch varies slightly). Keep it very short (≈40 ms). Until this file exists, a soft synth blip is generated in code. |

### `Gameplay/`

| File | When it plays |
|---|---|
| `Deploy` | ✓ exists. Operator placed. Used when the operator has no own `Deploy`. |
| `Retreat` | Operator recalled to the deck (not on death). |
| `OperatorDeath` | Operator dies. Used when the operator has no own `Death`. |
| `EnemyDeath` | ✓ exists. Enemy dies. Used when the enemy has no own `Death`. |
| `WaveStart` | ✓ exists. A wave begins. |
| `WaveCleared` | Every enemy of a wave is dead (not the last wave). |
| `LifeLost` | ✓ exists. An enemy reaches the exit. |
| `Victory` | Stage cleared. |
| `Defeat` | Out of lives. |
| `Pause` | Pause menu opens. |
| `Resume` | Pause menu closes. |
| `CardUpgrade` | 3 copies merge into a higher-rarity card. |

### `Combat/`

| File | When it plays |
|---|---|
| `MeleeAttack` | ✓ exists. Operator attack fallback: melee physical. |
| `RangedPhysicalAttack` | ✓ exists. Operator attack fallback: ranged physical. |
| `MagicAttack` | ✓ exists. Operator attack fallback: Arts. |
| `Stun` | An operator gets stunned (falling tools, car hit). |
| `Miss` | An attack misses: the sandstorm's miss chance, or Sand Plastic / Sand Poncho / Sand Hulk evading an operator. |

### `Tiles/`

| File | When it plays |
|---|---|
| `TrapTick` | A trap tile damages a unit standing on it (each tick). |
| `HoleEnter` | A ground enemy drops into a hole tile. |
| `HoleExit` | It climbs out of the paired hole. |

### `Stages/`

| Folder | File | When it plays |
|---|---|---|
| `Stage01` … `Stage06` | `Ambience` | That stage's background loop (see below). Any stage can have one. |
| `Stage02` | `ItemFall` | An item starts falling (every 10 s). |
| `Stage02` | `ItemLandGlass` | Glass item lands (damage over time). *Fallback: Combat/MeleeAttack.* |
| `Stage02` | `ItemLandMetal` | Metal item lands (burst damage). *Fallback: Combat/MeleeAttack.* |
| `Stage02` | `ItemLandTools` | Tools item lands (damage + stun). *Fallback: Combat/MeleeAttack.* |
| `Stage04` | `HeatUp` | An operator gains a heat stack. |
| `Stage04` | `Ignite` | An operator reaches 5 stacks and catches fire. |
| `Stage04` | `BurnTick` | Burning damage (once per second while anyone burns). |
| `Stage04` | `Extinguish` | A burning operator is retreated. |
| `Stage05` | `CarPass` | A car enters the boulevard (engine / horn / pass-by). |
| `Stage05` | `CarHit` | A car runs into an enemy or operator. |
| `Stage06` | `AcidTick` | Acid rain damage tick (every 2 s). |

## Operators — `Operators/{Class}/{Name}/`

| File | When it plays |
|---|---|
| `Deploy` | Placed on the field. *Fallback: Gameplay/Deploy.* |
| `Attack` | The attack starts: swing, shot, launch, cast. Medics: each heal. *Fallback: Combat/ sound for its damage type.* |
| `Impact` | The attack lands (once per volley, even on several enemies). |
| `Ability` | The operator's special trigger (table below). |
| `Ability_{Name}` | Operators with several triggers have named ones (table below). Each falls back to the plain `Ability`, so one `Ability` file covers them all. |
| `Death` | Dies. *Fallback: Gameplay/OperatorDeath.* |

| Folder | Attack | Impact | Ability files |
|---|---|---|---|
| `Guard/ChiPaw` | chi paw strike | strike lands | `Ability`: knockback (every 4th hit) |
| `Guard/Echosquire` | slash | hit / life drain | |
| `Guard/Ignisaur` | claw (melee) or fireball (ranged) | hit | |
| `Guard/Scrapper` | swing | hit | `Ability`: stun (20% chance) |
| `Guard/Shadeslice` | slash | hit | `Ability_Regen`: starts regenerating (5 s untouched) |
| `Guard/Stagger` | slugger swing | hit | `Ability_Dodge`: dodges an attack · `Ability_Empowered`: the +50% ATK strike after a dodge |
| `Defender/Bulkhead` | bash | hit | `Ability`: ATK/DEF buff switches on (blocking 3+) |
| `Defender/Drawgoo` | attack | hit | `Ability`: draws cards on death (plays after `Death`) |
| `Defender/Mossmo` | | | `Ability`: reactive heal pulse |
| `Defender/Thornchin` | attack | hit | `Ability`: spine retaliation. *Fallback: Combat/MeleeAttack.* |
| `Sniper/Basurocket` | rocket launch | **explosion** | |
| `Sniper/Deadeye` | rifle shot | bullet hit | |
| `Sniper/Proxishot` | shotgun blast | pellets hit | `Ability`: turns camouflaged |
| `Caster/Castiel` | beam cast | beam tick | |
| `Caster/Chillpath` | frost cast | frost hit | `Ability_Freeze`: freezes an enemy (and gains haste) · `Ability_ExtraHit`: every 4th attack's bonus bolt |
| `Caster/Pyrolite` | fire cast | fire burst | |
| `Medic/Bubblets` | shield cast | | `Ability`: bubble pop. *Fallback: Combat/MagicAttack.* |
| `Medic/Coalesce` | heal | | `Ability`: bonus heal (20% on low-HP allies) |
| `Medic/Progeny` | heal | | `Ability`: chain-heal bounce |

## Enemies — `Enemies/{Archetype}/{Name}/`

| File | When it plays |
|---|---|
| `Attack` | Attack starts. |
| `Impact` | Its projectile lands (ranged enemies only). |
| `Death` | Dies. *Fallback: Gameplay/EnemyDeath.* |

| Folder | Attack | Impact |
|---|---|---|
| `Grunt/SludgeGrunt` | melee hit | |
| `Grunt/SandPlastic` | melee hit / sand throw | sand lands |
| `Rusher/ToxicRusher` | melee hit | |
| `Tank/PollutionTank` | heavy slam | |
| `Tank/SandHulk` | shockwave slam | |
| `Caster/SandPoncho` | cast | spell lands |
| `Caster/SmogCaster` | cast | spell lands |
| `Flyer/TalonRat` | claw swipe | |
| `Flyer/Skyshard` | shard shot | shard blast |
| `Flyer/AcidFlyer` | acid spit | acid splash |

Enemy sounds play at 70% volume (80% for deaths), since many more enemies are on screen at once.

Code: `Scripts/Audio/AudioManager.cs` (shared sounds: the `SfxPaths` table), `Scripts/Audio/UnitSfx.cs` (unit sounds).

## Music — `Assets/_Project/Audio/Resources/Music/`

| File | When it plays |
|---|---|
| `MainMenuMusic` | ✓ exists. Main menu. |
| `GameplayMusic` | ✓ exists. Any stage without its own track. |
| `Stages/Stage01` … `Stages/Stage06` | That stage's own track (the sandbox plays the track of the map it shows). |

Music loops, so make the tracks seamless loops. A retry keeps the track playing instead of restarting it.

## Stage ambience — `SFX/Stages/StageNN/Ambience`

Every stage can have a background loop that plays under the music for the whole stage (planning, waves and pause). Name it `Ambience` in that stage's folder. Stages without one are silent.

| Folder | Suggested ambience |
|---|---|
| `Stages/Stage01/` | Junkyard: light wind, distant clanks, flies. |
| `Stages/Stage02/` | Street / landfill crowd: distant voices, things being tossed. |
| `Stages/Stage03/` | Sandstorm wind and blowing grit. |
| `Stages/Stage04/` | Factory hum, furnace roar, machinery, hissing steam. |
| `Stages/Stage05/` | Boulevard traffic rumble, distant horns. |
| `Stages/Stage06/` | Acid rain on metal and puddles. |

- Make it a seamless loop, 10–30 s, with no obvious one-off events that repeat noticeably.
- Keep it quiet in the mix; the game plays it at 45% of the SFX volume, with a fade in/out.
- `Ambience_2`, `Ambience_3`, ... are allowed: one is picked at random when the stage starts.
- The sandbox plays the ambience of the map it shows. Retrying a stage keeps the loop going.
- A new stage only needs a `Stages/StageNN/` folder; no code changes.

## Adjusting volume — `Audio/Resources/SfxVolumeSettings.asset`

Unity's audio import settings have no volume, so per-file volume lives in one settings asset:

1. In the Project window, select `Assets/_Project/Audio/Resources/SfxVolumeSettings`.
2. In the Inspector, press **+** under **Clips** to add a row.
3. Drag the audio file into **Clip** and set **Volume** (1 = as recorded, 0.5 = half).

- Works for every sound effect: operators, enemies, UI, stage sounds. Not music or ambience.
- Changes apply immediately, even while the game is running in Play mode (edits made in Play mode are kept).
- Volume can only go down. To make a file louder than recorded, boost it in an audio editor.
- Each variation (`Attack`, `Attack_2`, ...) is its own row.
