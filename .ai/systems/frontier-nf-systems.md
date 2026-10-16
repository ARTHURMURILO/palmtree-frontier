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
- Shuttle console extras (`_NF/Shuttles`, plus core `Shuttles` patches): inertia dampening, service
  flags, target tracking, and the Palmtree/Coyote radar proximity alert (`.ai/PORTING.md` §20).
- Expedition auto-recovery (Palmtree/Coyote): dead or left-behind bodies are returned to the
  shuttle when it departs and wiped expeditions abort early (`.ai/PORTING.md` §21). Deathrattles
  say "on an expedition" while on the map and re-broadcast with the real coordinates once the
  shuttle lands.
- **NFSD vault gating (Palmtree, 2026-10):** the two vault bluespace events
  (`BluespaceVaultError`, `BluespaceVaultSmallError`) only roll while an NFSD officer is on duty,
  and trespassing on the spawned vault grid flags the character as wanted.
  - `EventManagerSystem.CanRun` uses **Coyote's any-of semantics** for
    `StationEventComponent.RequiredJobs`: Frontier's original loop demanded *every* listed job
    (`return false` on the first unsatisfied one), Coyote flipped it to `jobsOk = true; break;` on
    the first satisfied one, so an event can gate on a department without needing its whole
    roster. Palmtree ports that flip, so `requiredJobs` with several entries means "at least one
    of these jobs is filled". The only other user in the tree is `BluespaceCacheError`
    (`Sheriff: 1`), where both semantics are identical - keep the divergence in mind when copying
    upstream events that list several jobs. Unlike Coyote (which passes `includeAfk: true`),
    Palmtree keeps Frontier's `includeAfk: false`, so an SSD/AFK officer does not count.
  - Both vault events list the NFSD jobs under `requiredJobs` (Sheriff, Deputy, Bailiff,
    Brigmedic, NFDetective, PublicAffairsLiaison, SeniorOfficer, plus Cadet, which Coyote omits),
    so a single on-duty NFSD character is enough for the event to be picked; otherwise `CanRun`
    fails and the event never spawns. Coyote's list is identical apart from Cadet and its longer
    `reoccurrenceDelay`/`rewardAccounts`, which were not ported.
  - `NfsdVaultTrespassComponent` (`Content.Shared/_PS/Security`) is added to the vault grid through
    the `BluespaceGridSpawnGroup.addComponents` list in `nf_bluespace_grids_events.yml`.
    `NfsdVaultTrespassSystem` sweeps every half second, and any humanoid standing on a marked grid
    whose **record job** is not in an exempt department (`Security` = NFSD, plus `Command`) is
    flagged wanted with the reason `Trespass - Vault`. Sweeping the characters (not the grid's
    children) also catches mobs buckled to furniture or inside containers.
  - `WantedStatusSystem` (`Content.Server/_PS/Security`) wraps the sector-wide criminal records:
    it resolves a character's `StationRecordKey` through their ID card (falling back to a lookup by
    identity name) and reads/sets `SecurityStatus.Wanted` with `CriminalRecordsSystem.OverwriteStatus`
    so the wanted list and the wanted icon update like a manual record edit.
  - Tests: `Content.IntegrationTests/Tests/_PS/NfsdVaultTrespassTest.cs`.
- **Black market ATMs (Palmtree, 2026-10):** regular ATMs refuse wanted criminals;
  `BankATMComponent.BlackMarket` (`// Palmtree` field, set on
  `ComputerBlackMarketBankATM`/`ComputerWallmountBlackMarketBankATM`) marks the machines that serve
  everyone. `BankSystem` cancels `ActivatableUIOpenAttemptEvent` and bails out of the
  withdraw/deposit handlers for a **non-black-market** ATM when `WantedStatusSystem.IsWanted(user)`
  is true, so law-abiding characters keep using regular ATMs while criminals have to find a black
  market one. Locale: `bank-atm-menu-refuses-wanted`. Test:
  `Content.IntegrationTests/Tests/_PS/BlackMarketAtmTest.cs`.
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
  admin purchases, public transit buses, dead drops), entities with a mind, visiting-mind ghosts
  (`VisitingMindComponent`, how Frontier's `/ghost` and death-ghosting work), anything tracked by a
  pinpointer (`PinpointerTargetComponent`), and anything a player has picked up
  (`PlayerTouchedComponent`).
- Loot protection (Palmtree): entities worth more than `nf14.gc.max_value` and grids worth more than
  `nf14.gc.grid_max_value` are never culled; player-touched or pinned entities are moved off a grid
  before it is culled instead of dying with it. Player-touched protection lapses
  `nf14.gc.touched_duration` seconds (default 60) after the entity is dropped, so abandoned items
  become cullable again. Visiting-mind ghosts are also rescued from culled grids.
- CVars: `nf14.gc.enabled`, `nf14.gc.period`, `nf14.gc.entities_per_tick`, `nf14.gc.idle_count`,
  `nf14.gc.max_value`, `nf14.gc.grid_max_value`, `nf14.gc.touched_immune`,
  `nf14.gc.touched_duration`, `nf14.gc.ignore_loaded_chunks` (debug: cull regardless of chunk
  loading, for testing). Defaults: 30-minute period, 4 idle passes (~2 hours), touched-item
  protection on and lapsing 60 seconds after a drop.
- Tests: `Content.IntegrationTests/Tests/_NF/DeletionCensusTest.cs` covers chunk gating, exemptions,
  touched expiry, loot rescue, value guards and the station exemption.
- `WorldControllerSystem.TryGetChunk` avoids creating chunks during census checks and NPC activity
  checks (`HTNSystem.IsNPCActive`).

## Unknowns

- Which NF systems are actually active under the live config.
- Balance/reachability of ported `_PS` weapons and Bayou clothing.

## Tiered thrusters (2026-10)

Coyote's thruster set (`.ai/PORTING.md` §22) lives in
`Resources/Prototypes/_NF/Entities/Structures/Shuttles/thrusters_but_cooler.yml`: six thrusters with
their own machine boards, lathe recipes and tags, unlocked by `NFShuttlecraftThrust` (slow pair) and
`NFShuttlecraftThrustSmall` (fast four) and printable from the `NFEngineeringBoards` lathe pack and
the `NFBlueprintsEngineering` blueprint pack. The base `Thruster`/`SmallThruster` were renamed to the
SpacePutt Mule/Mini naming, and the small thruster/gyroscope fixtures use
`MidImpassable | LowImpassable` instead of `MachineLayer` (coyote PR #656). `ThrusterComponent`
needs no code changes: `BaseThrust` feeds the shuttle's max speed and `ThrustPerPartLevel` the
acceleration, both already Frontier fields. Test: `Tests/_NF/TieredThrusterTest.cs`.

## Armor plates (2026-10)

Monolith's insertable armor plates (`.ai/PORTING.md` §23) live in `Resources/Prototypes/_Mono` and
`Content.Shared/_Mono/ArmorPlate`: the standard armor vest (`ClothingOuterArmorBasic`), every
hardsuit (`ClothingOuterHardsuitBase`), every jacket/coat with storage (`ClothingOuterStorageBase`)
and the chameleon vest (`ClothingOuterChameleon`) have an `ArmorPlateHolder` storage slot; inserting
a plate (ballistic / trauma / diffusion / speed) makes it the active plate, absorbing damage at the
cost of plate durability and wearer stamina. Plates are researched through `UniversalArmorPlatesT1/T2`
(`NFArsenalMercenary`, positions 3,45 and 5,45), printed from the `ArmorNF` lathe category
(`NFMercenaryArchetypeStatic` pack) and available as blueprints (`NFBlueprintsMercenary`). Each plate
also carries a `PhysicalComposition` (materials from its own recipe) so the material reclaimer can
recycle it. Monolith materials that do not exist here were substituted (Plastitanium -> Plasteel,
Copper -> Silver, Iridite -> Diamond). Tests: `Tests/_Mono/ArmorPlateTest.cs` and the general
`MaterialArbitrageTest`.
