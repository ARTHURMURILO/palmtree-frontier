#!/usr/bin/env python3
"""Build the RSI sprites for the Palmtree temperature atmos devices out of the /tg/station art.

The devices do not get new art: their sprites are lifted straight out of the tgstation DMI files
that the vanilla SS14 atmos devices already come from (see the copyright field of
Structures/Piping/Atmospherics/pump.rsi/meta.json), so the set stays visually consistent.

Facing directions
    tgstation draws these devices for one facing only. South is used as-is, north is the same
    sprite flipped vertically, and east/west are 90 degree rotations - the same trick the vanilla
    SS14 pump sprites use to move their body between facings.
    The atmos meter is a box that is read from above, so all four facings share the same art.

Pipe layers
    Layer offsets are not invented; they were measured from pump.rsi vs pump_alt1/pump_alt2:
        vertical pipes   (south/north): layer 2 is +7 px in x, layer 3 is -7 px in x
        horizontal pipes (east/west):   layer 2 is -7 px in y, layer 3 is +7 px in y

Usage:
    Tools/gen_atmos_tg_sprites.py --tg ../tgstation-master

The tgstation checkout is not vendored: unpack an archive of the commit below next to the
repository and point --tg at it. The output folder defaults to the repository's Resources tree.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import struct
import zlib

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parent.parent
TG = REPO.parent / "tgstation-master"
OUT = REPO / "Resources/Textures/_PS/Structures/Piping/Atmospherics"

SIZE = 32

BINARY_DMI = "icons/obj/machines/atmospherics/binary_devices.dmi"
UNARY_DMI = "icons/obj/machines/atmospherics/unary_devices.dmi"
METER_DMI = "icons/obj/pipes_n_cables/meter.dmi"

TG_COMMIT = "50b07c1ae8f3a978e11a062509b06babc5669ce5"

COPYRIGHT = ("Taken from https://github.com/tgstation/tgstation at commit "
             f"{TG_COMMIT} (CC-BY-SA-3.0), repacked for Space Station 14 by Palmtree.")


# ---------------------------------------------------------------- dmi reading

def read_dmi(path: pathlib.Path) -> dict[str, Image.Image]:
    """Return every state of a .dmi as a 32x32 image (frame 0, direction 0)."""
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path} is not a PNG")

    offset = 8
    description = None
    while offset < len(data):
        (length,) = struct.unpack(">I", data[offset:offset + 4])
        chunk_type = data[offset + 4:offset + 8]
        chunk_data = data[offset + 8:offset + 8 + length]
        offset += 12 + length
        if chunk_type != b"zTXt":
            continue
        keyword, _, rest = chunk_data.partition(b"\x00")
        if keyword == b"Description":
            description = zlib.decompress(rest[1:]).decode("utf-8", errors="replace")
            break

    if description is None:
        raise ValueError(f"{path} has no DMI description chunk")

    width = height = 32
    states: list[dict] = []
    current = None
    for line in description.splitlines():
        line = line.strip()
        if line.startswith("width ="):
            width = int(line.split("=")[1])
        elif line.startswith("height ="):
            height = int(line.split("=")[1])
        elif line.startswith('state = "'):
            current = {"name": line[len('state = "'):-1].strip(), "dirs": 1, "frames": 1}
            states.append(current)
        elif current is not None and line.startswith("dirs ="):
            current["dirs"] = int(line.split("=")[1])
        elif current is not None and line.startswith("frames ="):
            current["frames"] = int(line.split("=")[1])

    sheet = Image.open(path).convert("RGBA")
    dim_x = sheet.width // width

    out: dict[str, Image.Image] = {}
    index = 0
    for state in states:
        cells = state["dirs"] * state["frames"]
        cx = index % dim_x
        cy = index // dim_x
        if state["name"]:
            out[state["name"]] = sheet.crop((cx * width, cy * height,
                                             cx * width + width, cy * height + height))
        index += cells
    return out


# ---------------------------------------------------------------- geometry

def facing_frames(master: Image.Image) -> list[Image.Image]:
    """South, north, east, west frames, in RSI direction order.

    The master is drawn facing south, so a mark at the bottom of the sprite has to end up on the
    right for east and on the left for west. PIL's rotate(90) turns the bottom edge into the right
    edge, rotate(-90) turns it into the left edge - getting these the wrong way round makes devices
    point their flow direction backwards whenever the east/west frames are used.
    North is a vertical flip, which is what the vanilla pump sprites do.
    """
    return [
        master,
        master.transpose(Image.FLIP_TOP_BOTTOM),
        master.rotate(90),
        master.rotate(-90),
    ]


def centre_body(master: Image.Image, stub_row: int, drop_stub: bool = False) -> Image.Image:
    """Centre the device body in the tile, keeping any connector it is drawn with.

    tgstation draws these devices hanging off the south edge of the tile: the body sits low and a
    separate 3x2 connector is drawn a couple of pixels below it. Rotating that art for east/west
    leaves the body jammed against one edge, which reads as a clipped sprite, so the body is
    centred and the connector moves with it - exactly how the vanilla SS14 pump sprites behave.

    :param stub_row: first row of the connector, excluded from the centring bounds.
    :param drop_stub: also delete the connector instead of keeping it (the heat exchanger's long
        pipe stub, which duplicates the pipe underlay).
    """
    body = master.copy()
    for y in range(stub_row, SIZE):
        for x in range(SIZE):
            body.putpixel((x, y), (0, 0, 0, 0))

    bounds = body.getchannel("A").getbbox()
    if bounds is None:
        return master

    cx = (bounds[0] + bounds[2] - 1) / 2
    cy = (bounds[1] + bounds[3] - 1) / 2
    dx = round((SIZE - 1) / 2 - cx)
    dy = round((SIZE - 1) / 2 - cy)

    source = body if drop_stub else master
    shifted = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shifted.paste(source, (dx, dy))
    return shifted


def static_frames(master: Image.Image) -> list[Image.Image]:
    """Four identical facings, for devices whose art does not turn with the pipe."""
    return [master, master, master, master]


def layer_shift(frame: Image.Image, layer: int, direction: int) -> Image.Image:
    if layer == 0:
        return frame

    vertical = direction in (0, 1)
    offset = 7 if layer == 1 else -7

    dx = offset if vertical else 0
    dy = 0 if vertical else -offset

    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(frame, (dx, dy))
    return out


# ---------------------------------------------------------------- rsi writing

def write_rsi(folder: pathlib.Path, states: dict[str, list[Image.Image]]):
    folder.mkdir(parents=True, exist_ok=True)
    # Drop states from an older run so the RSI never keeps orphans around.
    for old in folder.glob("*.png"):
        old.unlink()
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": SIZE, "y": SIZE},
        "states": [],
    }

    for state_name, frames in states.items():
        sheet = Image.new("RGBA", (SIZE * 2, SIZE * 2), (0, 0, 0, 0))
        for i, frame in enumerate(frames):
            sheet.paste(frame, ((i % 2) * SIZE, (i // 2) * SIZE))
        sheet.save(folder / f"{state_name}.png")
        meta["states"].append({"name": state_name, "directions": 4})

    (folder / "meta.json").write_text(json.dumps(meta, indent=4) + "\n")


def write_layered(name: str, masters: dict[str, Image.Image], turn_with_pipe: bool = True):
    """Write the primary RSI and the two pipe layer variants."""
    for layer, suffix in enumerate(("", "_alt1", "_alt2")):
        states: dict[str, list[Image.Image]] = {}

        for state_name, master in masters.items():
            frames = facing_frames(master) if turn_with_pipe else static_frames(master)
            states[state_name] = [layer_shift(frame, layer, i) for i, frame in enumerate(frames)]

        write_rsi(OUT / f"{name}{suffix}.rsi", states)


def main():
    global TG, OUT

    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--tg", type=pathlib.Path, default=TG,
                        help="tgstation checkout to read the .dmi files from (default: %(default)s)")
    parser.add_argument("--out", type=pathlib.Path, default=OUT,
                        help="folder to write the .rsi files into (default: %(default)s)")
    args = parser.parse_args()
    TG, OUT = args.tg, args.out

    binary = read_dmi(TG / BINARY_DMI)
    unary = read_dmi(TG / UNARY_DMI)
    meter = read_dmi(TG / METER_DMI)

    # The tgate art ends in a detached red coupling at rows 30-31; the pump at rows 28-29. That
    # coupling is what every vanilla pump sprite has, so it stays.
    write_layered("temperature_gate", {
        "tgate": centre_body(binary["tgate_off-1"], 28),
        "tgateOn": centre_body(binary["tgate_on-1"], 28),
        "tgateFlow": centre_body(binary["tgate_flow-1"], 28),
    })

    write_layered("temperature_pump", {
        "tpump": centre_body(binary["tpump_off-1"], 26),
        "tpumpOn": centre_body(binary["tpump_on-1"], 26),
    })

    # The tgstation heat exchanger sprite ends in a connector stub that pokes out past the tile.
    # The SS14 pipe underlay already draws that pipe, so the stub is dropped and the body re-centred.
    write_layered("heat_exchanger", {
        "he": centre_body(unary["he1"], 27, drop_stub=True),
    })

    # The meter is read from above, so its art must not turn with the pipe. The gauge bar is a
    # separate layer so that only the bar gets tinted by temperature; both layers are listed in
    # SubFloorHide.VisibleLayers so the meter stays readable above a floor tile.
    write_layered("atmos_meter", {
        "meter": meter["meter"],
        "pressure0": meter["pressure0"],
        "pressure1": meter["pressure1_3"],
        "pressure2": meter["pressure2_3"],
        "pressure3": meter["pressure3_4"],
        "pressure4": meter["pressure4"],
    }, turn_with_pipe=False)

    print("wrote", OUT)


if __name__ == "__main__":
    main()
