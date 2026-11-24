#!/usr/bin/env python3
"""Generates Resources/Maps/_PS/HilbertHotel/room_cozy.yml.

A small 11x9 wood-floored hotel room with:
  - a landing marker for arrivals,
  - an exit door on the south wall,
  - a room controller,
  - a bed, table and chair,
  - pre-filled breathable grid atmosphere (format 7 GridAtmosphere v2).

This is a one-off generator kept in the repo docs for reference; the map itself is
committed. Regenerate with `python3 Tools/gen_hilbert_room.py`.
"""

import base64
import struct
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
OUT = REPO / "Resources/Maps/_PS/HilbertHotel/room_cozy.yml"

# Room bounds (tile coordinates, inclusive). Interior is 1..width-2 x 1..height-2.
WIDTH = 11   # x = 0..10
HEIGHT = 9   # y = 0..8

TILE_SPACE = 0
TILE_WOOD = 1

EXIT_TILE = (5, 0)

# --- tiles ------------------------------------------------------------------
tiles = bytearray(16 * 16 * 7)


def set_tile(x: int, y: int, tile_id: int) -> None:
    offset = (y * 16 + x) * 7
    struct.pack_into("<iBBB", tiles, offset, tile_id, 0, 0, 0)


for y in range(HEIGHT):
    for x in range(WIDTH):
        set_tile(x, y, TILE_WOOD)

tiles_b64 = base64.b64encode(bytes(tiles)).decode()

# --- atmosphere -------------------------------------------------------------
# Same standard mix used by station maps: ~101 kPa of 21% O2 / 79% N2 at 20C.
# The list predates the ten /tg/ gases (enum ids 11-20), which append after the vanilla ids and
# default to zero here; GasMixture.AfterDeserialization pads a shorter list to
# Atmospherics.AdjustedNumberOfGases (currently 24), so this list need not grow.
MOLES = [21.824879, 82.10312, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
CHUNK = 4

chunks: dict[tuple[int, int], int] = {}
for y in range(HEIGHT):
    for x in range(WIDTH):
        origin = (x // CHUNK, y // CHUNK)
        rel = (x % CHUNK, y % CHUNK)
        chunks[origin] = chunks.get(origin, 0) | (1 << (rel[0] + rel[1] * CHUNK))

# --- entities ---------------------------------------------------------------
entities: list[tuple[str, float, float, float | None]] = []


def add(proto: str, x: float, y: float, rot: float | None = None) -> None:
    entities.append((proto, x, y, rot))


# Walls around the perimeter, skipping the exit tile.
for x in range(WIDTH):
    for y in (0, HEIGHT - 1):
        if (x, y) != EXIT_TILE:
            add("WallWood", x + 0.5, y + 0.5)
for y in range(1, HEIGHT - 1):
    for x in (0, WIDTH - 1):
        add("WallWood", x + 0.5, y + 0.5)

# Room fixtures and furniture.
add("HilbertHotelExit", EXIT_TILE[0] + 0.5, EXIT_TILE[1] + 0.5)
add("HilbertHotelRoomController", 1.5, HEIGHT - 1.5)
add("HilbertHotelLandingMarker", WIDTH // 2 + 0.5, 3.5)
add("Bed", 2.5, HEIGHT - 2.5)
add("TableWood", WIDTH - 2.5, HEIGHT - 2.5)
add("ChairWood", WIDTH - 2.5, HEIGHT - 3.5)
# Wall lights (always powered so the room needs no APC).
add("AlwaysPoweredWallLight", 4.5, HEIGHT - 1.5, 3.141592653589793)
add("AlwaysPoweredWallLight", 6.5, 1.5, 0.0)

# --- yaml -------------------------------------------------------------------
lines: list[str] = []
lines.append("meta:")
lines.append("  format: 7")
lines.append("  category: Map")
lines.append("  engineVersion: 266.0.0")
lines.append('  forkId: ""')
lines.append('  forkVersion: ""')
lines.append("  time: 10/03/2026 00:00:00")
lines.append(f"  entityCount: {2 + len(entities)}")
lines.append("maps:")
lines.append("- 1")
lines.append("grids:")
lines.append("- 2")
lines.append("orphans: []")
lines.append("nullspace: []")
lines.append("tilemap:")
lines.append("  0: Space")
lines.append("  1: FloorWood")
lines.append("entities:")
lines.append('- proto: ""')
lines.append("  entities:")
lines.append("  - uid: 2")
lines.append("    components:")
lines.append("    - type: MetaData")
lines.append("      name: hotel room")
lines.append("    - type: Transform")
lines.append("      parent: 1")
lines.append("    - type: MapGrid")
lines.append("      chunks:")
lines.append("        0,0:")
lines.append("          ind: 0,0")
lines.append(f"          tiles: {tiles_b64}")
lines.append("          version: 7")
lines.append("    - type: Broadphase")
lines.append("    - type: Physics")
lines.append("      bodyStatus: InAir")
lines.append("      angularDamping: 0.05")
lines.append("      linearDamping: 0.05")
lines.append("      fixedRotation: False")
lines.append("      bodyType: Dynamic")
lines.append("    - type: Fixtures")
lines.append("      fixtures: {}")
lines.append("    - type: OccluderTree")
lines.append("    - type: GridPathfinding")
lines.append("    - type: Gravity")
lines.append("      gravityShakeSound: !type:SoundPathSpecifier")
lines.append("        path: /Audio/Effects/alert.ogg")
lines.append("      enabled: True")
lines.append("      inherent: True")
lines.append("    - type: DecalGrid")
lines.append("      chunkCollection:")
lines.append("        version: 2")
lines.append("        nodes: []")
lines.append("    - type: SpreaderGrid")
lines.append("    - type: ImplicitRoof")
lines.append("    - type: GridAtmosphere")
lines.append("      version: 2")
lines.append("      data:")
lines.append("        tiles:")
for (cx, cy), mask in sorted(chunks.items()):
    lines.append(f"          {cx},{cy}:")
    lines.append(f"            0: {mask}")
lines.append("        uniqueMixes:")
lines.append("        - volume: 2500")
lines.append("          temperature: 293.15")
lines.append("          moles:")
for mole in MOLES:
    lines.append(f"          - {mole}")
lines.append("        chunkSize: 4")
lines.append("  - uid: 1")
lines.append("    components:")
lines.append("    - type: MetaData")
lines.append("      name: Hilbert Hotel Room")
lines.append("    - type: Transform")
lines.append("    - type: Map")
lines.append("      mapPaused: True")
lines.append("    - type: GridTree")
lines.append("    - type: Broadphase")
lines.append("    - type: OccluderTree")
lines.append("    - type: MapLight")
lines.append("      ambientLightColor: '#D8B059FF'")

# Group entities by prototype, assigning uids from 3 upwards.
groups: dict[str, list[tuple[int, float, float, float | None]]] = {}
uid = 3
for proto, x, y, rot in entities:
    groups.setdefault(proto, []).append((uid, x, y, rot))
    uid += 1

for proto, entries in groups.items():
    lines.append(f"- proto: {proto}")
    lines.append("  entities:")
    for ent_uid, x, y, rot in entries:
        lines.append(f"  - uid: {ent_uid}")
        lines.append("    components:")
        lines.append("    - type: Transform")
        if rot is not None:
            lines.append(f"      rot: {rot}")
        lines.append(f"      pos: {x},{y}")
        lines.append("      parent: 2")
        lines.append("      anchored: True")

lines.append("...")

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text("\n".join(lines) + "\n")
print(f"Wrote {OUT} ({len(entities)} entities, {OUT.stat().st_size} bytes)")
