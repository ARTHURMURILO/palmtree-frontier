# Idea: Officer Beepsky — automatically patrolling security bot

| | |
|---|---|
| **Status** | Proposed — not implemented (plan requested 2026-10-15, alongside the shipped roomba) |
| **Scope** | New `_PS` feature: a Securitron/Beepsky bot that patrols the grid it is placed on (Frontier Outpost by default), checks nearby people, and stuns/arrests offenders. Stun, not lethal. |
| **Touched areas** | `Content.Server/_PS/Securitron` (new code, if needed), `Resources/Prototypes/_PS/NPCs`, `Resources/Prototypes/_PS/Entities/Mobs/NPCs`, `Resources/Textures/_PS/Mobs/Silicon/Bots/secbot.rsi`, `Resources/Locale/en-US/_PS`, `Resources/Maps/_NF/Outpost/frontier.yml` (single map edit), changelog |
| **Research anchors** | `MobSiliconBase`/`MobCleanBot` (`Resources/Prototypes/Entities/Mobs/NPCs/silicon.yml`), `_PS/Roomba` operators (`Content.Server/_PS/Roomba`), `IdleCompound` + `PickAccessibleOperator` (`Resources/Prototypes/NPCs/idle.yml`, `Content.Server/NPC/HTN/PrimitiveTasks/Operators/PickAccessibleOperator.cs`), `MeleeCombatCompound` (`Resources/Prototypes/NPCs/Combat/melee.yml`), `NPCBlackboard` (`Content.Server/NPC/NPCBlackboard.cs`), `ai_factions.yml`, `FactionClothing` (`_NF/Entities/Clothing/base_faction_clothing.yml`), `_PS` `WeaponDetectorSystem`, `SharedStunSystem`/`StunbatonSystem`, `SharedCuffableSystem`, `CriminalRecord`/`CriminalRecordsSystem` |
| **Prior art** | SS13 secbot + Officer Beepsky. Modern /tg/ \+ SPLURT-tg: `code/modules/mob/living/basic/bots/secbot/` (`secbot.dm`, `secbot_ai.dm`, `secbot_subtypes.dm`, `super_beepsky.dm`); classic: Sandstorm-Station-13 `code/modules/mob/living/simple_animal/bot/secbot.dm`. **No SS14 equivalent**: not in Wizden, Frontier, Coyote, or this repo (verified); the Rayten fork's `SecuritronCompound.yml` is a stub with the arrest/patrol tasks commented out. |

## 1. Summary

Port **Officer Beepsky** (SS13's `secbot`): a small security robot that

1. **Automatically patrols the grid he is placed on** — no waypoints, no roboticist setup. This is
   the core ask; placement on the Frontier Outpost is just where he starts out.
2. **Checks people** he passes for contraband clothing, visibly carried weapons, and (phase 2)
   security-record `Wanted` status.
3. **Stuns** offenders with a baton (knockdown + stamina, never brute), announces the arrest in
   chat, and after a timeout resumes patrol. Cuffing and fines are stretch goals.
4. Reacts to being attacked (retaliates) and can be emagged into a "harmbaton" menace (optional).

There is deliberately no arrest-carrying or brig automation in v1: with Palmtree's no-antag sandbox,
Beepsky is a deterrent/mover of unruly players, not a full security system.

## 2. Behaviour design

### 2.1 Patrol (the core)

Reuse the existing wandering machinery instead of writing a pathing system:

```yaml
- type: htnCompound
  id: BeepskyPatrolCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:PickAccessibleOperator
        rangeKey: PatrolRange          # new blackboard value, ~24 (IdleCompound uses IdleRange = 7)
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        pathfindInPlanning: true
    - !type:HTNPrimitiveTask
      operator: !type:WaitOperator
        key: PatrolWait             # short beat at each stop, e.g. 3-8 s via RandomOperator
```

- `PickAccessibleOperator` calls `PathfindingSystem.GetRandomPath(owner, maxRange, ...)`, which
  returns a **random reachable coordinate**: the bot can only pick points on its own grid's
  pathfinding graph, so "patrol whatever grid he's placed in" falls out for free.
- `rangeKey: PatrolRange` (default 7 is too small for a patrol) — set 20-30 via the entity's HTN
  `blackboard`. Points too close to where he stands are possible (`GetRandomPath` only takes a max
  range); the wait at each stop masks it, and a `MinRange` could be added to a custom operator later.
- Sub-options, only if desired later:
  - **Mapper waypoints** (SS13 patrol beacons): a `PatrolPointMarker` entity + a custom
    `PickPatrolPointOperator` (same shape as the roomba's `FindCleanableDecalsOperator`). Not needed
    for v1.
  - **Return to post**: store the spawn coordinates (`MapInitEvent`) and add a "walk home" branch
    when the bot drifts more than N tiles from the outpost. Nice-to-have.
- Doors: set `NavInteract: true` in the `HTN` blackboard and give him security access (SS13 secbots
  open doors; the roomba/cleanbot does not). `NavPry` only if we want him to force doors — probably
  not for v1.
- Chunk activity: Frontier's `HTNSystem.IsNPCActive` skips NPCs on unloaded chunks, so patrolling an
  outpost no one is on costs nothing; he wakes when players load the area. No new sleep logic needed.

### 2.2 Threat checks

Three independent gates, in priority order (first match ends the patrol branch):

| Gate | How | Notes |
|---|---|---|
| **Contraband** | `NpcFactionMember: [NanoTrasen]` (or a new `Securitron` faction hostile to `ContrabandClothing`/`ContrabandDetection`); `FactionClothing` already puts `ContrabandClothing` on smugglers wearing contraband bags/headsets | Frontier-native; no new code. Verify the hostile-trigger path actually picks up `FactionClothing` wearers as targets with `NearbyMeleeTargets`. `ContrabandDetection` is referenced by faction YAML but nothing in C# assigns it — treat it as unused for now. |
| **Weapons** | A small `ScanForContrabandOperator` modelled on `_PS` `WeaponDetectorSystem` (gun/melee/explosive tags, `Contraband` component?) scanning nearby mobs within vision + LOS | Needs a custom operator; keep vision short (~5 tiles) and add a per-mob cooldown so the bot does not ping-pong. |
| **Records (phase 2)** | `CriminalRecordsSystem`-backed `SecurityStatus.Wanted`/`Detained` check per nearby mob (`CriminalRecordComponent`), server-only | Records are server-only and keyed by mind/session — needs care (mobs without minds). SS13-authentic "check records on scan". |

Retaliation: attacking Beepsky should add the attacker as a target (check what the repo uses —
`NPCRetaliationSystem`/`MeleeRetaliate`; carps/simple hostiles retaliate out of the box).

### 2.3 The arrest

1. Target found → `MoveToOperator` into `MeleeRange` (existing combat compounds do this:
   `MeleeAttackTargetCompound`).
2. Announce: `ChatSystem.TrySendInGameICMessage(uid, Loc.GetString("securitron-arrest-announce"), ...)`
   (precedent: `MedibotInjectOperator`), plus a beep sound.
3. Stun: two options —
   - **Melee stun (recommended, SS13-flavored)**: `MeleeWeaponComponent` on the bot with a high
     `BluntStaminaDamageFactor` (blunt damage drains stamina → stamina collapse knocks the target
     down, the same stat the stun baton depletes), or a tiny `_PS` system that calls
     `SharedStunSystem.TryKnockdown` on `MeleeHitEvent` if the attacker has a `SecuritronComponent`.
     The second is explicit and avoids accidental brute damage; costs ~30 lines.
   - **Taser**: the re-enabled `WeaponTaser` + `InnateRangedCombatCompound`. Less SS13-authentic
     (Beepsky was melee) and adds friendly-fire risk; keep as an admin/emag variant.
4. Optional phase 3: cuff (`HandcuffComponent` + `SharedCuffableSystem`), or fines
   (`price_arrest`-style) if the maintainer wants the joke.
5. Timeout/cooldown, then back to patrol. Never chase beyond the grid (`TargetAccessibleCon` keeps
   targets within the reachable graph).

### 2.4 Emag / variants (optional, phase 3)

- `EmaggableSecuritron` modelled on `EmaggableMedibotComponent`: emag → attacks everyone (SS13's
  harmbaton), maybe `NavSmash: true`.
- Subtypes worth the YAML: Officer Beepsky (default, 45-50 HP), Commander Beep O'sky (bigger,
  `NavPry`?), Prison Ofitser, Officer Pipsqueak (junior), Armsky. Sandstorm also has a
  `SuperBeepsky` boss variant — out of scope unless asked.

## 3. Implementation sketch

```
Content.Server/_PS/Securitron/
    SecuritronComponent.cs              # marker/config (stun duration, cooldowns, announce), if needed
    SecuritronSystem.cs                 # stun-on-meleehit or scan helpers (only if the gates need code)
    ScanForContrabandOperator.cs        # optional: HTN operator returning a target entity
Resources/Prototypes/_PS/NPCs/silicon.yml      # BeepskyCompound / BeepskyPatrolCompound / arrest compound
Resources/Prototypes/_PS/Entities/Mobs/NPCs/silicon.yml  # MobSecuritron (+ Beepsky subtypes)
Resources/Textures/_PS/Mobs/Silicon/Bots/secbot.rsi      # 4-direction sprite
Resources/Locale/en-US/_PS/securitron.ftl                # petting/announce strings
```

- **Sprite**: extract `secbot0`/`secbot1` (and `secbot-c` wreck) from the /tg/-lineage DMI with the
  `.dmi` → `.rsi` zTXt/Pillow method (`.ai/guides/porting-from-ss13.md` §5):
  - `S.P.L.U.R.T-tg/icons/mob/silicon/aibots.dmi` states `secbot`/`secbot0`/`secbot1`/`secbot-c`,
    4 directions, 32×32 — freshest art;
  - `Sandstorm-Station-13/icons/mob/aibots.dmi` has the older `secbot0/1/-c` in four directions if
    the SPLURT art does not fit.
  - Record license/copyright in the RSI `meta.json`.
- **Sounds**: /tg/ uses `sound/mobs/non-humanoids/beepsky/{criminal,justice,freeze}.ogg`. Check
  `Resources/Audio` for a suitable existing beep first (`Audio/Ambience/Objects/periodic_beep.ogg`
  is the cleanbot's pet sound; `Audio/Machines/...` has alarm/beep sets) or port the three oggs with
  an `attributions.yml` entry.
- **Entity**: parent `MobSiliconBase` (like the roomba), add `Access` (security + basic silicon),
  `HTN` (`BeepskyCompound`, blackboard `NavInteract: true`, `PatrolRange: 24`), `MeleeWeapon`,
  `InteractionPopup` (pet lines), `MovementSpeedModifier` (faster than a cleanbot: ~3.5 walk),
  `MobThresholds` ~45-50 HP, `Destructible` (wreck on death, like other bots), optional
  `GhostRole`/`GhostTakeoverAvailable` so players can possess him.
- **Crafting**: not required by the request. If wanted later, reuse the bot construction graph
  pattern (a secbot needs a stun baton + proximity sensor + borg arm; no bucket) — but note the
  `Stunbaton` is a controlled item; an admin/cargo route is probably saner.

## 4. Placement on the Frontier Outpost

- The outpost is the game map `Frontier` (`Resources/Prototypes/_NF/Maps/Outpost/frontier.yml`) →
  `Resources/Maps/_NF/Outpost/frontier.yml` (single station grid, ~5.5k entities).
- v1: add **one** `MobBeepsky` to that map somewhere sensible (NFSD office / main corridor) — a
  small isolated hunk, but it *will* show up as a merge conflict when upstream changes the map, so
  keep it as a single `- uid:`/`- proto: MobBeepsky` pair and document it in the PR/changelog.
- Alternative if map conflicts become annoying: a `SpawnMobBeepsky` marker prototype only
  (spawnable by admins/mappers), leaving placement to the maintainer. Recommend both: place one on
  the outpost, ship the spawner for elsewhere.
- Do **not** put him on docks/expedition maps; patrol range and pathfinding are per-grid.

## 5. Validation

- `RoombaTest`-style integration test (`Content.IntegrationTests/Tests/_PS/SecuritronTest.cs`):
  spawn on a multi-tile test grid, wake, run ticks, assert the bot moved > N tiles from spawn
  (patrol works) and that a `Stunnable` test dummy within reach gets knocked down (arrest works,
  if the stun operator is in scope).
- `EntityTest` catches prototype errors; YAMLLinter catches the HTN YAML; headless client run is
  unaffected (server-only kinds are client-ignored).
- Manual: spawn on the outpost, watch a full patrol loop through doors; check he does not camp or
  jitter between two tiles (tune `PatrolRange`/wait); verify no friendly-fire in the sandbox.
- Changelog entry (player-visible): `add: Officer Beepsky patrols the Frontier Outpost`.

## 6. Open questions (maintainer decisions)

1. **Lethality**: stun-only (recommended) or stun + handcuffs + fines? Any limit on who he can
   arrest (e.g. only `Wanted` records, or also weapons/contraband)?
2. **Faction**: add him to `NanoTrasen` (inherits hostile-to-`ContrabandClothing`) or define a new
   `Securitron` faction (cleaner, avoids surprising interactions with other NT NPCs)?
3. **Sandbox fit**: should he exist by default on the outpost, or ship as a craftable/admin spawn
   only? If default: one Beepsky in a quiet corner, or a small squad?
4. **Ghost role / emag**: posessable by ghosts? emag variant a must-have or optional?
5. **Patrol style**: random walk (v1 recommendation) or mapper-defined patrol routes/waypoints?
