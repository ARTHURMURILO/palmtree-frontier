# System: Heat pump (two-port thermomachine) (2026-11)

> The freezer and heater that /tg/station had between March 2021 and April 2022: a thermomachine with
> two pipe connections that **moves** heat instead of making it. Palmtree ships it as a sidegrade next
> to the vanilla freezer/heater, which keep working exactly as they always have.
> Source: /tg/station `/obj/machinery/atmospherics/components/binary/thermomachine`, final version at
> commit `d7f3828d` (PR [#57831](https://github.com/tgstation/tgstation/pull/57831), removed in
> [#66411](https://github.com/tgstation/tgstation/pull/66411)).

## Locations

| What | Where |
|---|---|
| Component (state, limits, readings) | `Content.Shared/_PS/Atmos/Components/GasHeatPumpComponent.cs` |
| Shared system (messages, examine, appearance, safeties) | `Content.Shared/_PS/Atmos/EntitySystems/SharedGasHeatPumpSystem.cs` |
| Server system (the heat maths, power, explosion) | `Content.Server/_PS/Atmos/EntitySystems/GasHeatPumpSystem.cs` |
| Client system + window + BUI | `Content.Client/_PS/Atmos/` |
| Prototypes (3 pipe layers) | `Resources/Prototypes/_PS/Entities/Structures/Piping/Atmospherics/heat_pump.yml` |
| Circuitboard | `Resources/Prototypes/_PS/Entities/Objects/Devices/Circuitboards/heat_pump.yml` |
| Lathe recipe | `Resources/Prototypes/_PS/Recipes/Lathes/heat_pump.yml` |
| Research node | `Resources/Prototypes/_PS/Research/atmos.yml` (`NFAtmosphericHeatPump`) |
| Sprites | `Resources/Textures/_PS/Structures/Piping/Atmospherics/heat_pump.rsi` |
| Sprite generator | `Tools/gen_heat_pump_sprites.py` |
| Guidebook | `Resources/Prototypes/_PS/Guidebook/engineering.yml`, `Resources/ServerInfo/_PS/Guidebook/Engineering/HeatPump.xml` |
| Locale | `Resources/Locale/en-US/_PS/atmos-devices.ftl` |
| Tests | `Content.IntegrationTests/Tests/_PS/Atmos/HeatPumpTest.cs` |

Upstream files touched: the engineering guidebook category (one child line, `# Palmtree`) - nothing else.

## The two ports

- **Gas port** (`gas`, `pipeDirection: South`): the pipe being conditioned. It sits where a freezer's
  port does, so a heat pump drops into an existing wall without moving pipes.
- **Waste port** (`waste`, `pipeDirection: North`): where the heat goes when cooling and where it comes
  from when heating.

Green cuff = gas, red cuff = waste, which is /tg/station's own colour language (`COLOR_LIME` on the
machine's facing, `COLOR_MOSTLY_PURE_RED` on the opposite one).

## The maths

```
C1 = heat capacity of the gas port (J/K)      C2 = machine heat capacity (J/K)
heat = (target - gas) * C1*C2/(C1+C2) * dt    # joules this tick, negative while cooling
exchanged = heat * efficiency                 # negative = the gas loses heat
gas   += exchanged
waste += -exchanged + motor                   # motor = the compressor's own waste heat
```

Everything the gas loses arrives in the waste loop, so the only heat that appears from nowhere is the
compressor's `MotorHeat`, and that is the electricity it burns.

### Efficiency

Cooling is the hard direction:

```
difference = max(1, waste.T - gas.T)
efficiency = clamp(1 - log10(difference) * 0.08, 0.65, 1)       # a hot loop hurts
efficiency *= min(moleEfficiency(gas), moleEfficiency(waste))    # an empty pipe hurts a lot
efficiency = max(efficiency, EfficiencyFloor)                    # parts decide the floor
moleEfficiency(m) = m < 5 ? 0.1 : max(1 - 5/(m+1), 0.1)
```

Heating skips all of that (efficiency 1) and only needs gas in the waste loop to draw from.

### Power

Superlinear in the heat moved, like /tg/station, but with numbers that fit an SS14 APC:

```
load = IdlePower + |heat/s| * PowerPerHeat * (1 + |heat/s| / PowerSoftCap)
load *= 1.5 - 0.5 * efficiency
```

### Safeties

Waste loop above `SafeTemperature` (500 000 K): the machine switches itself off and pops a warning.
With the safeties off - via the interface, or because the machine was emagged - it detonates instead,
with a chance that grows with the loop temperature (75% at the safe temperature, 100% at 1e8 K).

### Parts

| Part | Effect |
|---|---|
| `MatterBin` 2x | `HeatCapacity = 5000 * 2^(rating-1)` |
| `Manipulator` 2x | `EfficiencyFloor = min(0.25 * rating, 1)` |

`RefreshPartsEvent` (Frontier's machine upgrading) drives both.

## Deviations from /tg/station, and why

1. **Heating takes heat from the waste loop.** /tg/station's heater made heat out of nothing - the
   `exchange_target` update only ran in the cooling branch. That is the asymmetry that made freezers
   "harder" and heaters free. Here the waste loop is never optional, in either direction. This is a
   deliberate change, not an oversight.
2. **The power curve is rescaled.** /tg/station's `(heat*0.05 + idle) ^ (1.05 - ...)` works out to a
   couple of watts for normal jobs; SS14 machines draw from an APC and a SMES, so the curve keeps the
   superlinear shape with sane magnitudes.
3. **No metallic hydrogen upgrade.** /tg/station let 3 sheets of metallic hydrogen make the machine
   work with no waste loop at all. This fork has no metallic hydrogen (no crystallizer), and the
   upgrade removed the reason to plumb a loop in the first place. If it is wanted later, it is a
   component flag that skips the waste-port requirement.
4. **No tank slot.** /tg/station's version could hold a gas tank and pump its contents into the waste
   port, which existed to bootstrap a loop. In SS14 a canister and a connector do that already.
5. **Explosion does not vent the pipes.** In BYOND the machine held the gas, so exploding had to dump
   it; here the gas lives in the pipe network, which survives the machine.
6. **Auto-switching is kept.** The machine picks cooling or heating from the setpoint every tick, just
   like /tg/station's `cooling = temperature_target_delta <= 0`. There is no separate freezer and
   heater prototype; the sprite picks the freezer or heater body from the mode.

## Art

No new art. The body is the vanilla `thermomachine.rsi` freezer/heater states; the port cuffs are the
vanilla `pipe.rsi` pipe tinted the colours /tg/station used, drawn as a 6 px band at each end so they
stay readable over the machine body in all four facings. `Tools/gen_heat_pump_sprites.py` builds the
RSI from those two files, so there is no external dependency to re-run it.

The machine does not ship `_alt1`/`_alt2` sprite variants: the body fills the tile, so the 7 px pipe
layer shift that the pumps use would push it out of its cell. All three pipe layer prototypes share the
same sprite and differ only in `AtmosPipeLayers.pipeLayer`.
