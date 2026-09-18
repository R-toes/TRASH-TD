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

---

## 3. Data Architecture Mandate

Per GDD section 1.10 and the course's software-reuse objective:

- **Creature data** → `ScriptableObject` assets (`.asset` files), designer-editable without code changes
- **Enemy data** → `ScriptableObject` assets
- **Stage data** → Separate config files (ScriptableObjects or JSON) containing wave timing, spawn points, path nodes
- **Adding a new creature or enemy must not require modifying existing code** — only creating a new data asset and (optionally) a new prefab
- Architecture patterns (inheritance, interfaces, composition) are part of the assignment deliverable — favor reusable, extensible patterns over one-off implementations

---

## 4. "Do Not Invent" List

The following areas are **undefined in the source GDD**. Do not fill them in with invented content.
Use clearly marked `// TODO:` comments and placeholder implementations instead.

| Area | Status |
|---|---|
| **Narrative / Lore** | Not defined — no story, factions, or world-building |
| **Art / Visual Style** | Not defined — use placeholder/programmer art |
| **Audio Direction** | Not defined — leave empty Audio/ folder |
| **Economy beyond card-draft** | Not defined — no currencies, shops, or meta-progression beyond the duplicate-upgrade system |
| **Persistence model** | Ambiguous — unclear if progression persists between runs or resets per map (could be roguelite). **Do not assume either way** — flag and ask Raim |
| **Skill specifics** | GDD mentions skills/abilities with cooldowns but doesn't define specific skills per class. Stub the system, don't invent individual skills |
| **Boss mechanics** | No separate boss format defined — treat bosses as wave entries |

---

## 5. Working Agreement (Agent ↔ Project Owner)

1. **Match the GDD exactly.** Do not add features, systems, or scope beyond what's specified without asking Raim first.
2. **Prefer minimal, targeted changes** over broad rewrites when modifying existing code.
3. **If an implementation choice isn't determined by the GDD** (e.g., exact stat numbers, exact Unity packages, specific skill effects), flag it as a decision point rather than picking silently.
4. **Keep this file updated** if Raim makes design decisions in later sessions that supersede a "not yet defined" placeholder.
5. **Pathfinding must use A\*** — this is an explicit technical requirement, not a suggestion.
6. **All maps are grid-based** and selected non-linearly (Bloons TD style), with 3 difficulty variants each (Easy/Normal/Hard → 10/5/1 life points).

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
