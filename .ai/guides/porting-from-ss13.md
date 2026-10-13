# Guide: Porting SS13 (BYOND) Content to SS14

> How to take features, maps and art from the BYOND SS13 forks (paths in `.ai/file_paths.md`) and turn
> them into SS14 content. This is a different process from `porting-from-coyote.md` (SS14 → SS14). The
> Hilbert's Hotel port is the worked example; see `.ai/systems/hilbert-hotel.md`.

## 1. Source checkouts

Checkouts: the `BYOND` directory (see `.ai/file_paths.md` for the machine-specific path; it contains
spaces, so quote it in shell commands).

| Checkout | What it is |
|---|---|
| `S.P.L.U.R.T-tg` | Newest SPLURT, based on modern /tg/station. Has the newer Hilbert's Hotel "condo" system. |
| `S.P.L.U.R.T-Station-13` | Old SPLURT on old TG. Has the older Hilbert's Hotel ERP variant and apartment templates. |
| `Sandstorm-Station-13` | Sandstorm fork with its own `.ai/` docs. Vanilla Hilbert's Hotel is a space ruin here. |

Where features live in an SS13 fork:

- `modular_*` folders are fork additions (`modular_zubbers`, `modular_zzplurt`, `modular_splurt`,
  `modular_sand`, ...); core /tg/ code is in `code/`.
- Search: `grep -ril "feature" <checkout> --include="*.dm"`.
- Map templates: `_maps/**/*.dmm`; `/datum/map_template` subtypes carry `mappath`.
- The `.dme` file lists what gets compiled; fork includes are appended there.

## 2. `.dmm` maps

The condo maps use the **TGM** variant (header `//MAP CONVERTED BY dmm2tgm.py ...`):

- Dictionary at the top: `"key" = (/turf/..., /obj/...{dir = 4; pixel_x = 2}, /area/...)`.
- Grid blocks: `(x,y,z) = {"` followed by **one key per line**, closed by `"}`. Keys are
  variable-length — do not assume fixed-width rows, or the parse produces alternating garbage.
- Atom variables in `{...}` can contain nested parentheses; parse them with depth counting.
- Normalize coordinates by min x/y. BYOND y is up, same as SS14. Ignore `/area` atoms.
- Fork turfs seen in practice: `/turf/open/indestructible/hotelwood`, `.../hoteltile`, `.../dark`,
  `.../bathroom`, `/turf/closed/indestructible/hotelwall`, `.../hoteldoor` (+ `/fakedoor`),
  `.../fakeglass`, `/turf/open/space/bluespace`, `/turf/cordon` (map-edge barrier) and
  `/turf/template_noop` (leave existing turf as-is).
- Wall-mounted atoms encode facing in the path (`/obj/machinery/light/warm/directional/west`,
  `/obj/machinery/shower/directional/east`, `/obj/machinery/room_controller/directional/north`);
  some atoms use a `dir` variable instead.
- Multiple atoms per tile are normal (carpet + table + items). Multi-tile objects exist but the
  converter places them on a single tile.

### Converter tool: `Tools/convert_dmm_room.py`

```
python3 Tools/convert_dmm_room.py <input.dmm> <output.yml> --name "Room Name"
```

What it does and why:

- Reads prototype files with `utf-8-sig` (many start with a BOM; a plain UTF-8 read breaks naive
  block parsers) and resolves concrete (non-abstract) prototypes.
- Resolves `ApcPowerReceiver` through prototype `parent` chains, so any mapped machine gets
  `needsPower: false` (rooms have no APC).
- Mapping tables: `FLOOR_TILES`, `WALL_PROTOS`, `WINDOW_TURFS`, `EXIT_TURFS`, `CARPET_PROTOS`,
  `WATER_TURFS`, `OBJECT_PROTOS` (longest-prefix match), `SKIP_OBJECT_PREFIXES`,
  `DIRECTIONAL_PROTOS`, `FURNITURE_PROTOS` + `PROP_PRIORITY`.
- Directions: `DIR_TO_ROT` = north π, south 0, east π/2, west -π/2. Applied only to
  `DIRECTIONAL_PROTOS`; entities with `Transform noRot` must not be rotated (a load-time
  `DebugAssertException: NoRot entity has a non-zero local rotation` otherwise).
- Overlap resolution: only one large furniture prop survives per tile (priority: bed > toilet/sink >
  window/door > table/bookshelf > chair > closet); structural entities (walls, windows, doors,
  controllers, exits) are never culled; exact duplicates (two windows on one tile) are deduped.
- Landing marker: BFS from the first exit to the nearest **entity-free** floor tile. A mapped
  controller next to the exit is relocated; the SS14 controller is also non-blocking.
- Every emitted entity gets `anchored: True`, and the grid gets `Gravity` (enabled/inherent),
  `MapLight` and a standard-air `GridAtmosphere`.
- Prints every unmapped turf/object; use that report to extend the tables.

## 3. SS14 map format facts the generator relies on

- Format 7, `category: Map`, must contain a `MapComponent` — save with `savemap`, never `savegrid`.
- Tiles: 16x16 chunks; 7 bytes per tile (`int32` YAML tile id + flags + variant +
  rotationMirroring), base64, row-major. `tilemap` maps YAML ids to tile prototypes (0 = Space).
- `GridAtmosphere` v2: `chunkSize: 4`, `uniqueMixes` (volume, temperature, `moles` with
  `Atmospherics.AdjustedNumberOfGases` = 12 values) and a bitmask per mix per 4x4 chunk. This is
  the reliable way to give an indoor runtime-loaded map air: interior floors are not `isSpace`, so
  a `MapAtmosphereComponent` alone does not pressurize them (only Space/lattice tiles use it).
- Grid components to include: `MapGrid`, `Broadphase`, `Physics`, `Fixtures`, `OccluderTree`,
  `GridPathfinding`, `Gravity` (**`enabled: true`, `inherent: true` — the component defaults to
  false**), `DecalGrid`, `SpreaderGrid`, `ImplicitRoof`, `GridAtmosphere`.
- Map components: `MetaData`, `Transform`, `Map` (`mapPaused: True`), `GridTree`, `Broadphase`,
  `OccluderTree`, `MapLight`.
- Walls, windows and exits must be airtight; an exit door that replaces a wall tile needs
  `Airtight` (`noAirWhenFullyAirBlocked: false` if a player can land on it), or the room leaks.
- `MapLoaderSystem.TryLoadMap` leaves maps **paused and uninitialized**; call
  `SharedMapSystem.InitializeMap` yourself. Do not pass `InitializeMaps = true` and call it again
  (throws).

## 4. SS13 concept → SS14 mapping

| SS13 | SS14 |
|---|---|
| `SSmapping.request_turf_block_reservation` + `map_template.load` | one runtime-loaded map per room (`MapLoaderSystem.TryLoadMap`) |
| `area/Exited` last-mind check | periodic occupancy sweep over `MobStateComponent`/`ActorComponent` (ghosts and dead don't count) |
| `storeRoom` / conserved rooms (contents kept, deleted later) | keep the loaded map for a grace period (5 min), then delete |
| `SScondos.item_blacklist` | not ported; items are deleted with the room |
| numeric room number | shareable `int` room code (1..999999) |
| `room_controller` TGUI | XAML BUI + per-actor `BoundUserInterfaceMessage`s (state differs per viewer) |
| `requires_power = FALSE` areas | `needsPower: false` on APC machines, `AlwaysPowered*` lights |
| `default_gravity = STANDARD_GRAVITY` | grid `Gravity: enabled: true, inherent: true` |
| bluespace storage box / cryopod departure | not ported |

## 5. Sprites and audio

- BYOND `.dmi` is a PNG with a `zTXt` metadata chunk (frame grid, states, directions, delays).
  SS14 `.rsi` is a directory with `meta.json` plus one PNG per state.
- **DMI layout** (verified against DMISharp, `github.com/bobbah/DMISharp`): the sheet is a grid of
  `size` cells (`dimX = width / size.x`). It is read in row-major order, and each state consumes
  `dirs * frames` consecutive cells in **frame-major, direction-minor** order (for frame 0:
  south, north, east, west; then frame 1, ...). A state's cells may wrap across sheet rows.
  Consequence: a 4-direction state stored as 4 cells wide (or as a 2×2 block) is still valid in
  `.rsi` — Robust slices with `dimX = image.Width / meta.size.x`, so directions do not have to be
  stacked vertically in the file.
- **RSI cell order (not just the grid shape)**: the engine reads each state image as flat
  row-major cells where direction 0's frames come first, then direction 1's, etc. — i.e. cell
  `dir * frames + frame`. Rows must therefore be *directions* (S, N, E, W), with frames along X;
  writing directions along X and frames along Y (transposed) loads without errors but breaks every
  facing except the first. `RsiDirection` values are South=0, North=1, East=2, West=3.
- **Trap**: the metadata is authoritative and can contain an **empty state name** (`state = ""`)
  that still consumes cells. Any parser that skips it shifts every later state and silently
  extracts the wrong sprites. Match state names with `state = "([^"]*)"` and count its
  `dirs * frames` cells.
- Validate conversions against DMISharp (`dotnet` + NuGet) or RSIEdit; RSIEdit is GUI-only, but a
  ~30-line Python zTXt/Pillow script plus DMISharp diffing has worked for the Soviet set and
  RD beret ports.
- When copying art, keep the `meta.json` license/copyright and audio `attributions.yml`.

## 6. SS14 → SS14 fork content (e.g. the Coyote `_HL` shower)

Even when the source is another SS14 fork, the same classification applies:

- Check every component/field against this repo and apply the 2023→2026 renames from
  `porting-from-coyote.md`.
- Inline unrelated base prototypes instead of dragging in a whole subsystem (the shower inlined
  `BaseSinkUnfinished` into `ShowerUnfinished`).
- Drop construction graph nodes whose dependencies do not exist here (plumbing).
- Keep the source prefix for provenance (`_HL`); add new prefixes to the README table.
- Relevant components that exist here: `ItemToggle`, `ItemToggleActiveSound`, `ToggleableVisuals`,
  `UseDelay`, `GenericVisualizer`, `Rotatable`. `Destructible` is server-only. `BaseStructure` has
  `Anchorable`; `BaseStructureDynamic` is unanchored and dynamic.

## 7. Validation

- `Content.YAMLLinter` does **not** validate map files; missing or abstract prototypes only fail at
  load ("Missing prototype for map: X").
- Integration tests are the real gate: load every map, run atmos ticks, assert pressure, gravity,
  anchoring and power; `EntityTest` spawns every prototype.
- Visual check:
  `dotnet run --project Content.MapRenderer -c Debug -- --format png -o out -f Resources/Maps/.../room.yml`
  (`-f` wants filesystem-relative paths; the gameMap-prototype route additionally needs
  `minPlayers` and station grids).
- Full pipeline: solution build → rebuild `Content.YAMLLinter` → lint → unit tests → targeted
  integration tests → headless client typecheck.

## 8. Hazards and gotchas

- **`PopupClient` is a no-op on the server** (it exists for client-side prediction). Use
  `PopupEntity(message, entity, recipient)` for server-side popups; otherwise the popup silently
  never appears.
- Never pause a runtime map with a player body on it: `EntityPaused` freezes the body (this bit us
  with admin ghosting). Keep the map live and delete it later.
- `GravityComponent.Enabled` defaults to **false**.
- Machines need `needsPower: false` or an APC; resolve `ApcPowerReceiver` through prototype parents.
- Props default to dynamic/unanchored unless the prototype is a structure; emit `anchored: True`.
- Rotating a `noRot` entity in a map file triggers a load-time `DebugAssertException`.
- `savemap` writes to the server user-data directory (`bin/Content.Server/data`), and map loading
  **prefers user data over `Resources`** ("Reading map user data instead of content"). Copy saved
  files back and delete stale user-data copies.
- Prototype files can start with a UTF-8 BOM; parse them with `utf-8-sig`.
- Pooled integration tests reuse servers: clean up rooms/maps in `finally`, or state leaks (e.g.
  "you already own a room") break later tests.
- In-game editing workflow: `~` console, `mapping <id> <path>`, F5/F6/F8 panels, `fixgridatmos
  <grid>`, `savemap <id> <path>`, `rmmap <id>`.

## 9. Worked example: Hilbert's Hotel

- Sources: SPLURT-tg `modular_zubbers/code/modules/condos/` (newer condo system),
  `modular_zzplurt/code/modules/hilbertshotel/` and `condo_infinidorms*.dm` (SPLURT extensions);
  old variant in `S.P.L.U.R.T-Station-13/modular_splurt/code/modules/ruins/spaceruin_code/hilbertshotel.dm`;
  vanilla /tg/ ruin in `S.P.L.U.R.T-tg/code/modules/mapfluff/ruins/spaceruin_code/hilbertshotel.dm`.
- Result: `_PS/HilbertHotel` server/shared/client code, `_PS` prototypes, seven room maps under
  `Resources/Maps/_PS/HilbertHotel/` and the `Tools/convert_dmm_room.py` /
  `Tools/gen_hilbert_room.py` generators. Full system notes in `.ai/systems/hilbert-hotel.md`.
