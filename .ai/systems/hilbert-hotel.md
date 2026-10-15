# System: Hilbert's Hotel (`_PS`)

## Purpose

Hilbert's Hotel is a Palmtree feature ported from SPLURT-tg's newer "condo"
implementation (not the old SPLURT-Station-13 ruin). It gives players private,
runtime-loaded rooms for ERP:

- A check-in terminal teleports a player into a **private room map**.
- Every room has a **shareable numeric code**. Whoever knows the code can check
  into the same room from any terminal.
- A **room controller** inside the room lets the owner lock the room
  (`Open` → `Guests Only` → `Locked`), hide it from the public listing, and
  manage trusted guests / ownership.
- Rooms are **loaded on demand and unloaded** when empty, like expeditions.

## Locations

| Layer | Path |
|---|---|
| Shared prototype/components/UI types | `Content.Shared/_PS/HilbertHotel/` |
| Server logic | `Content.Server/_PS/HilbertHotel/` |
| Client UI | `Content.Client/_PS/HilbertHotel/` |
| Entity prototypes | `Resources/Prototypes/_PS/Entities/Structures/Machines/hilbert_hotel.yml` |
| Room archetypes | `Resources/Prototypes/_PS/HilbertHotel/rooms.yml` |
| Room maps | `Resources/Maps/_PS/HilbertHotel/` |
| Locale | `Resources/Locale/en-US/_PS/hilbert-hotel.ftl` |
| Map generator (reference) | `Tools/gen_hilbert_room.py` |
| Tests | `Content.IntegrationTests/Tests/_PS/HilbertHotelTest.cs` |

## Components

| Component | Prototype | Purpose |
|---|---|---|
| `HilbertHotelTeleporterComponent` | `HilbertHotelTeleporter` | Check-in terminal BUI; `MaxRooms`, `ExitPrototype`, `ControllerPrototype` data fields |
| `HilbertHotelRoomControllerComponent` | `HilbertHotelRoomController` | In-room BUI; `MaxGuests` |
| `HilbertHotelExitComponent` | `HilbertHotelExit` | Fake door that teleports the user back to their entry point |
| `HilbertHotelLandingMarkerComponent` | `HilbertHotelLandingMarker` | Marks where arrivals spawn inside a room map |

## Prototype

`HilbertHotelRoomPrototype` (`hilbertHotelRoom`) describes a room archetype:

```yaml
- type: hilbertHotelRoom
  id: CozyHotelRoom
  name: hilbert-hotel-room-cozy   # LocId, defined in hilbert-hotel.ftl
  mapPath: /Maps/_PS/HilbertHotel/room_cozy.yml
  order: 0                        # selection-list ordering
  # landingOffset: 0,0            # optional offset from the resolved landing point
```

Add new rooms following `.ai/guides/adding-hotel-rooms.md`.

## Runtime model

`HilbertHotelSystem` (server) owns a `Dictionary<int, HilbertHotelRoom>` keyed by
room code:

1. **Check-in** (`TryCheckIn`): validates range/alive/code. If a room with the
   code exists, access is checked (`CanJoin`: owner always; `Open` anyone;
   `GuestsOnly` owner + trusted; `Locked` owner only) and the player is moved to
   the room's `Landing`. Otherwise a new room is created — but only if the player
   does **not already own** a room (one room per player), the **terminal itself**
   is under its `MaxRooms` limit, and its 5-second creation cooldown has passed.
2. **Creation** (`TryCreateRoom`): `MapLoaderSystem.TryLoadMap` loads the
   archetype map *paused and uninitialized*; the system runs
   `SharedMapSystem.InitializeMap` to fire map init and unpause. Landing is
   resolved from (in order): `HilbertHotelLandingMarker`, a latejoin
   `SpawnPointComponent`, any non-observer spawn point, or the first grid's
   center. Missing exit/controller entities are spawned at the landing point.
3. **Occupancy sweep** (`Update`, every 5s): when no living mob (player, pet,
   NPC — ghosts and dead bodies excluded) is on the map, an empty timer starts.
   If it stays empty for 5 minutes the map (and anything left inside) is
   deleted. Rejoining cancels the timer. Rooms are deliberately **never
   paused**: freezing a map also freezes any player who returns to their body
   inside it (e.g. after admin ghosting).
4. **Exit** (`TryLeaveRoom`): teleports the user to the position they checked in
   from, falling back to the terminal's location, then the default map.
5. **Delete** (`DeleteRoom`): refused while any living mob is inside (pets
   included); the deleting owner does not count. On success it ejects every
   non-ghost player to their entry point, evacuates ghosts, then deletes the
   map. Reachable from the in-room controller's "Delete room" button and from
   the check-in terminal's Delete button on a room the viewer owns.
6. **Round restart**: `RoundRestartCleanupEvent` clears the room registry (the
   engine deletes all maps anyway).

Ghosts are evacuated before a room map is deleted so they are not destroyed.

## Locking / sharing

- The **code** is the share mechanism: "I'm in room 4242, come join."
- `HilbertHotelRoomStatus` is `Open`, `GuestsOnly` or `Locked`; the controller
  button cycles through them. `Locked` is what players mean by "lock my room".
- Trusted guests are stored by `NetUserId` (session identity) with a cached
  display name, so they survive character respawns within the round.
- `Visible` controls whether the room appears in the terminal's public listing.
  The owner always sees their own room.

## UI

Both UIs use per-actor `BoundUserInterfaceMessage`s (not global `SetUiState`),
because access flags (`CanJoin`, `IsOwner`) differ per viewer:

- `HilbertHotelTeleporterStateMessage` / `HilbertHotelRoomControllerStateMessage`
  carry the lists. The server sends them on `BoundUIOpenedEvent` and whenever
  room state changes (`NotifyRoomChanged`).
- Client windows are plain XAML (`DefaultWindow`); the SS13 tgui theme was
  deliberately not recreated.

## Room content

The built-in archetypes are:

| Prototype | Map | Source |
|---|---|---|
| `CozyHotelRoom` | `room_cozy.yml` | hand-generated by `Tools/gen_hilbert_room.py` |
| `HotelSuiteRoom` | `room_hotel.yml` | SPLURT-tg `hilbertshotel.dmm` |
| `PoolsideSuiteRoom` | `room_suite.yml` | SPLURT-tg `gm_condo.dmm` |
| `CabinRoom` | `room_cabin.yml` | SPLURT-tg `cabin_woods.dmm` |
| `BeachCondoRoom` | `room_beach.yml` | SPLURT-tg `beach_condo.dmm` |
| `LibraryRoom` | `room_library.yml` | SPLURT-tg `public_library.dmm` |
| `ApartmentRoom` | `room_apartment.yml` | SPLURT-tg `apartment.dmm` |
| `DebugStationHotelRoom` | `/Maps/Test/dev_map.yml` | test-only fallback landing |

The SPLURT rooms were converted from BYOND `.dmm` maps with
`Tools/convert_dmm_room.py`. It maps tiles, walls, windows, doors, the hotel
exit/controller and a curated set of props that already exist in SS14 (beds,
tables, chairs, closets, toilets, sinks, mirrors, bookshelves, dressers,
railings, fireplaces, showers, carpets, water). Unsupported props (flora,
decals, lights, BYOND-only machines) are skipped and reported. The shower is
the one ported `_HL` prop (prototype + sprite + audio); everything else uses
SS14-native art.

## Traps

- `TryLoadMap` leaves runtime maps **paused and uninitialized** by default. The
  system calls `InitializeMap` itself; do not pass `InitializeMaps = true` and
  then call it again (it throws).
- Room maps should be **format 7 map files** (`savemap`, not `savegrid`) so they
  contain a `MapComponent`.
- Room maps need a breathable atmosphere. `room_cozy.yml` and the converted
  maps ship a serialized `GridAtmosphere` v2 block (see the generators); maps
  without one are vacuum and players will suffocate.
- `MapAtmosphereComponent` only applies to tiles whose tile definition has
  `isSpace: true` (space/lattice), not interior floors. Do not rely on it for
  indoor rooms.
- The hotel exit door **must be airtight** (it replaces a wall tile in room
  maps); otherwise the room depressurizes through the doorway. `HilbertHotelExit`
  carries an `Airtight` component for this reason. The
  `AllRoomArchetypesLoadAndHoldAir` test runs the atmos simulation for every
  archetype and catches leaks.
- Deleting a room map deletes everything on it. The occupancy check only counts
  non-ghost players; bodies left by disconnected players are eventually deleted
  with the room (SS13 behaves the same way).
- **Never pause a room map.** Pausing also sets `EntityPaused` on the player's
  body; when an admin ghost returns to it the player can be left in a frozen
  map until (or beyond) the next occupancy sweep. Rooms stay live for their
  5-minute grace period instead.
- Room maps must set `Gravity: enabled: true, inherent: true` on the grid,
  otherwise players float. `GravityComponent.Enabled` defaults to **false**.
- Hotel rooms have no power grid. Machines that inherit `ApcPowerReceiver`
  (microwaves, fridges, jukeboxes, vending machines, toilets) are emitted with
  `needsPower: false` so they appear powered without an APC.
- Props placed in room maps must be anchored (`anchored: true`); some SS14
  prototypes (e.g. `SmartFridge`, potted plants) default to dynamic and would
  otherwise be pushable.
- `HilbertHotelRoomController` has no fixtures/`canCollide` so it can never
  block a doorway or trap a player who lands on its tile.
- Rooms have **no power grid**: every machine that inherits `ApcPowerReceiver`
  is emitted with `needsPower: false` (self-powered). Lights use
  `AlwaysPoweredWallLight`, which needs no APC at all.
- The converter maps BYOND `/obj/machinery/light` fixtures to
  `AlwaysPoweredWallLight`, and resolves furniture collisions: only one large
  furniture prop survives per tile (priority: bed > toilet/sink > window/door >
  table/bookshelf > chair > closet). Floor decor (carpets, water), wall decor
  (mirrors, lights, curtains, noticeboards) and structural entities are never
  culled; exact duplicates (two windows on one tile) are.
- The public listing shows `Occupants` by scanning `ActorComponent` entities per
  room; it deliberately excludes ghosts.

## Tests

`HilbertHotelTest` covers: room archetype prototypes, loading `room_cozy.yml`
with fixtures + air, loading the debug station map, a full
check-in → controller state → exit flow using a connected test client,
visibility/access rules, one-room-per-owner plus terminal deletion, a
ghost-out/return regression (rooms must not be left paused), a delete guard
test (a cat inside blocks deletion and keeps the room alive past the empty
timeout), and an atmos soak test that loads every archetype, runs 200 ticks and
asserts none of the rooms leak to vacuum, that every room grid has gravity, that
every placed prop is anchored and that powered machines are self-powered.
