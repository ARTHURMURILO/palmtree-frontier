# System: Temperature Atmos Devices (2026-11)

> Four atmos devices from /tg/station that have no Space Station 14 equivalent, plus the passive
> gate that already existed in the engine but was never added to the RPD.
> Source: `tgstation-master/code/modules/atmospherics/machinery/components/{binary,unary}_devices/`
> and `.../atmospherics/machinery/other/meter.dm` (commit `b40b836a6e` checkout, TG payload).

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
| Locale | `Resources/Locale/en-US/_PS/atmos-devices.ftl` |
| Guidebook | `Resources/ServerInfo/Guidebook/Engineering/TemperatureDevices.xml` |
| Tests | `Content.IntegrationTests/Tests/_PS/Atmos/TemperatureDevicesTest.cs` |

Everything lives under the `_PS` prefix so upstream merges stay clean. The only upstream files
touched are the RPD mode list (`_PS/RCD/rpd.yml`, already a Palmtree file) and the guidebook
category (`Resources/Prototypes/Guidebook/engineering.yml`, one child entry).

## Devices

### `GasTemperatureGate` (temperature gate)

`/tg/station` `temperature_gate.dm`. A binary device that releases gas from its inlet to its outlet
while the inlet gas is colder than `TargetTemperature` (or hotter when `Inverted`).

- Powered (200 W) like the vanilla pumps; `AtmosDeviceDisabledEvent` closes the UI and switches it off.
- Flow uses the same maths as `GasPressureRegulatorSystem`: transfer at most
  `MaxTransferRate * PumpSpeedup() * dt`, and never more than it takes to equalise the two sides.
  That keeps it from pushing gas into a fuller pipeline, and it means the gate is tick-rate invariant.
- UI: on/off, target temperature with `±10/100/1000`, "max" and "use current" buttons, invert checkbox.
  The server dirties the sensor readouts once a second (same pattern as the pressure regulator).
- Sprites: `tgate` / `tgateOn` / `tgateFlow`.

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
exactly and is independent of the atmos tick rate.

### `GasHeatExchanger` (heat exchanger)

`/tg/station` `heat_exchanger.dm`. **Design decision:** TG uses a *pair* of unary devices that are
placed facing each other and find each other through `atmos_init`. That pairing is fragile in SS14
(rotation, pipe layers, copy/paste, destruction), so the pair is folded into one binary device whose
inlet and outlet are simply two separate pipelines. Both mixtures are set to the equilibrium
temperature, which is exactly what TG's `combined_energy / combined_heat_capacity` computes.

- Unpowered, no UI, no gas transfer.
- Skips when both nodes resolve to the same pipeline, when either side is the immutable space
  mixture (an unconnected node), or when the combined heat capacity is negligible.

### `GasMeter` (atmos meter)

`/tg/station` `meter.dm`. A gauge that sits on a pipe and reads it.

- Uses a single `monitored` `PipeNode` with `pipeDirection: Longitudinal`, exactly like `GasPipeSensor`.
- Maps pressure to five bands (the same 0.15 / 1.8 / 30 / 59 atm steps as TG) and swaps the sprite
  state; the exact pressure and temperature are on examine.
- **Visible above the floor**: `SubFloorHide.visibleLayers` keeps the body layer and the sprite uses
  `drawdepth: FloorObjects`, the same trick the air vent uses. The gauge bar is baked into the body
  states (`meter0` … `meter4`) because `VisibleLayers` only accepts enum layer keys, and a second
  string-keyed layer could not be made visible.
- The temperature tint is applied to that single layer with a `GenericVisualizer` colour entry.

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
and the meter from `icons/obj/pipes_n_cables/meter.dmi`. The vanilla SS14 atmos devices are already
derived from the same files (see the `copyright` field of `pump.rsi/meta.json`).

TG draws one facing per device, so the RSI generator builds the four SS14 facings as: south as-is,
north flipped vertically, east/west rotated 90°. The meter does not turn with the pipe - it is read
from above.

Regenerate with `gen_atmos_tg_sprites.py` (workspace root, next to `dmi2rsi.py`); it reads the
`tgstation-master` checkout and rewrites the `_PS` RSIs.

## Things that were deliberately not done

- **Meter temperature tint on a separate layer** - see above; the bar is baked into the body.
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
