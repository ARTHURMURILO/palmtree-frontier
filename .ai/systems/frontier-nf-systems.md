# System: Frontier (`_NF`) Systems

> Summary of the parent fork's gameplay layer. Verify specifics against `_NF` source when relying on
> them; this repo does not modify most of it.

## Purpose

Frontier turns SS14 into a hub-and-ships sandbox: a station (Nash) with NFSD, player-owned shuttles,
sectors/POIs, a player economy, salvage/expeditions and missions. Station antagonists are disabled by
default (`rules: []` in upstream presets).

## Locations

`Content.Server/_NF`, `Content.Shared/_NF`, `Content.Client/_NF`, `Resources/Prototypes/_NF`,
`Resources/Locale/en-US/_NF`, `Resources/Maps/_NF`.

## Major systems

| System | Location | Purpose |
|---|---|---|
| Bank | `_NF/Bank` | Accounts, ATM, transfers, loadout/round payouts |
| Shipyard | `_NF/Shipyard` | Ship purchase/sale/consoles/records, drydock |
| Shuttle records | `_NF/ShuttleRecords` | Ownership and registration |
| Market | `_NF/Market` | Market consoles, pricing, whitelist modifiers |
| Cargo | `_NF/Cargo/Systems` | Frontier cargo ordering/selling |
| Salvage/expeditions | `_NF/Salvage`, `Content.Server/Salvage` | Expeditions, dungeons, debris, magnets |
| Cryo | `_NF/CryoSleep` | Cryo pods, despawn/return |
| Pirates | `_NF/Pirate`, `_NF/Cargo/Systems/NFCargoSystem.PirateBounty.cs` | Pirate bounty database |
| Bounty contracts | `_NF/BountyContracts` | Contract offers/UI |
| Medical bounties | `_NF/Medical/MedicalBounty*` | Medical bounty redemption |
| NPCs | `_NF/NPC` | Hostile/friendly NPCs for dungeons/expeditions |
| Trade/smuggling | `_NF/Trade`, `_NF/Smuggling` | Trade outposts, contraband |
| Mail | `_NF/Mail` | Mail delivery |
| Size attributes | `_NF/SizeAttribute` | `ShortWhitelist`/`TallWhitelist` (used by Rodentia/Anthromorph) |
| Garbage collection | `_NF/GC` | Deletion census for abandoned map items/wrecks; exemptions and loot protection |

## Round/station integration

- Game presets: `Resources/Prototypes/_NF/game_presets.yml` (`NFAdventure`, `NFPirate`, `NFTest`).
- `GameTicker.NFSpawning.cs` (partial `GameTicker`, intentional namespace collision) hooks spawn to
  apply NF loadouts and bank balance.
- Station maps/pools: `NFMapPool`; default map `Frontier`.
- NPC/faction presets live under `_NF` prototypes (`ai_factions.yml`, GameRules, Events).

## Prototype areas (partial)

`Resources/Prototypes/_NF` contains: `Bank`, `Shipyard`, `Catalog`, `Cargo`, `Loadouts`, `NPCs`,
`Entities/Mobs/NPCs`, `Events`, `GameRules`, `Missions`-adjacent bounty content, `Sectors`, `POI`,
`Salvage`, `Shipyard`, `Trade`, `VendingMachines`, and more.

## Palmtree interactions with `_NF`

- Contractor loadout groups are extended with `_PS` (concealment implanter), `_CS` (Bayou clothes,
  leather satchel), `_DEN` (foot protectors, clicker) loadouts.
- `_PS` lobby backgrounds/music are separate from `_NF/lobbyscreens.yml`.
- No `_NF` core logic was changed by the port except loadout group lists (the garbage collection port
  below is a deliberate exception).

## Garbage collection (deletion census)

Ported from Frontier PR #3574 (closed unmerged; as deployed by Coyote and several forks), with
additional loot protection.

- `_NF/GC/Systems/DeletionCensusSystem` scans direct children of the default map and the FTL map in
  batches every `nf14.gc.period` seconds. Entities sitting on unloaded worldgen chunks (or parented
  to the FTL map) accumulate tallies; after `nf14.gc.idle_count` passes they are deleted. Grids are
  deleted through `LinkedLifecycleGridSystem.UnparentPlayersFromGrid`, which reparents players first
  (and now also rescuable loot).
- Exemptions: `DeletionCensusExemptComponent` (YAML: POIs, the bus, bluespace event grids/dungeons/
  salvage, PDAs, ID cards, the station map grid; code: stations via `StationSystem`, shipyard and
  admin purchases, public transit buses, dead drops), entities with a mind, anything tracked by a
  pinpointer (`PinpointerTargetComponent`), and anything a player has picked up
  (`PlayerTouchedComponent`).
- Loot protection (Palmtree): entities worth more than `nf14.gc.max_value` and grids worth more than
  `nf14.gc.grid_max_value` are never culled; player-touched or pinned entities are moved off a grid
  before it is culled instead of dying with it. Player-touched protection lapses
  `nf14.gc.touched_duration` seconds (default 60) after the entity is dropped, so abandoned items
  become cullable again.
- CVars: `nf14.gc.enabled`, `nf14.gc.period`, `nf14.gc.entities_per_tick`, `nf14.gc.idle_count`,
  `nf14.gc.max_value`, `nf14.gc.grid_max_value`, `nf14.gc.touched_immune`,
  `nf14.gc.touched_duration`. Defaults: 30-minute period, 4 idle passes (~2 hours), touched-item
  protection on and lapsing 60 seconds after a drop.
- `WorldControllerSystem.TryGetChunk` avoids creating chunks during census checks and NPC activity
  checks (`HTNSystem.IsNPCActive`).

## Unknowns

- Which NF systems are actually active under the live config.
- Balance/reachability of ported `_PS` weapons and Bayou clothing.
