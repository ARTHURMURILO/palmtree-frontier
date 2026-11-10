#!/usr/bin/env python3
"""Build the RSI sprites for the Palmtree heat pump out of the vanilla thermomachine art.

The heat pump is the two-port thermomachine /tg/station had between 2021 and 2022 (PR #57831).
It does not get new art: the machine body is the freezer/heater body Space Station 14 already
ships, and the two port markings are the vanilla pipe sprite tinted the way /tg/station tinted
them - green for the gas port (`COLOR_LIME`), red for the thermal/waste port
(`COLOR_MOSTLY_PURE_RED`).

Layers, bottom to top:
    body        freezer/heater off/on, plus the open-panel state
    portWaste   red port nub on the waste end (the opposite end to the gas port)
    portGas     green port nub on the gas port
Both nubs are the vanilla thermomachine's own `pipe` state tinted the way /tg/station tinted them, and
are drawn over the body, the same way /tg/station drew its overlay. A nub rather than a length of pipe:
the machine body fills its tile, so a pipe would look like it was cutting through the machine.

Facing
    Body states are one-direction (the machine is a box), the port states are four-direction, the
    same split the vanilla thermomachine uses for its own `pipe` state. South is the gas port, so
    a heat pump replaces a freezer in a wall without moving the pipe.

Pipe layers
    The body is one file, but the two port-marking layers get their own heat_pump_alt1.rsi /
    heat_pump_alt2.rsi, cut to the same 7 px offsets the vanilla pipe_alt1/pipe_alt2 sprites use, so
    the markings follow the pipes on each layer and the placement ghost shows the layer the cursor
    picked. The body still fills its tile, so it is never shifted.

Usage:
    Tools/gen_heat_pump_sprites.py
"""

from __future__ import annotations

import argparse
import json
import pathlib
import shutil

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parent.parent
SOURCE = REPO / "Resources/Textures/Structures/Piping/Atmospherics"
OUT = REPO / "Resources/Textures/_PS/Structures/Piping/Atmospherics"

SIZE = 32
DIRS = ["South", "North", "East", "West"]  # RSI 2x2 order

# Copied verbatim, frames and animation delays included: the running states are animated.
BODY_STATES = [
    "freezerOff",
    "freezerOn",
    "freezerPanelOpen",
    "heaterOff",
    "heaterOn",
    "heaterPanelOpen",
]

GAS_COLOR = (0, 255, 0)  # /tg/ COLOR_LIME
WASTE_COLOR = (200, 30, 30)  # /tg/ COLOR_MOSTLY_PURE_RED

# Which tile edge each facing puts the port on.
EDGE = {
    "South": "bottom",
    "North": "top",
    "East": "right",
    "West": "left",
}

OPPOSITE = {"South": "North", "North": "South", "East": "West", "West": "East"}


def load_frames(path: pathlib.Path, directions: int) -> list[Image.Image]:
    """Cut a state PNG into its directional frames (RSI packs 4 directions in a 2x2 grid)."""
    img = Image.open(path).convert("RGBA")
    if directions == 1:
        return [img.crop((0, 0, SIZE, SIZE))]

    frames = []
    for i in range(directions):
        x, y = (i % 2) * SIZE, (i // 2) * SIZE
        frames.append(img.crop((x, y, x + SIZE, y + SIZE)))
    return frames


def tint(image: Image.Image, color: tuple[int, int, int]) -> Image.Image:
    """Multiply the sprite by a colour, keeping the shading of the original pipe."""
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    src = image.load()
    dst = out.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            # The pipe art is greyscale; its own value decides how bright the tint is.
            value = max(r, g, b) / 255.0
            dst[x, y] = (
                min(255, int(color[0] * value)),
                min(255, int(color[1] * value)),
                min(255, int(color[2] * value)),
                a,
            )
    return out


def opposite_index(index: int) -> int:
    """Atlas order is South, North, East, West, so opposites are pairs 0/1 and 2/3."""
    return (index + 1) if index % 2 == 0 else (index - 1)


# The vanilla connector, cut out of the thermomachine's own `pipe` state. South and north are the
# original pieces (they are different heights, which is why one cannot just be the other flipped);
# east and west are the south piece turned a quarter turn, because the vanilla ones are clipped down
# to two or three pixels and look nothing like the other two.
SOUTH_NUB = (13, 28, 18, 32)
NORTH_NUB = (13, 0, 18, 5)


def port_nub(frames: list[Image.Image], facing: str) -> Image.Image:
    """The connector for one facing, on a 32x32 tile, flush against the machine body."""
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    index = DIRS.index(facing)

    if facing == "South":
        # Sits on the body's bottom edge.
        out.alpha_composite(frames[index].crop(SOUTH_NUB), (SOUTH_NUB[0], SOUTH_NUB[1]))
    elif facing == "North":
        # One pixel taller than the south one, so it reaches the body.
        out.alpha_composite(frames[index].crop(NORTH_NUB), (NORTH_NUB[0], NORTH_NUB[1]))
    else:
        # The vanilla pipe's sideways frames carry their barrel at rows 13-17, so the nub sits there
        # and not a pixel lower, or the two look misaligned where they meet.
        nub = frames[DIRS.index("South")].crop(SOUTH_NUB).transpose(
            Image.ROTATE_90 if facing == "East" else Image.ROTATE_270)
        out.alpha_composite(nub, (SIZE - nub.width, 13) if facing == "East" else (0, 13))

    return out


# The vanilla pipe sprite is drawn a few pixels off the tile centre for the two alt layers: a
# vertical run shifts sideways, a horizontal run shifts up or down, and the two layers mirror each
# other. These are the numbers measured off pipe.rsi, pipe_alt1.rsi and pipe_alt2.rsi.
LAYER_SHIFT = {1: 7, 2: -7}


def layer_shift(frame_index: int, layer: int) -> tuple[int, int]:
    """How far a port nub moves on an alt pipe layer, so it stays on the pipe it marks."""
    shift = LAYER_SHIFT.get(layer, 0)
    if shift == 0:
        return (0, 0)

    # A port facing north or south belongs to a vertical run, which the layer shifts sideways; one
    # facing east or west belongs to a horizontal run, which shifts up or down instead.
    if DIRS[frame_index] in ("South", "North"):
        return (shift, 0)
    return (0, -shift)


def offset_frame(frame: Image.Image, dx: int, dy: int) -> Image.Image:
    """Move a frame, clipping whatever leaves the tile."""
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(frame, (dx, dy), frame)
    return out


def build_states() -> dict[str, tuple[int, list[Image.Image]]]:
    """The two port states, four directions each, tinted the way /tg/station tinted its ports."""
    pipe = load_frames(SOURCE / "thermomachine.rsi/pipe.png", 4)

    gas = [tint(port_nub(pipe, DIRS[i]), GAS_COLOR) for i in range(4)]
    waste = [tint(port_nub(pipe, DIRS[opposite_index(i)]), WASTE_COLOR) for i in range(4)]

    return {
        "portGas": (4, gas),
        "portWaste": (4, waste),
    }


def write_rsi(folder: pathlib.Path, generated: dict[str, tuple[int, list[Image.Image]]], with_body: bool = True):
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob("*.png"):
        old.unlink()

    source_meta = json.loads((SOURCE / "thermomachine.rsi/meta.json").read_text())
    source_states = {state["name"]: state for state in source_meta["states"]}

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": (
            "Body: base sprites taken from tgstation, split to display on two layers "
            "(machinebody/panel) by Menshin for SS14, edited by chromiumboy. Port markings: "
            "pipe.rsi recoloured after the two-port thermomachine /tg/station had in 2021-2022 "
            "(PR #57831). Repacked for Palmtree."
        ),
        "size": {"x": SIZE, "y": SIZE},
        "states": [],
    }

    # The body states are the vanilla files, untouched, so the running animation survives. The alt
    # layer files only carry the port markings, so they skip this.
    if with_body:
        for name in BODY_STATES:
            shutil.copyfile(SOURCE / "thermomachine.rsi" / f"{name}.png", folder / f"{name}.png")
            meta["states"].append(source_states[name])

    for name, (directions, frames) in generated.items():
        sheet = Image.new("RGBA", (SIZE * 2, SIZE * 2), (0, 0, 0, 0))
        for i, frame in enumerate(frames):
            sheet.paste(frame, ((i % 2) * SIZE, (i // 2) * SIZE))
        sheet.save(folder / f"{name}.png")
        meta["states"].append({"name": name, "directions": directions})

    (folder / "meta.json").write_text(json.dumps(meta, indent=4) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=pathlib.Path, default=OUT,
                        help="folder to write the .rsi files into (default: %(default)s)")
    args = parser.parse_args()

    states = build_states()

    write_rsi(args.out / "heat_pump.rsi", states)
    print("wrote", args.out / "heat_pump.rsi")

    # One file per pipe layer, holding the port markings where that layer draws its pipes.
    for layer, suffix in ((1, "alt1"), (2, "alt2")):
        shifted = {
            name: (directions, [offset_frame(frame, *layer_shift(i, layer)) for i, frame in enumerate(frames)])
            for name, (directions, frames) in states.items()
        }
        write_rsi(args.out / f"heat_pump_{suffix}.rsi", shifted, with_body=False)
        print("wrote", args.out / f"heat_pump_{suffix}.rsi")


if __name__ == "__main__":
    main()
