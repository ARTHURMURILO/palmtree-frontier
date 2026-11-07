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

Upstream files touched, all one line each and marked `# Palmtree`: the engineering guidebook category
(`Resources/Prototypes/Guidebook/engineering.yml`), the `NFEngineeringBoardsStatic` lathe pack and the
blueprints pack (both under `Resources/Prototypes/_NF/Recipes/Lathes/Packs/`) so the board is actually
printable and the technology tree test stays green.

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
rate = clamp((target - gas) * C1*C2/(C1+C2), -MaxHeatRate, MaxHeatRate)
exchanged = rate * dt * efficiency           # signed: negative = the gas loses heat
gas   += exchanged
waste += -exchanged + motor                  # motor = the electricity the compressor just drew
```

Everything the gas loses arrives in the waste loop, so the only heat that appears from nowhere is the
compressor's own waste heat - and that is the electricity it burns, not a separate constant. It is
only added while cooling: /tg/station had no use for it while heating, and neither has this port.

The transfer is also clamped by what the waste loop can actually supply or absorb: an SS14 gas mixture
is clamped to `Atmospherics.TCMB`..`Tmax`, so an over-sized transfer would be silently truncated and
the heat would vanish.

### Efficiency

Cooling is the hard direction:

```
difference = max(1, waste.T - gas.T)
efficiency = clamp(1 - log10(difference) * 0.08, 0.65, 1)       # a hot loop hurts
efficiency = max(efficiency, EfficiencyFloor)                    # parts decide the floor
efficiency *= min(moleEfficiency(gas), moleEfficiency(waste))    # an empty pipe hurts a lot
moleEfficiency(m) = m < 5 ? 0.1 : max(1 - 5/(m+1), 0.1)
```

Heating has no temperature penalty (efficiency 1 before the mole factor), it still needs gas in the
waste loop to draw from, and it still pays the mole penalty - /tg/station applied the mole penalty in
the heating branch too.

The mole penalty is applied *after* the parts floor, and the floor is capped at
`MaxEfficiencyFloor` (0.9), so a fully upgraded machine cannot ignore an empty pipe.

### Power

Superlinear in the heat moved, like /tg/station, but with numbers that fit an SS14 APC:

```
load = IdlePower + |heat/s| * PowerPerHeat * (1 + |heat/s| / PowerSoftCap)
load *= 1.5 - 0.5 * efficiency
```

With the defaults a typical 18 kW job draws about 6.5 kW, and a job at the 78 kW cap draws about
40 kW. The machine is never cheaper per joule than the vanilla freezer (COP 3.9); it buys capacity and
both directions instead.

### Safeties

Waste loop above `SafeTemperature` (200 000 K): the machine switches itself off and pops a warning.
With the safeties off - via the interface, or because the machine was emagged - it detonates instead,
rolled at most once a second, with a chance that grows with the loop temperature
(`log6(T) * 10`, clamped to 100%: ~68% at the 200 000 K threshold and ~70% at `Atmospherics.Tmax`,
which is as hot as a mixture can get), and only once the loop is `DetonationMargin` (2000 K) past the
threshold. A machine with the safeties off is therefore very likely to go up within a few seconds, not
guaranteed on the first tick.

200 000 K rather than /tg/station's 500 000 K because a gas mixture here cannot go above
`Atmospherics.Tmax` (262 144 K) - the original threshold could never fire.

### Parts

| Part | Effect |
|---|---|
| `MatterBin` 2x | `HeatCapacity = 5000 * 2^(rating-1)`, `MaxHeatRate = 78 kW * 2^(rating-1)`, `PowerSoftCap = 78 kW * 2^(rating-1)` |
| `Manipulator` 2x | `EfficiencyFloor = min(0.25 * rating, 0.9)` |

`RefreshPartsEvent` (Frontier's machine upgrading) drives both. The ceiling on the transfer scales with
the bins as well as the heat capacity: `C1*C2/(C1+C2)` saturates at the gas's own heat capacity once
the machine's is large, so scaling `HeatCapacity` alone left the bins with almost nothing to do.
`PowerSoftCap` scales with it too, so an upgraded machine does not pay the superlinear penalty for
work its bigger compressor is meant to do.

`UpgradeExamineEvent` feeds the machine's "Upgrades" examine verb with the resulting heat capacity,
maximum heat rate and efficiency floor, so a player who installs better parts can see what changed.

## Deviations from /tg/station, and why

1. **Heating takes heat from the waste loop.** /tg/station's heater made heat out of nothing - the
   `exchange_target` update only ran in the cooling branch. That is the asymmetry that made freezers
   "harder" and heaters free. Here the waste loop is never optional, in either direction. This is a
   deliberate change, not an oversight.
2. **The transfer is capped** at `MaxHeatRate` (78 kW, four stock freezers' worth) and by what either mixture can take. /tg/station
   moved `(C1*C2)/(C1+C2) * delta` per tick with no ceiling, which for a 200 K difference is about
   300 kW - enough to empty a ship's APC in one tick.
3. **The compressor's waste heat is the electrical draw**, not a fixed 5000 J. /tg/station's constant
   was larger than the machine's own power draw at low load, i.e. a heat source.
4. **The parts efficiency floor is capped at 0.9 and applied before the mole penalty.** /tg/station let
   a fully upgraded machine reach 1.0, which made it ignore both the loop temperature and an empty
   pipe.
5. **The safeties threshold is 200 000 K, not 500 000 K**, because of the SS14 temperature clamp, and
   the detonation roll is limited to once a second instead of every atmos tick.
6. **The power curve is rescaled.** /tg/station's `(heat*0.05 + idle) ^ (1.05 - ...)` works out to a
   couple of watts for normal jobs; SS14 machines draw from an APC and a SMES, so the curve keeps the
   superlinear shape with sane magnitudes.
7. **No metallic hydrogen upgrade.** /tg/station let 3 sheets of metallic hydrogen make the machine
   work with no waste loop at all. This fork has no metallic hydrogen (no crystallizer), and the
   upgrade removed the reason to plumb a loop in the first place. If it is wanted later, it is a
   component flag that skips the waste-port requirement.
8. **No tank slot.** /tg/station's version could hold a gas tank and pump its contents into the waste
   port, which existed to bootstrap a loop. In SS14 a canister and a connector do that already.
9. **Explosion does not vent the pipes.** In BYOND the machine held the gas, so exploding had to dump
   it; here the gas lives in the pipe network, which survives the machine.
10. **Auto-switching is kept.** The machine picks cooling or heating from the setpoint every tick, just
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
