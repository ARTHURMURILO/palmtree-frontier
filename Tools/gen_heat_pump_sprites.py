#!/usr/bin/env python3
"""Build the RSI sprites for the Palmtree heat pump out of the vanilla thermomachine art.

The heat pump is the two-port thermomachine /tg/station had between 2021 and 2022 (PR #57831).
It does not get new art: the machine body is the freezer/heater body Space Station 14 already
ships, and the two port markings are the vanilla pipe sprite tinted the way /tg/station tinted
them - green for the gas port (`COLOR_LIME`), red for the thermal/waste port
(`COLOR_MOSTLY_PURE_RED`).

Layers, bottom to top:
    body        freezer/heater off/on, plus the open-panel state
    portWaste   red cuff of pipe on the waste end (the opposite end to the gas port)
    portGas     green cuff of pipe on the gas port
The cuffs are drawn over the body, the same way the /tg/station overlay was drawn on top of the
machine, so both ports stay readable in all four facings.

Facing
    Body states are one-direction (the machine is a box), the port states are four-direction, the
    same split the vanilla thermomachine uses for its own `pipe` state. South is the gas port, so
    a heat pump replaces a freezer in a wall without moving the pipe.

Pipe layers
    There is only one RSI. The machine body fills its tile, so the 7 px shift the pumps use for
    pump_alt1/pump_alt2 would push it out of its cell; the three pipe layer prototypes share this
    sprite and differ only in AtmosPipeLayers.pipeLayer.

Usage:
    Tools/gen_heat_pump_sprites.py
"""

from __future__ import annotations

import argparse
import json
import pathlib

from PIL import Image

REPO = pathlib.Path(__file__).resolve().parent.parent
SOURCE = REPO / "Resources/Textures/Structures/Piping/Atmospherics"
OUT = REPO / "Resources/Textures/_PS/Structures/Piping/Atmospherics"

SIZE = 32
DIRS = ["South", "North", "East", "West"]  # RSI 2x2 order

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


def cuff(image: Image.Image, edge: str, depth: int = 6) -> Image.Image:
    """Keep only the band of pipe nearest `edge`, so the marking reads as a coloured cuff at the
    port instead of a pipe hidden behind the machine body."""
    out = image.copy()
    px = out.load()
    for y in range(SIZE):
        for x in range(SIZE):
            if px[x, y][3] == 0:
                continue
            keep = {
                "top": y < depth,
                "bottom": y >= SIZE - depth,
                "left": x < depth,
                "right": x >= SIZE - depth,
            }[edge]
            if not keep:
                px[x, y] = (0, 0, 0, 0)
    return out


def build_states() -> dict[str, tuple[int, list[Image.Image]]]:
    """state name -> (directions, frames), with the layers already composited."""
    pipe = load_frames(SOURCE / "pipe.rsi/pipeStraight.png", 4)

    states: dict[str, tuple[int, list[Image.Image]]] = {}

    for name in BODY_STATES:
        states[name] = (1, load_frames(SOURCE / "thermomachine.rsi" / f"{name}.png", 1))

    gas: list[Image.Image] = []
    waste: list[Image.Image] = []
    for i, facing in enumerate(DIRS):
        gas.append(cuff(tint(pipe[i], GAS_COLOR), EDGE[facing]))
        waste.append(cuff(tint(pipe[i], WASTE_COLOR), EDGE[OPPOSITE[facing]]))

    states["portGas"] = (4, gas)
    states["portWaste"] = (4, waste)
    return states


def write_rsi(folder: pathlib.Path, states: dict[str, tuple[int, list[Image.Image]]]):
    folder.mkdir(parents=True, exist_ok=True)
    for old in folder.glob("*.png"):
        old.unlink()

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": (
            "Body taken from Structures/Piping/Atmospherics/thermomachine.rsi (CC-BY-SA-3.0, "
            "from /tg/station); the port markings are pipe.rsi recoloured after the two-port "
            "thermomachine /tg/station had in 2021-2022 (PR #57831). Repacked for Palmtree."
        ),
        "size": {"x": SIZE, "y": SIZE},
        "states": [],
    }

    for name, (directions, frames) in states.items():
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

    write_rsi(args.out / "heat_pump.rsi", build_states())
    print("wrote", args.out / "heat_pump.rsi")


if __name__ == "__main__":
    main()
