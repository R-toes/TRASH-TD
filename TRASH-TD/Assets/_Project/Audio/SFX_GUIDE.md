# Unit sound effects

Drop audio files into the unit's folder with the exact file names below. Nothing else to set up: the game finds them by name. Any format Unity imports works (`.wav`, `.mp3`, `.ogg`); the extension doesn't matter.

```
Audio/Resources/SFX/
├── Operators/{Class}/{OperatorName}/   Deploy, Attack, Impact, Ability, Death
└── Enemies/{Archetype}/{EnemyName}/    Attack, Impact, Death
```

## File names

| File | When it plays |
|---|---|
| `Deploy` | Operator placed on the field. |
| `Attack` | The attack starts: sword swing, gunshot, rocket launch, spell cast. For Medics: each heal (Bubblets: casting a shield). |
| `Impact` | The attack lands: bullet hit, explosion, spell burst. Plays once per volley even when it hits several enemies. |
| `Ability` | The unit's special trigger (see the table below). |
| `Death` | The unit dies. |

- **Variations:** add `Attack_2`, `Attack_3`, ... (any event) and one is picked at random each time. The plain `Attack` counts as one of them; it can also be left out.
- **Pitch** varies slightly on every play, so even a single file doesn't sound robotic.
- **Missing files are fine.** Deploy, Attack, enemy Death and the ability sounds noted below fall back to the shared sounds in `SFX/` (the ones used today). The rest stay silent until you add a file.
- Folder names are the unit's in-game name with spaces and symbols removed ("Chi Paw" → `ChiPaw`, "Stag-ger" → `Stagger`). Renaming a unit means renaming its folder. Operators are grouped by their in-game class (Drawgoo is a Defender).

## Operators

| Folder | Deploy | Attack | Impact | Ability | Death |
|---|---|---|---|---|---|
| `Guard/ChiPaw` | ✓ | chi paw strike | strike lands | knockback (every 4th hit) | ✓ |
| `Guard/Echosquire` | ✓ | slash | hit / life drain | | ✓ |
| `Guard/Scrapper` | ✓ | swing | hit | stun (20% chance) | ✓ |
| `Guard/Shadeslice` | ✓ | slash | hit | | ✓ |
| `Guard/Stagger` | ✓ | slugger swing | hit | | ✓ |
| `Defender/Bulkhead` | ✓ | bash | hit | | ✓ |
| `Defender/Drawgoo` | ✓ | attack | hit | | ✓ |
| `Defender/Mossmo` | ✓ | | | reactive heal pulse | ✓ |
| `Defender/Thornchin` | ✓ | attack | hit | spine retaliation (falls back to melee sound) | ✓ |
| `Sniper/Basurocket` | ✓ | rocket launch | **explosion** | | ✓ |
| `Sniper/Deadeye` | ✓ | rifle shot | bullet hit | | ✓ |
| `Sniper/Proxishot` | ✓ | shotgun blast | pellets hit | | ✓ |
| `Caster/Castiel` | ✓ | beam cast | beam tick | | ✓ |
| `Caster/Chillpath` | ✓ | frost cast | frost hit | | ✓ |
| `Caster/Pyrolite` | ✓ | fire cast | fire burst | | ✓ |
| `Medic/Bubblets` | ✓ | shield cast | | bubble pop (falls back to magic sound) | ✓ |
| `Medic/Coalesce` | ✓ | heal | | | ✓ |
| `Medic/Progeny` | ✓ | heal | | chain-heal bounce | ✓ |

## Enemies

| Folder | Attack | Impact | Death |
|---|---|---|---|
| `Grunt/SludgeGrunt` | melee hit | | ✓ |
| `Grunt/SandPlastic` | melee hit / sand throw | sand lands | ✓ |
| `Rusher/ToxicRusher` | melee hit | | ✓ |
| `Tank/PollutionTank` | heavy slam | | ✓ |
| `Tank/SandHulk` | shockwave slam | | ✓ |
| `Caster/SandPoncho` | cast | spell lands | ✓ |
| `Caster/SmogCaster` | cast | spell lands | ✓ |
| `Flyer/TalonRat` | claw swipe | | ✓ |
| `Flyer/Skyshard` | shard shot | shard blast | ✓ |
| `Flyer/AcidFlyer` | acid spit | acid splash | ✓ |

Enemy sounds play at 70% volume (80% for deaths), since many more enemies are on screen at once. Repeats of the same unit's sound within 0.08 s are skipped, so 40 rushers attacking together don't stack into noise.

Code: `Scripts/Audio/UnitSfx.cs`.
