# System: Temperature Atmos Devices (2026-11)

> Four atmos devices from /tg/station that have no Space Station 14 equivalent, plus the passive
> gate that already existed in the engine but was never added to the RPD.
> Source: `tgstation-master/code/modules/atmospherics/machinery/components/{binary,unary}_devices/`
> and `.../atmospherics/machinery/other/meter.dm` (tgstation commit
> `50b07c1ae8f3a978e11a062509b06babc5669ce5`).

## Locations

| What | Where |
|---|---|
| Shared components / UI messages | `Content.Shared/_PS/Atmos/Components/` |
| Shared systems (examine, UI messages, appearance) | `Content.Shared/_PS/Atmos/EntitySystems/` |
| Server systems (the gas/heat maths) | `Content.Server/_PS/Atmos/EntitySystems/` |
| Client systems + XAML windows + BUIs | `Content.Client/_PS/Atmos/` |
| Prototypes (3 pipe layers each) | `Resources/Prototypes/_PS/Entities/Structures/Piping/Atmospherics/temperature_devices.yml` |
| Construction graph + recipes | `Resources/Prototypes/_PS/Recipes/Construction/` |
| Sprites | `Resources/Textures/_PS/Structures/Piping/Atmospherics/` |
| Sprite generator | `Tools/gen_atmos_tg_sprites.py` |
| Locale | `Resources/Locale/en-US/_PS/atmos-devices.ftl` |
| Guidebook entry + page | `Resources/Prototypes/_PS/Guidebook/engineering.yml`, `Resources/ServerInfo/_PS/Guidebook/Engineering/TemperatureDevices.xml` |
| RPD modes | `Resources/Prototypes/_PS/RCD/rpd.yml` |
| Tests | `Content.IntegrationTests/Tests/_PS/Atmos/TemperatureDevicesTest.cs` |

Everything lives under the `_PS` prefix so upstream merges stay clean. Only two upstream files are
touched, each with a single line or block:

- `Resources/Prototypes/Guidebook/engineering.yml` - one child entry for the guidebook page.
- `Resources/Prototypes/Entities/Objects/Tools/tools.yml` - five ids added to the RPD's
  `availablePrototypes` (the four new devices plus `PassiveGate`).

## Devices

All four are 1x1, anchored, rotatable `SubFloorHide` entities placed with `AlignAtmosPipeLayers`, the
same footprint and placement rules as the vanilla pumps and valves. The two binary devices (gate and
pump) are directional - `GasBinaryBase` puts the inlet on the north side and the outlet on the south -
so rotate them so the inlet faces the pipeline the gas should come from. The heat exchanger and the
meter are unary and read the pipe they sit on.

### `GasTemperatureGate` (temperature gate)

`/tg/station` `temperature_gate.dm`. A binary device that releases gas from its inlet to its outlet
while the inlet gas is colder than `TargetTemperature` (or hotter when `Inverted`).

- Powered (200 W) like the vanilla pumps; `AtmosDeviceDisabledEvent` closes the UI and switches it off.
- Flow uses the same maths as `GasPressureRegulatorSystem`: transfer at most
  `MaxTransferRate * PumpSpeedup() * dt`, and never more than it takes to equalise the two sides.
  That keeps it from pushing gas into a fuller pipeline, and it means the gate is tick-rate invariant.
- UI: on/off, target temperature with `±10/100/1000`, "max" and "use current" buttons, invert checkbox.
  The target field is filled in when the window opens, never from `Update()` - the server dirties the
  sensor readouts once a second and rewriting the spin box would eat what the player is typing.
- Sprites: `tgate` / `tgateOn` / `tgateFlow`.
- Nothing happens while both ports sit on the same pipeline, or while either side resolves to the
  immutable space mixture: `AtmosphereSystem.Merge` silently drops anything fed into an immutable
  mixture, so a transfer into one would lose the gas. Anchored devices always end up in a node group
  with a real mixture, so the second guard is defensive - the pump, heat exchanger and meter carry
  the same one.

### `GasTemperaturePump` (temperature pump)

`/tg/station` `temperature_pump.dm`. Binary, powered (200 W), moves heat only and only from hot to cold.

The TG implementation removes 90% of each mixture, conducts between them and merges them back - an
artifact of BYOND's per-device gas volumes. Here both mixtures are walked towards their
heat-capacity weighted equilibrium by the same fraction per tick:

```
fraction  = clamp(HeatTransferRate / 100 * dt, 0, 1)
equilibrium = (T_in*C_in + T_out*C_out) / (C_in + C_out)
T_x += (equilibrium - T_x) * fraction
```

Moving both sides by the same fraction of their own distance to equilibrium conserves energy
exactly. The trajectory is a first-order Euler step, so it is only approximately tick-rate
independent: a long tick transfers slightly less than several short ticks would. The fraction is
clamped to 1, so the pump converges instead of overshooting.

### `GasHeatExchanger` (heat exchanger)

`/tg/station` `heat_exchanger.dm`. A pair of unary devices that have to be placed flat face to flat
face; each one's pipe points away from its partner, so the pair bridges two separate pipelines
without ever joining them. Both mixtures are set to the equilibrium temperature, which is exactly
what TG's `combined_energy / combined_heat_capacity` computes.

- The partner is the anchored `GasHeatExchanger` in the tile the device faces (the side opposite its
  `pipe` node), with its own pipe pointing away from the pair and on the same pipe layer.
- Two devices placed *back to back* (pipes pointing at each other) are rejected: their pipe nodes
  connect instead, which would silently merge the two pipelines. This is the failure mode that made
  the first version of the device look broken when two of them were placed facing each other.
- The sprite underlay is `pipeUnaryConnectors`, not `pipeStraight`: a pass-through pipe drawn through
  the machine's flat face was the other reported bug.
- Unpowered, no UI, no gas transfer.
- Skips when either node resolves to the immutable space mixture (no pipeline attached), when both
  sides are already within `MinimumTemperatureDelta` (0.1 K by default) of each other, or when the
  combined heat capacity is negligible.

### `GasMeter` (atmos meter)

`/tg/station` `meter.dm`. A gauge that sits on a pipe and reads it.

- Uses a single `monitored` `PipeNode` with `pipeDirection: Longitudinal`, exactly like `GasPipeSensor`.
- Maps pressure to five bands (the same 0.15 / 1.8 / 30 / 59 atm steps as TG) and swaps the sprite
  state; the exact pressure and temperature are on examine.
- **Two sprite layers, both visible above a floor tile.** The casing is mapped to
  `enum.SubfloorLayers.FirstLayer` and the gauge bar to `enum.GasMeterVisualLayers.Dial`, a
  fork-local enum in `Content.Shared/_PS/Atmos/GasDeviceVisuals.cs`; both are listed in
  `SubFloorHide.visibleLayers` and the entity uses `drawdepth: FloorObjects`, the same trick the air
  vent uses. The fork-local enum exists so the shared `SubfloorLayers` enum keeps no Palmtree-only
  value.
- The temperature tint is applied to the gauge bar only, so the casing keeps its own colours.
- The meter does not turn with the pipe - it is read from above - but it still ships as three
  prototypes so the RPD can place it on any pipe layer.

## Pipe layers

Each device ships as three prototypes (`…`, `…Alt1`, `…Alt2`) plus `alternativePrototypes`, the same
shape the vanilla pumps use. The RPD picks the right one at placement time and a multitool layer
change swaps the sprite through `AtmosPipeLayers.spriteRsiPaths`.

The layer offsets were **measured**, not guessed: `pump.rsi` vs `pump_alt1/2.rsi` shows

- vertical pipes (south/north): layer 2 is `+7 px` in x, layer 3 is `-7 px` in x
- horizontal pipes (east/west): layer 2 is `-7 px` in y, layer 3 is `+7 px` in y

## Art

The sprites are the TG originals, not new art: `temperature_gate(-1)`/`tpump` come from
`icons/obj/machines/atmospherics/binary_devices.dmi`, the heat exchanger from `unary_devices.dmi`
and the meter from `icons/obj/pipes_n_cables/meter.dmi`, all from the commit above. The vanilla SS14
atmos devices are already derived from the same files (see the `copyright` field of
`pump.rsi/meta.json`), so the set stays visually consistent.

TG draws one facing per device, so the generator builds the four SS14 facings as: south as-is, north
flipped vertically, east/west rotated 90° (`rotate(90)` for east, `rotate(-90)` for west - the same
convention `pump.rsi` follows). The meter does not turn with the pipe.

The red pipe coupling at the end of the gate and pump sprites is kept: every vanilla pump sprite has
one and it marks the outlet, which is how players read which way a pump pushes.

Regenerate with `Tools/gen_atmos_tg_sprites.py --tg ../tgstation-master`. The tgstation checkout is
not vendored, so point `--tg` at an unpacked archive of the commit above; the script rewrites the
`_PS` RSIs in place.

## Things that were deliberately not done

- **Emissive overlays** - TG has `-emissive` states for the gate and pump indicator lights. SS14
  would need them as a second unshaded layer per state; not needed for the devices to be readable.
- **Multitool inversion for the gate** - TG flips it with a multitool, this port uses a UI checkbox
  plus examine text (no `ToolRefinable` plumbing).
- **`/tg/station`'s turf meter** (`/obj/machinery/meter/turf`) - a meter that reads the tile's air
  instead of a pipe. Easy to add later as a `GasMeter` variant that calls
  `AtmosphereSystem.GetContainingMixture` instead of reading its node.

## Passive gate RPD entry

`GasPassiveGate` existed end to end (component, system, three pipe layer prototypes, construction
graph, guidebook page, sprites) but was missing from the RPD radial, so it was only reachable through
the construction menu. Added as a `PumpsValves` mode in `Resources/Prototypes/_PS/RCD/rpd.yml`.
