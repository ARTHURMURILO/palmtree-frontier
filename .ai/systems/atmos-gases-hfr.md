# System: /tg/ gas suite, Hypertorus Fusion Reactor, turbines and chalk (2026-11)

> Ported from **Funky Station** (itself a downstream of /tg/station): ten extra atmos gases,
> the Hypertorus Fusion Reactor (HFR), the Far Horizons gas turbines with multi-tile flatpacks,
> the electrolyzer / crystallizer / portable pipe scrubber and the construction chalk.
> Source: `funky-station/funky-station` master (HFR PR #775 and surrounding content), /tg/station
> for the gas maths. This is the PR #14 port; the reactors are the separate NSV13 work.

## The gas suite

| What | Where |
|---|---|
| `Gas` enum, `TotalNumberOfGases`, `GasNames`, reaction constants | `Content.Shared/Atmos/Atmospherics.cs` |
| Mixture storage (arrays sized by `AdjustedNumberOfGases`) | `Content.Shared/Atmos/GasMixture.cs` |
| Gas prototypes + tile overlay metadata | `Resources/Prototypes/Atmospherics/gases.yml` |
| Reactions (15 new + six vanilla reactions patched) | `Resources/Prototypes/Atmospherics/reactions.yml` |
| Reagents for the new gases | `Resources/Prototypes/Reagents/gases.yml` |
| Canisters (Frontier model, one colour per gas) | `Resources/Prototypes/_Funkystation/Entities/Structures/Storage/Canisters/gas_canisters.yml` |
| Supermatter coefficients and the two conversions | `Content.Shared/_EE/Supermatter/Components/SupermatterComponent.cs`, `Content.Server/_EE/Systems/SupermatterSystem.Processing.cs` |
| Tests | `Content.IntegrationTests/Tests/_Funkystation/Atmos/*`, `FunkyAtmosPrototypesTest.cs`, `Content.IntegrationTests/Tests/_EE/SupermatterTest.cs` |

- Gas ids **11-20 are appended after Frontier's `Helium = 10`**: `BZ, Healium, Nitrium, Pluoxium,
  Hydrogen, HyperNoblium, ProtoNitrate, Zauker, Halon, AntiNoblium`. Never renumber the enum; that
  would corrupt every save and every reaction array.
- **`Atmospherics.Tmax` stays 262144 K.** Funky raised it to `262144000` so the HFR could reach
  25,000,000 K. Do not bring that back: it is a global clamp for every mixture in the game, it
  costs precision, and a save written at those temperatures cannot be loaded by any other build.
  The HFR is scaled to the fork's ceiling instead (next section).
- **Hyper-noblium suppresses fire.** Six vanilla reactions (`PlasmaFire`, `TritiumFire`,
  `FrezonCoolant`, `FrezonProduction`, `N2ODecomposition`, `AmmoniaOxygen`) return `NoReaction`
  when the mixture holds at least 5 mol of hyper-noblium and is above 20 K, matching /tg/. The
  ported reactions copy the guard; two of them (`MetalHydrogenReaction`,
  `PluoxiumRadiationProductionReaction`) do not, same as Funky, and `WaterVaporReaction` is not
  guarded upstream either. That is intentional behaviour, not a bug.
- The new gases are append-only for saves: `GasMixture.AfterDeserialization` pads older saves.
  Anything that assumes "11 gases" (UI tables, monitor thresholds, per-gas arrays) must use
  `TotalNumberOfGases`/`AdjustedNumberOfGases`; the atmos monitor and sensor UIs gained guards for
  gases with no threshold.
- The `NoRecharge` tag (cell/weapon/wall/turbo charger blacklists) exists so the HFR crystal cell
  cannot be recharged.

## Hypertorus Fusion Reactor

| What | Where |
|---|---|
| Constants, conversion formulas, `TemperatureScale` | `Content.Shared/_Funkystation/Atmos/HFR/HypertorusFusionReactor.cs` |
| Core component (settings, mixtures, runtime links) | `Content.Server/_Funkystation/Atmos/Components/HFRCoreComponent.cs` |
| Part components (console, corners, fuel/moderator input, waste output) | `Content.Server/_Funkystation/Atmos/Components/HFR*Component.cs` |
| Main loop + fusion maths | `Content.Server/_Funkystation/HFR/Systems/HypertorusFusionReactorSystem.cs`, `HypertorusFusionReactor.Processing.cs` |
| Part systems (linking, anchoring, UI) | `Content.Server/_Funkystation/Atmos/EntitySystems/HFR*System.cs` |
| Console UI | `Content.Client/_Funkystation/Atmos/UI/HFRConsoleWindow.*`, `HFRConsoleBoundUserInterface.cs` |
| Fusion recipes (`fusionRecipe` prototype) | `Resources/Prototypes/_Funkystation/Atmospherics/hfr.yml` |
| Entity prototypes, construction graph, lathe, cargo crate, research | `Resources/Prototypes/_Funkystation/Entities/Structures/Machines/Atmospherics/hfr.yml`, `Recipes/Construction/Graphs/utilities/hfr.yml`, `Recipes/Lathes/hfr.yml`, `Catalog/Cargo/cargo_atmospherics.yml`, `_NF/Research/techtree.yml` |
| Guidebook | `Resources/ServerInfo/_Funkystation/Guidebook/Engineering/HypertorusFusionReactor.xml`, `Resources/Prototypes/_Funkystation/Guidebook/engineering.yml` |
| Tests | `Content.IntegrationTests/Tests/_Funkystation/HFR*Test.cs` |

### Temperature scale (the one thing to get right)

The Funky maths is authored for a **25,000,000 K** fusion ceiling. The fork's atmos ceiling is
`Atmospherics.Tmax` (262,144 K), so the reactor runs at a scaled temperature range:

- `TemperatureScale = Atmospherics.Tmax / 25,000,000` (~0.01048576; one Funky degree is one
  hundredth of a kelvin here).
- `FusionMaximumTemperature = Atmospherics.Tmax`.
- Every **absolute temperature** and every **kelvin-per-second heat rate that is relative to the
  range** is expressed through `TemperatureScale` (cold-coolant threshold, negative heat cap,
  over-temperature cooling, power-level table).
- The **positive per-tick heat cap is absolute** (not scaled): it is calibrated against the heat
  output at ambient temperature (~48 K/event versus the 50 K cap for h2+tritium). Scaling it would
  make leaving power level 0 take tens of minutes.
- **Gas production divides the scale back out** (`scaledHeatOutput = HeatOutput / TemperatureScale`)
  so a given power level produces the same moles/second of byproducts as Funky. This is why the
  reactor is still a gas factory even though its recovered heat (and turbine power) is ~95x lower.
- `PowerLevelUpperTemperatures` / `GetPowerLevel` are **shared by the server and the console graph**
  (500 K first boundary so ambient air is still level 0, geometric steps to 40% of the ceiling).
  Change the table in one place only.
- Heat-up and equilibrium were measured: level 1 ~6 s, level 6 ~44 s (Funky 4.7 s / 54.7 s), the
  ceiling is reachable without the atmos clamp interfering. Those numbers assume a heating
  conductor of 500: the reactor starts at 50 (`HFRCoreComponent.HeatingConductor`), the console
  clamps the setting to 50-500, and `hfr.yml`'s printed quick guide tells the crew to set 500 to
  kickstart the reaction (then drop to ~100 once the power level exceeds 1). The unpowered-console
  fallback also runs at 500.
- **Never raise `Atmospherics.Tmax`** to make an engine reach its design temperature. Scale the
  engine (this is also the `.ai/HAZARDS.md` rule).

### Assembly and control invariants

- Part links are **runtime only, not serialised, in both directions**: the core holds `ConsoleUid`,
  `FuelInputUid`, `ModeratorInputUid`, `WasteOutputUid` and `CornerUids`, and every part holds its
  own `CoreUid`. They are rebuilt from the anchored parts on startup/anchor (`TryLinkComponent`,
  `TryLinkCorner`, `TryFindCore`), and `ComponentShutdown` on either side clears both. Relinking
  happens on entity startup — keep it that way; `HFRReviewFixesTest.AssemblyRelinksAfterSaveLoad`
  guards the save/load round-trip. Do not re-add `[DataField]` to any link: a save/load would
  resurrect a dead or recycled UID.
- `AreAllPartsConnected` is the activation gate (`ToggleActiveState` refuses to start otherwise),
  and `Process` deactivates a reactor that loses a part. It requires **exactly one corner per
  diagonal**: four corner links alone are not enough, because two corners on one diagonal and a
  missing fourth are rejected. Unbolting is blocked while active, but deletion is not.
- A stale console UID must be dropped before `PowerReceiverSystem.IsPowered` (it returns true for a
  deleted receiver). Pipe nodes are resolved through `TryGetPartPipe`, which tolerates deleted
  parts.
- Console draw is owned by `HFRConsoleSystem.SetPowerState`: 350 W idle, 100 kW running, back to
  350 W after a meltdown or when the core is gone. Do not set `ApcPowerReceiverComponent.Load`
  directly from `ToggleActiveState`.
- **Recipe changes are refused while `IsActive` or `PowerLevel > 0`** (the meltdown flags are read
  from the selected recipe at detonation, so a mid-countdown swap would otherwise be a griefing
  lever). The console disable-buttons in the UI mirror the server check; a legal change dumps the
  leftover fusion gases through the waste path.
- Meltdown (91 s countdown) emits **every mole of both internal mixtures exactly once** (pockets,
  then the 3x3, leftovers merged back); the blast intensity is a function of power level only
  (12-tile port scaling, not Funky's ~38).
- There is no "cooling volume" control: /tg/ resized the coolant port mixture, which has no safe
  SS14 equivalent, so the control/field/message were removed rather than wired to the wrong thing.
- `AtmosSystem.React` runs inside the core, so normal gas chemistry happens there (the h2+o2 and
  plasma+o2 recipes can fire inside the reactor). That is expected.

## Turbines, flatpacks and chalk

| What | Where |
|---|---|
| Turbine main loop (flow, RPM, stator, damage, power) | `Content.Server/_FarHorizons/Power/Generation/FissionGenerator/TurbineSystem.cs` |
| Turbine state (persisted `Ruined`, runtime fields) | `Content.Shared/_FarHorizons/Power/Generation/FissionGenerator/TurbineComponent.cs` |
| Flatpack site works + pack-up + packed state | `Content.Server/_FarHorizons/Power/Generation/FissionGenerator/TurbineAssemblySystem.cs`, `TurbineFlatpackStateComponent.cs`, `Content.Shared/_FarHorizons/.../TurbineAssemblyComponent.cs` |
| Multi-tile footprint check (shared by server and ghost) | `Content.Shared/_PS/Construction/SharedFlatpackPlacementSystem.cs`, `Components/FlatpackFootprintComponent.cs`, `Content.Server/_PS/Construction/FlatpackPlacementSystem.cs`, `Content.Client/_Funkystation/Construction/FlatpackPlacementGhostSystem.cs` |
| Chalk tool, marks, radial and build-from-mark | `Content.{Shared,Server,Client}/_Funkystation/ConstructionChalk/`, `Resources/Prototypes/_Funkystation/ConstructionChalk/chalk_categories.yml`, `Entities/Objects/Tools/construction_chalk.yml` |
| Tests | `Content.IntegrationTests/Tests/_Funkystation/{Turbine*,ConstructionChalkTest}.cs`, `Tests/_PS/AtmosPipeLayerHijackTest.cs` |

Persistence invariants that have already caused bugs:

- **Pipe stubs are found by position, never by parentage.** `TurbineSystem.FindOrSpawnPipe` used to
  search the turbine's children; the map loader reparents anchored stubs to the grid, so after a
  save/load the search missed them and every load spawned another pair. It now looks for the
  `TurbineGasPipe` prototype on the target tile (world position, 1 m slack; the inlet and outlet are
  two tiles apart), re-anchors a reused stub, and deletes duplicates left by older saves.
- **Tile ids must be prototype ids, not ints.** `TurbineAssemblyComponent.FloorTileId` and
  `TurbineFloorRestore.OriginalTileId` store `ITileDefinitionManager` prototype IDs and resolve them
  at pack time; the old `int` fields were session-local and broke deck restoration after a reload.
- `TurbineComponent.Ruined` is persisted; the spark/smoke flags are runtime and rebuilt on startup
  without replaying their popups or sounds. `TurbineAssemblyComponent.Deployed` is persisted so a
  deployed turbine stays packable after a reload; map-spawned turbines get no pack-up verb.
- `TurbineFlatpackStateComponent` carries blade health / ruin flag / stator load / flow rate so
  pack-unpack cannot repair a turbine for free.
- The footprint check rejects empty tiles, anchored entities (except subfloor cables and non-primary
  pipes), **living mobs and non-anchored collidable bodies**. The client ghost calls the same
  `CanPlace`, so the preview matches the server.
- Chalk marks are capped (`ConstructionChalkComponent.MaxMarks`, 128 active marks globally) and
  expire (`MarkLifetime`, 15 minutes, via `TimedDespawnComponent`); placement over the cap shows a
  popup and spawns nothing. Erase/build-from-mark paths are unchanged.

## Upstream files the port patches (all annotated)

- `Content.Shared/Atmos/Atmospherics.cs` — gas enum/constants (append-only), **not** `Tmax`.
- `Resources/Prototypes/Atmospherics/{gases,reactions}.yml`, `Resources/Prototypes/Reagents/gases.yml`.
- Supermatter coefficients/conversions under `Content.Shared/_EE` and `Content.Server/_EE`.
- `Content.Shared/Construction/{SharedFlatpackSystem.cs, Components/FlatpackComponent.cs}` — unpack
  delay and the footprint event hooks (the flatpack footprint/placement files themselves were moved
  to `_PS`, they are Palmtree code).
- `Content.Client/Atmos/AlignAtmosPipeLayers.cs`, `Content.Client/Construction/ConstructionPlacementHijack.cs`
  — pipe-layer placement rebuilt through `IAtmosPipeLayerHijack` (`Content.Client/_Funkystation/Placement/`)
  so any hijack in that placement mode can switch layer; RCD/RPD use their own modes and are not
  part of this path.
- `Content.Server/Construction/ConstructionSystem.Initial.cs` — extracted
  `TryStartStructureConstructionAt` and a GUID material-container key (parallel construction by
  design; the per-session `_beingBuilt` set still blocks duplicate ghosts and materials are removed
  synchronously before the do-after starts).
- Atmos monitor/sensor guards, charger blacklists, lathe packs, vox/guidebook locale, engineering
  lockers/EngiVend chalk boxes.
- `Content.IntegrationTests/PoolManagerTestEventHandler.cs` — test time limit raised 20 → 30 min for
  the fork's ~19-minute pooled suite.

## Licensing / attribution

- HFR audio (`Resources/Audio/_Funkystation/Hypertorus/`) is /tg/station CC-BY-SA-3.0; provenance
  and per-file source links live in its `attributions.yml`. Funky only carried the files.
- The HFR console's Font Awesome 6 Free `.ttf`s are SIL OFL 1.1; `LICENSE.txt` ships next to them.
- The Far Horizons turbine code/assets keep their original headers and `_FarHorizons` prefix.
