# Bluespace Dock + Ship Registrar (2026-10)

**Status**: **implemented** (2026-10) on `master` (merged from the `ps-bluespace-dock` work). Durable notes live in
`.ai/systems/ps-systems.md` ("Bluespace Dock + Ship Registrar"); this file keeps the design record
and the deviations from the plan.
**Scope**: two new admin/SR consoles plus a live ship preview:

1. **Bluespace Dock** (`ComputerBluespaceDock`): pay a configurable tax to store your docked,
   civilian ship as a file in `UserData`; pay a second tax to pull it back out; sell stored ships;
   list/search your character's stored ships.
2. **Ship Registrar** (`ComputerShipRegistrar`, Station Representative + admins): turn a docked,
   unregistered grid (player-built ship) into a proper vessel - station, deed, records entry, IFF
   name, and a civilian "registered" marker the Bluespace Dock accepts.
3. **Ship preview**: a `_PS` camera EUI (the admin-camera mechanism) so both consoles can show a
   live view of a grid - unnamed grids all show up as "grid" on the scanner, so a preview is how a
   player identifies their ship.

**Touched areas**: `Content.Shared/_PS/BluespaceDock`, `Content.Server/_PS/BluespaceDock`,
`Content.Client/_PS/BluespaceDock`, `Content.Shared/_PS/ShipRegistry`,
`Content.Server/_PS/ShipRegistry`, `Content.Client/_PS/ShipRegistry`,
`Resources/Prototypes/_PS/Entities/Structures/Machines/Computers`,
`Resources/Textures/_PS/Structures/Machines`, `Resources/Locale/en-US/_PS`,
`Content.Shared/_NF/CCVar/NFCCVars.cs` (Palmtree CVars), integration tests under
`Content.IntegrationTests/Tests/_PS`.

## Source / prior art

Coyote implemented a prototype of this: `Content.Server/_NF/Shipyard/Systems/BluespaceDrydockSystem.cs`
(PR #346 by drraccoony, 2025-11; refactored 2026-04 by Neenner). It was never finished as a player
feature: the dedicated console is placed on no map and craftable nowhere, the player-facing path was
bolted onto the shuttle records console behind the `nf14.shuttle.bluespace_docking_enabled` CVar,
storage lived as a YAML string on an ID card / shuttle record (round-scoped, one ship per ID), there
were no costs, no civilian check, no list/sell, hardcoded popups and a 3-second timer hack in the
records path.

We keep their **proven mechanics** and drop their storage model:

- `MapLoaderSystem.TrySaveGrid(grid, path, options)` writes into `IResourceManager.UserData`
  (`MapLoaderSystem.cs` `Write()`), and loading checks `UserData` before content paths
  (`MapLoaderSystem.cs` `TryReadFile()`). So ships can be saved to and loaded from `UserData`.
- Strip the deed and the station references (`StationMemberComponent`, `StationBoundObjectComponent`)
  before saving: the station is mapped and the file cannot carry it, and invalid station uids make
  the deserializer log errors and `MapInit` handlers (holopad rename) throw on load. Delete the
  station and the grid only after the file is known-good.
- `SerializationOptions { MissingEntityBehaviour = IncludeNullspace, ErrorOnOrphan = false }` keeps
  information-carrying nullspace entities; fall back to `Ignore` (drops dangling references) when
  that fails. Pre-steps that make the save clean: power down fuel generators + gravity generators,
  **delete the ship's station** (stations are nullspace entities and would otherwise be serialized
  into the ship file), and undock the ship (docking joints/DockedWith references are runtime state).
- On load, `MapInitEvent` must be re-raised for the deserialized entities **except** powered lights,
  item slots and docking ports (re-initializing those duplicates bulbs/items/joints).
- After loading: create the ship's station (`StandardFrontierVessel`), rename it, `TryFTLDock` to
  the console's station (docks if a compatible dock is free, otherwise spawns nearby), recreate the
  deed on the grid and the ID, and add `DeletionCensusExemptComponent` +
  `LinkedLifecycleGridParentComponent` like the shipyard purchase does.

## Locked decisions (maintainer)

- **Ownership**: per character - registry entries keyed by `ckey` (NetUserId) **and** character name
  (both must match). Another character of the same player does not see those ships.
- **Store tax**: 10% of a fresh `PricingSystem.AppraiseGrid` (CVar, default 0.10).
- **Retrieve tax**: 5% of the value recorded at storage time (CVar, default 0.05).
- **Stored sell**: sells the stored ship for its recorded value minus the shipyard base sale rate
  (CVar `nf14.bluespace_dock.stored_sell_rate`, default 0.95 - mirrors `shipyard_base_sell_rate`),
  deletes the file and the entry. `ConfirmButton` (red, second press confirms).
- **Columns**: `Name` = deed full name (`Dove QT-723`); `Model` = `VesselPrototype.Name` exactly as
  the shipyard purchase menu shows it.
- **Civilian check**: allow `VesselPrototype.Group` in `{Shipyard, Scrap, Expedition}`; deny
  `Security` (NFSD), `BlackMarket` and everything else (Sr, Medical, Syndicate, Centcomm, Custom).
  **No vessel identity at all -> refuse** (unregistered player-built ships must go through the
  registrar first).
- **Retrieve placement**: `TryFTLDock` - dock if possible, otherwise spawn nearby (shipyard
  behaviour). Tax is charged only after a successful spawn.
- **Console**: admin-spawn only (`suffix: Palmtree`, no lathe/cargo/blueprint), indestructible,
  un-pryable, requires power, single user UI, ID card slot.
- **Code/assets**: `_PS`; the console sprite is a **copy** of the shipyard console states under
  `Resources/Textures/_PS/Structures/Machines/` (never point at `_NF`'s computers.rsi).
- **Registrar**: privileged ID (SR access; admins bypass) + target ID, list of docked unregistered
  grids, live preview, custom name + suffix, `ConfirmButton`; fee CVar default 0.
- **Grid name bloat** (unnamed grids flooding the scanner/IFF with "grid") is a separate follow-up,
  not part of this batch.

## Data model

`UserData/bluespace_dock/`:

- `registry.yml` - authoritative list of entries (serialized with `ISerializationManager` from
  `[DataDefinition]` records):
  `{ ShipId, File (relative path), OwnerUserId, OwnerName, Vessel (proto id or null), Model,
    DisplayName, RegistrationSuffix, Value, StoredAtUtc }`.
- `<ckey>/<shipId>.yml` - the ship grid file written by `MapLoaderSystem.TrySaveGrid`.

The registry is the source of truth: a missing/unreadable ship file purges its entry (self-heal,
logged + popup); files are only the payload.

## Checks

Store (Dock current ship): ID inserted; ID has a deed with a live shuttle; the shuttle is docked to
this console's station (shipyard logic); no living thing aboard (`ShipyardSystem.FoundOrganics`);
vessel identity resolves to an allowed group; bank covers the tax; not already being processed.
Then: pre-steps -> serialize -> read the file back and YAML-parse it -> only then charge, write the
registry entry, remove the deed and delete the ship. Any failure aborts before charging/deleting and
best-effort restores the station/name.

Retrieve: entry exists (and belongs to this character); file readable; bank covers the tax; no other
ship with the same identity afloat; load + fix-ups + station + FTL dock; then charge, remove the
entry (file is deleted for a stored sell only), popup.

Stored sell: entry exists and belongs to this character; then charge nothing, deposit
`value * stored_sell_rate`, delete file + entry.

Registrar: privileged ID has SR access (admins bypass); target ID inserted; selected grid is docked
to the station; grid has no deed/record/station vessel identity; fee affordable; name valid
(length/sanitized); then create station + `ExtraShuttleInformationComponent`, deed on grid + target
ID, `ShuttleRecord`, IFF name, `DeletionCensusExempt` + `LinkedLifecycleGridParent`, and stamp
`RegisteredVesselComponent { Group = Civilian, Model = "Custom" }`.

## UI

Bluespace Dock (mockup from the maintainer): title, `Welcome back, <ID name>!`, search box,
`Name | Model | Options` header, rows with `[UNDOCK] [SELL]` (ConfirmButton), bottom
`[DOCK CURRENT SHIP] $tax` + `[SELL]` + ID slot button, centred `INSERT ID` when the slot is empty.

Ship Registrar: privileged + target ID slots, search + list of docked unregistered grids
(`Name | Size | Value | [SELECT] [PREVIEW]`), selected-grid summary, fee/balance, name + suffix
fields, `[REGISTER SHIP]` ConfirmButton, `INSERT ID` placeholder.

Ship preview: separate `_PS` EUI window (a copy of the admin-camera mechanism: invisible `Eye`
camera entity at the ship + `ViewSubscriberSystem.AddViewSubscriber`, client `CameraView` +
`EyeLerpingSystem.AddEye`, mouse-wheel zoom, no follow button).

## Validation

- `dotnet build SpaceStation14.sln`, YAMLLinter (rebuild first), headless client sandbox typecheck.
- `Content.Tests`.
- Integration tests: `Tests/_PS/BluespaceDockTest.cs` (registry round-trip + grid save/load
  round-trip + self-heal + vessel group resolution) and `Tests/_PS/ShipRegistrarTest.cs`
  (registration creates station/deed/record/marker; preview camera spawns/cleans up).
- Manual: spawn the consoles, dock a civilian ship, store/retrieve/sell; register a built grid,
  preview it, store it afterwards.
