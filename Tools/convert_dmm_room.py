#!/usr/bin/env python3
"""Converts a BYOND .dmm room map into an SS14 format 7 map file.

Built for the SPLURT-tg condo maps (`modular_zubbers/code/modules/condos/_maps/`)
so their layouts can be used as Hilbert's Hotel room archetypes. Only tiles,
walls, windows, doors and a curated set of furniture that already exists in SS14
are converted; unsupported props are skipped and reported.

Usage:
    python3 Tools/convert_dmm_room.py <input.dmm> <output.yml> [--name "Room Name"]

The output map gets:
  * a MapGrid with all tiles,
  * wall/window/door/furniture entities,
  * a HilbertHotelLandingMarker next to the first exit door,
  * a pre-filled breathable GridAtmosphere (format 7, v2),
  * a MapLight.
"""

import argparse
import base64
import re
import struct
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]

# ---------------------------------------------------------------------------
# .dmm parsing (TGM format: dictionary at the top, one key per line in the grid)
# ---------------------------------------------------------------------------

DICT_RE = re.compile(r'^"([^"]+)"\s*=\s*\((.*?)\)\s*$', re.M | re.S)
GRID_RE = re.compile(r'^\((\d+),(\d+),(\d+)\)\s*=\s*\{"\n(.*?)\n"\}', re.M | re.S)


def parse_dmm(text: str):
    """Returns (entries, cells) where entries maps key -> list of atom strings."""
    entries = {}
    for match in DICT_RE.finditer(text):
        key, body = match.group(1), match.group(2)
        atoms, depth, cur = [], 0, ""
        for ch in body:
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            if ch == "," and depth == 0:
                atoms.append(cur.strip())
                cur = ""
            else:
                cur += ch
        if cur.strip():
            atoms.append(cur.strip())
        entries[key] = atoms

    cells = {}
    for match in GRID_RE.finditer(text):
        x, y, z = int(match.group(1)), int(match.group(2)), int(match.group(3))
        if z != 1:
            continue
        for i, line in enumerate(match.group(4).split("\n")):
            key = line.strip()
            if key:
                cells[(x, y + i)] = key
    return entries, cells


def split_atom(atom: str):
    """Splits '/obj/foo{dir = 4; name = "x"}' into (path, vars dict)."""
    brace = atom.find("{")
    if brace == -1:
        return atom.strip(), {}
    path = atom[:brace].strip()
    body = atom[brace + 1 : atom.rfind("}")]
    variables = {}
    for part in body.split(";"):
        if "=" not in part:
            continue
        k, v = part.split("=", 1)
        variables[k.strip()] = v.strip().strip('"')
    return path, variables


# ---------------------------------------------------------------------------
# Mapping tables (BYOND -> SS14)
# ---------------------------------------------------------------------------

# Turfs that become a floor tile.
FLOOR_TILES = {
    "/turf/open/floor/wood": "FloorWood",
    "/turf/open/floor/wood/parquet": "FloorWoodLarge",
    "/turf/open/floor/wood/tile": "FloorWoodTile",
    "/turf/open/floor/wood/large": "FloorWoodLarge",
    "/turf/open/floor/wood/woodmosaic": "FloorWoodLarge",
    "/turf/open/indestructible/hotelwood": "FloorWood",
    "/turf/open/indestructible/dark": "FloorDark",
    "/turf/open/indestructible/hoteltile": "FloorShowroom",
    "/turf/open/indestructible/bathroom": "FloorWhite",
    "/turf/open/floor/grass": "FloorGrass",
    "/turf/open/misc/grass/jungle/station": "FloorGrassJungle",
    "/turf/open/floor/stone": "FloorGrayConcrete",
    "/turf/open/floor/iron/showroomfloor": "FloorShowroom",
    "/turf/open/floor/iron/kitchen": "FloorKitchen",
    "/turf/open/floor/iron/freezer": "FloorFreezer",
    "/turf/open/floor/iron": "FloorSteel",
    "/turf/open/floor/iron/dark/diagonal": "FloorDarkDiagonal",
    "/turf/open/floor/iron/dark/herringbone": "FloorDarkHerringbone",
    "/turf/open/floor/iron/dark": "FloorDark",
    "/turf/open/floor/iron/smooth_large": "FloorSteel",
    "/turf/open/floor/iron/textured_large": "FloorSteel",
    "/turf/open/floor/iron/solarpanel/ocean": "FloorSteel",
    "/turf/open/floor/iron/terracotta/diagonal": "FloorDark",
    "/turf/open/floor/plating": "Plating",
    "/turf/open/floor/plating/ocean_plating": "Plating",
    "/turf/open/floor/engine": "FloorReinforced",
    "/turf/open/floor/mineral/titanium/tiled/white": "FloorWhite",
    "/turf/open/floor/mineral/titanium": "FloorHull",
    "/turf/open/floor/asphalt": "FloorAsphalt",
    "/turf/open/floor/fakeice": "FloorIce",
    "/turf/open/misc/asteroid/moon": "FloorAsteroidSand",
    "/turf/open/misc/asteroid": "FloorAsteroidSand",
    "/turf/open/misc/ironsand/ocean": "FloorAsteroidIronsand",
    "/turf/open/misc/beach/coast": "FloorDesert",
    "/turf/open/misc/beach/sand": "FloorDesert",
    "/turf/open/misc/dirt/jungle/dark": "FloorDirt",
    "/turf/open/misc/dirt/jungle": "FloorDirt",
    "/turf/open/misc/dirt/station": "FloorDirt",
    "/turf/open/misc/dirt": "FloorDirt",
    "/turf/open/misc/snow": "FloorSnow",
    "/turf/open/floor/wood/broken": "FloorBrokenWood",
}

# Turfs that become a floor tile plus an entity.
WALL_PROTOS = {
    "/turf/closed/wall/r_wall": "WallReinforced",
    "/turf/closed/wall/mineral/wood": "WallWood",
    "/turf/closed/wall/mineral/sandstone": "WallSandstone",
    "/turf/closed/wall/mineral/titanium": "WallShuttle",
    "/turf/closed/wall/mineral/stone": "WallSolid",
    "/turf/closed/wall/mineral": "WallSolid",
    "/turf/closed/wall": "WallSolid",
    "/turf/closed/indestructible/wood": "WallWood",
    "/turf/closed/indestructible/sandstone": "WallSandstone",
    "/turf/closed/indestructible/rock": "WallRock",
    "/turf/closed/indestructible/reinforced": "WallReinforced",
    "/turf/closed/indestructible/hotelwall": "WallWood",
    "/turf/closed/indestructible/iron": "WallSolid",
    "/turf/closed/indestructible": "WallSolid",
    "/turf/cordon": "WallSolid",
}

# Turfs that become a window entity.
WINDOW_TURFS = (
    "/turf/closed/indestructible/fakeglass",
    "/turf/closed/indestructible/window",
    "/turf/closed/wall/mineral/glass",
)

# Turfs that become the hotel exit door.
EXIT_TURFS = (
    "/turf/closed/indestructible/hoteldoor",
)

# Carpet turfs become a floor tile plus a carpet entity.
CARPET_PROTOS = {
    "/turf/open/floor/carpet/royalblue": "CarpetBlue",
    "/turf/open/floor/carpet/lone/star": "Carpet",
    "/turf/open/floor/carpet/lone": "Carpet",
    "/turf/open/floor/carpet/red": "CarpetPink",
    "/turf/open/floor/carpet/green": "CarpetGreen",
    "/turf/open/floor/carpet/blue": "CarpetBlue",
    "/turf/open/floor/carpet/cyan": "CarpetCyan",
    "/turf/open/floor/carpet/orange": "CarpetOrange",
    "/turf/open/floor/carpet/purple": "CarpetPurple",
    "/turf/open/floor/carpet/black": "CarpetBlack",
    "/turf/open/floor/carpet/white": "CarpetWhite",
    "/turf/open/floor/carpet": "Carpet",
}

# Water turfs become a floor tile plus a water entity.
WATER_TURFS = (
    "/turf/open/water",
    "/turf/open/floor/iron/pool",
    "/turf/open/floor/iron/pool/cobble",
)

# Objects (matched by longest prefix).
OBJECT_PROTOS = {
    "/obj/structure/bed/double": "Bed",
    "/obj/structure/bed": "Bed",
    "/obj/structure/table/wood/poker": "TableWood",
    "/obj/structure/table/wood/fancy": "TableFancyBlack",
    "/obj/structure/table/wood": "TableWood",
    "/obj/structure/table/reinforced/plastitaniumglass": "TableReinforcedGlass",
    "/obj/structure/table/reinforced": "TableReinforced",
    "/obj/structure/table/glass": "TableGlass",
    "/obj/structure/table": "Table",
    "/obj/structure/chair/wood": "ChairWood",
    "/obj/structure/chair/comfy": "ComfyChair",
    "/obj/structure/chair/sofa": "BenchComfy",
    "/obj/structure/chair/office/light": "ChairOfficeLight",
    "/obj/structure/chair/office/dark": "ChairOfficeDark",
    "/obj/structure/chair/stool/bar": "ChairWood",
    "/obj/structure/chair": "Chair",
    "/obj/structure/closet/secure_closet/personal/cabinet": "ClosetSteelBase",
    "/obj/structure/closet/cabinet": "ClosetSteelBase",
    "/obj/structure/closet/mini_fridge": "SmartFridge",
    "/obj/structure/closet": "ClosetSteelBase",
    "/obj/structure/dresser": "Dresser",
    "/obj/structure/bookcase": "Bookshelf",
    "/obj/structure/toilet": "ToiletEmpty",
    "/obj/structure/sink": "Sink",
    "/obj/structure/mirror": "Mirror",
    "/obj/structure/curtain": "CurtainsWhite",
    "/obj/structure/railing": "Railing",
    "/obj/structure/fireplace": "Fireplace",
    "/obj/structure/grille": "Grille",
    "/obj/structure/window": "Window",
    "/obj/structure/mineral_door/wood": "WoodDoor",
    "/obj/structure/mineral_door/paperframe": "PaperDoor",
    "/obj/structure/rack": "Rack",
    "/obj/structure/noticeboard": "NoticeBoard",
    "/obj/machinery/door/airlock": "WoodDoor",  # wood doors work without power
    "/obj/machinery/door/morgue": "WoodDoor",
    "/obj/machinery/door/window": "Window",
    "/obj/structure/extinguisher_cabinet": "ExtinguisherCabinet",
    "/obj/machinery/room_controller": "HilbertHotelRoomController",
    "/obj/machinery/smartfridge": "SmartFridge",
    "/obj/machinery/microwave": "KitchenMicrowave",
    "/obj/machinery/jukebox": "Jukebox",
    "/obj/machinery/vending/dorms": "VendingMachineBooze",
    "/obj/machinery/vending": "VendingMachineBooze",
    "/obj/machinery/light": "AlwaysPoweredWallLight",
    "/obj/machinery/shower": "Shower",
    "/obj/item/kirbyplants": "PottedPlant0",
}

# Object prefixes that must never match a mapping above (e.g. light switches
# start with "/obj/machinery/light").
SKIP_OBJECT_PREFIXES = ("/obj/machinery/light_switch",)

# BYOND directional suffix -> BYOND dir number, used for wall-mounted fixtures.
DIR_NAME_TO_BYOND = {"north": "1", "south": "2", "east": "4", "west": "8"}

# Prototypes that should receive a rotation from a BYOND /directional suffix.
# (Anything with Transform noRot must not be rotated, e.g. the room controller.)
DIRECTIONAL_PROTOS = {"AlwaysPoweredWallLight", "Shower"}

# Furniture props: only one of these may occupy a tile. Structural entities
# (walls, windows, doors, controllers, exits, decor) are never culled.
FURNITURE_PROTOS = {
    "Bed", "TableWood", "Table", "TableGlass", "TableReinforced",
    "TableReinforcedGlass", "TableFancyBlack", "Chair", "ChairWood",
    "ComfyChair", "ChairOfficeLight", "ChairOfficeDark", "BenchComfy",
    "ClosetSteelBase", "Dresser", "Bookshelf", "Rack", "Sink",
    "ToiletEmpty", "SmartFridge", "Jukebox", "VendingMachineBooze",
    "Fireplace", "PottedPlant0", "Railing",
}

# When two large props land on the same tile, the highest priority survives.
PROP_PRIORITY = {
    "Bed": 10,
    "ToiletEmpty": 9,
    "Sink": 9,
    "Window": 8,
    "WoodDoor": 8,
    "PaperDoor": 8,
    "Grille": 8,
    "TableWood": 6,
    "Table": 6,
    "TableGlass": 6,
    "TableReinforced": 6,
    "TableReinforcedGlass": 6,
    "TableFancyBlack": 6,
    "Bookshelf": 6,
    "Fireplace": 6,
    "SmartFridge": 6,
    "Jukebox": 6,
    "VendingMachineBooze": 6,
    "Chair": 5,
    "ChairWood": 5,
    "ComfyChair": 5,
    "ChairOfficeLight": 5,
    "ChairOfficeDark": 5,
    "BenchComfy": 5,
    "Dresser": 5,
    "Rack": 5,
    "PottedPlant0": 4,
    "Railing": 4,
    "ClosetSteelBase": 3,
}

# BYOND dir -> SS14 rotation (radians). Assumes SS14's default sprite faces south.
DIR_TO_ROT = {
    "1": 3.141592653589793,  # north
    "2": 0.0,  # south
    "4": 1.5707963267948966,  # east
    "8": -1.5707963267948966,  # west
    "5": 2.356194490192345,  # north-east
    "6": 0.7853981633974483,  # south-east
    "9": -2.356194490192345,  # north-west
    "10": -0.7853981633974483,  # south-west
}

FALLBACK_FLOOR = "Plating"
# A 21% O2 / 79% N2 mix at 20 C. The list predates the ten /tg/ gases (enum ids 11-20), which
# append after the vanilla ids and default to zero here; GasMixture.AfterDeserialization pads a
# shorter list to Atmospherics.AdjustedNumberOfGases (currently 24), so this list need not grow.
MOLES = [21.824879, 82.10312, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]


def concrete_prototype_ids():
    """Collects non-abstract entity/tile prototype ids for sanity checks."""
    ids = set()
    block_re = re.compile(r"^- type: (?:entity|tile)\s*\n(.*?)(?=^- type: |\Z)", re.M | re.S)
    id_re = re.compile(r"^  id: ([A-Za-z0-9_]+)\s*$", re.M)
    abstract_re = re.compile(r"^  abstract: true\s*$", re.M)
    for path in (REPO / "Resources/Prototypes").rglob("*.yml"):
        try:
            text = path.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        for match in block_re.finditer(text):
            body = match.group(1)
            id_match = id_re.search(body)
            if not id_match or abstract_re.search(body):
                continue
            ids.add(id_match.group(1))
    return ids


def powered_prototype_ids():
    """Entity prototype ids that inherit ApcPowerReceiver, resolving parents.

    Hotel rooms have no power grid, so any mapped machine that inherits an APC
    receiver is emitted with `needsPower: false`.
    """
    blocks = {}
    block_re = re.compile(r"^- type: entity\s*\n(.*?)(?=^- type: |\Z)", re.M | re.S)
    id_re = re.compile(r"^  id: ([A-Za-z0-9_]+)\s*$", re.M)
    parent_re = re.compile(r"^  parent:\s*(.+)$", re.M)
    comp_re = re.compile(r"^  - type: ([A-Za-z0-9_]+)\s*$", re.M)

    for path in (REPO / "Resources/Prototypes").rglob("*.yml"):
        try:
            text = path.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        for match in block_re.finditer(text):
            body = match.group(1)
            id_match = id_re.search(body)
            if not id_match:
                continue
            parents = []
            parent_match = parent_re.search(body)
            if parent_match:
                raw = parent_match.group(1).strip().strip("[]")
                parents = [p.strip() for p in raw.split(",") if p.strip()]
            blocks[id_match.group(1)] = (parents, set(comp_re.findall(body)))

    def has_component(proto_id, component, seen):
        if proto_id in seen or proto_id not in blocks:
            return False
        seen.add(proto_id)
        parents, components = blocks[proto_id]
        if component in components:
            return True
        return any(has_component(parent, component, seen) for parent in parents)

    return {proto_id for proto_id in blocks if has_component(proto_id, "ApcPowerReceiver", set())}


def longest_prefix(path: str, table) -> str | None:
    best = None
    for key in table:
        if path.startswith(key) and (best is None or len(key) > len(best)):
            best = key
    return best


class RoomCell:
    __slots__ = ("tile", "entities")

    def __init__(self):
        self.tile = "Space"
        self.entities = []  # (proto, rotation or None)


def convert(src: Path, dst: Path, name: str, concrete_ids: set, powered_ids: set):
    entries, cells = parse_dmm(src.read_text(encoding="utf-8", errors="replace"))
    if not cells:
        raise SystemExit(f"No grid cells found in {src}")

    xs = [c[0] for c in cells]
    ys = [c[1] for c in cells]
    min_x, min_y = min(xs), min(ys)

    room = {}
    unmapped_turfs, unmapped_objects = {}, {}

    for (x, y), key in cells.items():
        atoms = entries.get(key)
        if atoms is None:
            continue

        cell = RoomCell()
        turf = None
        for atom in atoms:
            path, variables = split_atom(atom)
            if path.startswith("/turf"):
                turf = path
                if path.startswith("/turf/open/space") or path.startswith("/turf/template_noop"):
                    cell.tile = "Space"
                elif any(path.startswith(t) for t in EXIT_TURFS):
                    cell.tile = FALLBACK_FLOOR
                    cell.entities.append(("HilbertHotelExit", None))
                elif any(path.startswith(t) for t in WINDOW_TURFS):
                    cell.tile = FALLBACK_FLOOR
                    cell.entities.append(("Window", variables.get("dir")))
                elif any(path.startswith(t) for t in WATER_TURFS):
                    cell.tile = "FloorAsteroidSand"
                    cell.entities.append(("FloorWaterEntity", None))
                elif longest_prefix(path, CARPET_PROTOS):
                    cell.tile = "FloorWood"
                    cell.entities.append((CARPET_PROTOS[longest_prefix(path, CARPET_PROTOS)], None))
                elif longest_prefix(path, WALL_PROTOS):
                    cell.tile = FALLBACK_FLOOR
                    cell.entities.append((WALL_PROTOS[longest_prefix(path, WALL_PROTOS)], None))
                elif longest_prefix(path, FLOOR_TILES):
                    cell.tile = FLOOR_TILES[longest_prefix(path, FLOOR_TILES)]
                elif path.startswith("/turf/closed"):
                    cell.tile = FALLBACK_FLOOR
                    cell.entities.append(("WallSolid", None))
                    unmapped_turfs[path] = unmapped_turfs.get(path, 0) + 1
                else:
                    cell.tile = FALLBACK_FLOOR
                    unmapped_turfs[path] = unmapped_turfs.get(path, 0) + 1
            elif path.startswith("/obj"):
                if any(path.startswith(prefix) for prefix in SKIP_OBJECT_PREFIXES):
                    continue

                proto_key = longest_prefix(path, OBJECT_PROTOS)
                if proto_key:
                    direction = variables.get("dir")
                    # BYOND directional atoms (lights, showers, ...) carry their
                    # facing in the path rather than a dir variable.
                    if OBJECT_PROTOS[proto_key] in DIRECTIONAL_PROTOS:
                        for name, byond_dir in DIR_NAME_TO_BYOND.items():
                            if f"/directional/{name}" in path:
                                direction = byond_dir
                                break
                    cell.entities.append((OBJECT_PROTOS[proto_key], direction))
                else:
                    unmapped_objects[path] = unmapped_objects.get(path, 0) + 1

        room[(x - min_x, y - min_y)] = cell

    # Drop prototypes that do not exist (or are abstract) in this repo.
    for cell in room.values():
        if cell.tile != "Space" and cell.tile not in concrete_ids:
            print(f"  warning: unknown tile '{cell.tile}', falling back to {FALLBACK_FLOOR}")
            cell.tile = FALLBACK_FLOOR
        kept = []
        for proto, direction in cell.entities:
            if proto in concrete_ids:
                kept.append((proto, direction))
            else:
                print(f"  warning: skipping unknown prototype '{proto}'")
        cell.entities = kept

    # Resolve furniture that landed on the same tile: only one large furniture
    # prop survives (highest priority wins). Structural entities are untouched,
    # but exact duplicates (e.g. two windows on one tile) are deduplicated.
    for cell in room.values():
        furniture = [entity for entity in cell.entities if entity[0] in FURNITURE_PROTOS]
        other = [entity for entity in cell.entities if entity[0] not in FURNITURE_PROTOS]

        if len(furniture) > 1:
            keep = max(furniture, key=lambda entity: PROP_PRIORITY.get(entity[0], 2))
            for entity in furniture:
                if entity is not keep:
                    print(f"  note: skipping overlapping '{entity[0]}' (kept '{keep[0]}')")
            furniture = [keep]

        seen = set()
        deduped = []
        for entity in other:
            if entity[0] in seen:
                continue
            seen.add(entity[0])
            deduped.append(entity)

        cell.entities = deduped + furniture

    width = max(x for x, _ in room) + 1
    height = max(y for _, y in room) + 1

    write_map(dst, name, room, width, height, src.name, unmapped_turfs, unmapped_objects, powered_ids)
    print(f"{src.name} -> {dst.name}: {width}x{height}, {sum(len(c.entities) for c in room.values())} entities")


def add_landing_marker(room, width, height):
    """Places the landing marker on the nearest entity-free floor tile reachable from an exit."""
    exits = [(x, y) for (x, y), cell in room.items() if any(e[0] == "HilbertHotelExit" for e in cell.entities)]

    def is_open(pos):
        cell = room.get(pos)
        return cell is not None and cell.tile not in ("Space", FALLBACK_FLOOR)

    def is_free(pos):
        cell = room.get(pos)
        return cell is not None and cell.tile not in ("Space", FALLBACK_FLOOR) and not cell.entities

    if exits:
        from collections import deque

        seen = {exits[0]}
        queue = deque([exits[0]])
        while queue:
            px, py = queue.popleft()
            for dx, dy in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                nxt = (px + dx, py + dy)
                if nxt in seen:
                    continue
                seen.add(nxt)
                if is_free(nxt):
                    room[nxt].entities.append(("HilbertHotelLandingMarker", None))
                    return
                if is_open(nxt):
                    queue.append(nxt)

    # Fall back to the floor tile closest to the map center.
    open_tiles = [(x, y) for (x, y), cell in room.items() if cell.tile not in ("Space", FALLBACK_FLOOR)]
    if not open_tiles:
        return
    cx, cy = width / 2, height / 2
    best = min(open_tiles, key=lambda p: (p[0] - cx) ** 2 + (p[1] - cy) ** 2)
    room[best].entities.append(("HilbertHotelLandingMarker", None))


def relocate_controller(room):
    """Moves a mapped room controller off any exit-adjacent tile.

    The original SPLURT controllers are wall-mounted and face the room, so they
    sit next to the door without blocking it. SS14's controller is a floor
    console; move it a few tiles away when it would crowd the exit or the
    landing spot.
    """
    controllers = [(x, y) for (x, y), cell in room.items()
                   if any(e[0] == "HilbertHotelRoomController" for e in cell.entities)]
    if not controllers:
        return

    exits = {(x, y) for (x, y), cell in room.items()
             if any(e[0] == "HilbertHotelExit" for e in cell.entities)}
    landings = {(x, y) for (x, y), cell in room.items()
                if any(e[0] == "HilbertHotelLandingMarker" for e in cell.entities)}

    def touches_exit(pos):
        return any((pos[0] + dx, pos[1] + dy) in exits for dx, dy in ((0, 1), (0, -1), (1, 0), (-1, 0)))

    from collections import deque

    for pos in controllers:
        if pos not in landings and not touches_exit(pos):
            continue

        target = None
        seen = {pos}
        queue = deque([pos])
        while queue and target is None:
            px, py = queue.popleft()
            for dx, dy in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                nxt = (px + dx, py + dy)
                if nxt in seen:
                    continue
                seen.add(nxt)
                cell = room.get(nxt)
                if cell is None or cell.tile in ("Space", FALLBACK_FLOOR):
                    continue
                if not cell.entities and nxt not in landings and not touches_exit(nxt):
                    target = nxt
                    break
                queue.append(nxt)

        if target is None:
            continue

        entity = next(e for e in room[pos].entities if e[0] == "HilbertHotelRoomController")
        room[pos].entities.remove(entity)
        room[target].entities.append(entity)


def chunk_tiles(room, chunk_x, chunk_y, tile_ids):
    data = bytearray(16 * 16 * 7)
    for y in range(16):
        for x in range(16):
            cell = room.get((chunk_x * 16 + x, chunk_y * 16 + y))
            if cell is None or cell.tile == "Space":
                continue
            offset = (y * 16 + x) * 7
            struct.pack_into("<iBBB", data, offset, tile_ids[cell.tile], 0, 0, 0)
    return base64.b64encode(bytes(data)).decode()


def atmos_chunks(room):
    chunks = {}
    for (x, y), cell in room.items():
        if cell.tile == "Space":
            continue
        origin = (x // 4, y // 4)
        rel = (x % 4, y % 4)
        chunks[origin] = chunks.get(origin, 0) | (1 << (rel[0] + rel[1] * 4))
    return chunks


def write_map(dst, name, room, width, height, source_name, unmapped_turfs, unmapped_objects, powered_ids):
    add_landing_marker(room, width, height)
    relocate_controller(room)

    # Assign YAML tile ids.
    tile_names = sorted({cell.tile for cell in room.values() if cell.tile != "Space"})
    tile_ids = {name_: i + 1 for i, name_ in enumerate(tile_names)}

    # Entity list (uid assignment happens below).
    entities = []
    for (x, y), cell in sorted(room.items()):
        for proto, direction in cell.entities:
            rot = DIR_TO_ROT.get(direction) if direction else None
            entities.append((proto, x + 0.5, y + 0.5, rot))

    lines = []
    lines.append("meta:")
    lines.append("  format: 7")
    lines.append("  category: Map")
    lines.append("  engineVersion: 266.0.0")
    lines.append('  forkId: ""')
    lines.append('  forkVersion: ""')
    lines.append(f"  time: 10/04/2026 00:00:00")
    lines.append(f"  entityCount: {2 + len(entities)}")
    lines.append("maps:")
    lines.append("- 1")
    lines.append("grids:")
    lines.append("- 2")
    lines.append("orphans: []")
    lines.append("nullspace: []")
    lines.append("tilemap:")
    lines.append("  0: Space")
    for tile, tid in tile_ids.items():
        lines.append(f"  {tid}: {tile}")
    lines.append("entities:")
    lines.append('- proto: ""')
    lines.append("  entities:")

    # Grid entity.
    lines.append("  - uid: 2")
    lines.append("    components:")
    lines.append("    - type: MetaData")
    lines.append(f"      name: {source_name}")
    lines.append("    - type: Transform")
    lines.append("      parent: 1")
    lines.append("    - type: MapGrid")
    lines.append("      chunks:")
    for cy in range((height + 15) // 16):
        for cx in range((width + 15) // 16):
            tiles = chunk_tiles(room, cx, cy, tile_ids)
            if tiles == base64.b64encode(bytes(16 * 16 * 7)).decode():
                continue
            lines.append(f"        {cx},{cy}:")
            lines.append(f"          ind: {cx},{cy}")
            lines.append(f"          tiles: {tiles}")
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
    for (cx, cy), mask in sorted(atmos_chunks(room).items()):
        lines.append(f"          {cx},{cy}:")
        lines.append(f"            0: {mask}")
    lines.append("        uniqueMixes:")
    lines.append("        - volume: 2500")
    lines.append("          temperature: 293.15")
    lines.append("          moles:")
    for mole in MOLES:
        lines.append(f"          - {mole}")
    lines.append("        chunkSize: 4")

    # Map entity.
    lines.append("  - uid: 1")
    lines.append("    components:")
    lines.append("    - type: MetaData")
    lines.append(f"      name: {name}")
    lines.append("    - type: Transform")
    lines.append("    - type: Map")
    lines.append("      mapPaused: True")
    lines.append("    - type: GridTree")
    lines.append("    - type: Broadphase")
    lines.append("    - type: OccluderTree")
    lines.append("    - type: MapLight")
    lines.append("      ambientLightColor: '#D8B059FF'")

    # Entity prototypes grouped for readability.
    groups = {}
    uid = 3
    for proto, x, y, rot in entities:
        groups.setdefault(proto, []).append((uid, x, y, rot))
        uid += 1
    for proto, group in groups.items():
        lines.append(f"- proto: {proto}")
        lines.append("  entities:")
        for ent_uid, x, y, rot in group:
            lines.append(f"  - uid: {ent_uid}")
            lines.append("    components:")
            lines.append("    - type: Transform")
            if rot is not None:
                lines.append(f"      rot: {rot}")
            lines.append(f"      pos: {x},{y}")
            lines.append("      parent: 2")
            lines.append("      anchored: True")
            if proto in powered_ids:
                lines.append("    - type: ApcPowerReceiver")
                lines.append("      needsPower: false")

    lines.append("...")
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text("\n".join(lines) + "\n")

    if unmapped_turfs:
        print("  unmapped turfs:")
        for t, c in sorted(unmapped_turfs.items(), key=lambda kv: -kv[1]):
            print(f"    {c:4d} {t}")
    if unmapped_objects:
        print("  unmapped objects:")
        for o, c in sorted(unmapped_objects.items(), key=lambda kv: -kv[1])[:20]:
            print(f"    {c:4d} {o}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--name", default=None)
    args = parser.parse_args()

    name = args.name or args.input.stem
    convert(args.input, args.output, name, concrete_prototype_ids(), powered_prototype_ids())


if __name__ == "__main__":
    main()
