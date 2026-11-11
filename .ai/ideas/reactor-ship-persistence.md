# Reactor ship persistence

**Status:** **Implemented (2026-11-06), hardened 2026-11-10.** The data-restore fixes, the policy
layer and the save/load normalisation all ship on `engines-reactors`:

- `BluespaceDockSystem.FindLiveReactor` / `IsLiveReactor` is the gate; it is wired into both
  `FindStoreBlocker` (so the console refuses early) and `TrySaveShipFile` (the choke point every
  store goes through). The threshold is **exclusive**: below 50 °C stores, at 50 °C refuses.
- `BluespaceDockSystem.SafeReactors` is the scram: `RbmkReactorSystem.ShutDown` (public) for the
  RBMK and `StormdriveSystem.Deactivate` for the drive, plus the remaining fuse/warning state.
- `forcedock --scram` runs the scram *before* the ordinary checks, so force-dock cannot smuggle a
  live core out either; the admin log records how many cores were safed.
- **Save/load normalisation (2026-11-10):** `BluespaceDockSystem.ResetRuntimeCooldowns` ends with
  `NormalizeReactorRuntime`, and because that hook already runs on save *and* load, both paths share
  one behaviour. It clears the RBMK's `AlertActive`/`NextTick`/`NextFlicker` and the stormdrive's
  `ReactorEndTimes`/`MeltdownAt`/`WarningState`/`NextProcess`/`NextAlert`/`NextCountdownAnnounce`/
  `NextGravityPull`/`DumpingFuel`. It does **not** touch `VesselIntegrity`, `Slagged`, `DesiredK`,
  temperature, the rod containers, rod depletion/age or control-rod settings.
- The branch-side restore fixes remain the other half: `FuelRods`/`ControlRods` rebuilt in
  `ComponentStartup`, `Slagged`/`VesselIntegrity` serialized, rod depletion and relative rod age
  serialized.
- Locale key: `bluespace-dock-error-live-reactor-aboard`.
- Tests:
  - `Content.IntegrationTests/Tests/_PS/BluespaceDockReactorTest.cs` — the gate and its edge cases
    (`TemperatureExactlyAtTheThresholdBlocksStorage`, `RbmkAlertAloneBlocksStorage`,
    `StormdriveWarningStatesBlockStorage`, `MaintenanceStateIsStorableOnlyWhileCold`,
    `MultipleReactorsReturnAStableOffender`, `ReactorFlatpacksAreNotLiveCores`).
  - `Content.IntegrationTests/Tests/_PS/BluespaceDockReactorSaveTest.cs` — real save→load round
    trips (`ColdFuelledRbmkSurvivesTheRoundTrip`, `SlaggedRbmkStaysWreckedAcrossTheRoundTrip`,
    `FuelledStormdriveSurvivesTheRoundTrip`, `LiveReactorNeverReachesTheShipFile`,
    `FuelRodDepletionSurvivesTheRoundTrip`,
    `InvalidContainerReferencesDoNotBreakReactorScansOrSaves`).
  - `Content.IntegrationTests/Tests/_NSV13/ReactorBaseSliceTest.cs` — the reactors-only slice: a
    directly spawned RBMK and stormdrive tick with no flatpack, cargo, research or console.

**Scope:** RBMK (`engines-rbmk`) and stormdrive (`engines-stormdrive`), plus the shared save path in
`Content.Server/_PS/BluespaceDock/BluespaceDockSystem.cs` and
`Content.Server/_PS/Commands/ForceDockCommand.cs`.

**Touched areas:** `_PS/BluespaceDock`, `_PS/Commands/ForceDockCommand.cs`, `RbmkReactorSystem`
(`ShutDown` made public), one locale key, and the dock integration test fixture.

---

## 1. The problem

A live grid save serialises `[DataField]`s and container contents, then marks every entity
`mapInit: true`. On load, `MapInitEvent` does not fire, and container contents are restored straight
into `Container._containerList` without insert events (`.ai/HAZARDS.md` section 24).

For the reactors that means the saved file is not the machine that comes back:

| Today | Consequence |
|---|---|
| `RbmkReactorComponent.FuelRods` is rebuilt only from insert events | retrieved RBMK has 0/5 rods registered, rods trapped in the container, core inert, and it accepts five more rods |
| `StormdriveComponent.ControlRods` is rebuilt only from `MapInitEvent` / insert events | retrieved drive has 5 rods in the bay, 0 in the simulation: `ControlRodIntegrity` recomputes to 0, cooling is off for good, rods cannot be ejected |
| `VesselIntegrity` / `Slagged` are runtime-only | store/retrieve is a free full repair and un-melts a slagged RBMK |
| `StormdriveComponent.MeltdownAt` is runtime-only, `ReactorEndTimes`/`WarningState` are saved | a stored armed fuse is cancelled on load, or detonates on the first tick with no countdown; a saved `Overheat` warning comes back silent |
| `RbmkFuelRodComponent.SpawnedAt` is runtime-only | self-decaying rods come back with the whole round uptime as their age, i.e. nearly dead |
| `PowerSupplier.MaxSupply`, the radiation source, temperature, `DesiredK`, `running` are saved | a hot machine restores hot with no coolant loop or pipe gas behind it |

The two branch-side restore fixes already landed (`FuelRods`/`ControlRods` rebuilt in
`ComponentStartup`, `Slagged`/`VesselIntegrity` serialized, relative rod age). They close the data
loss, the free repair and the phantom fuse. What remains is the policy layer below: a stored reactor
must be cold and stopped, and the save must not carry transient state it cannot restore.

---

## 2. Policy

### 2.1 What may be stored

A reactor may only be stored cold and stopped. Everything else is refused at the dock console with a
clear message, not silently fixed.

| Engine | Gate |
|---|---|
| RBMK | `IsRunning == false`, `Temperature < 50 °C`, `AlertActive == false`. A slagged wreck is allowed (it is legitimate cargo) and stays slagged. |
| Stormdrive | `State` is `Idle` or `Maintenance`, `Heat < 50`, `ReactorEndTimes == false`, `WarningState == None`. The anomaly rule from the original design (no `StormdriveAnomalySurge`/`Sheer`/`Squall` on the grid) is **not implemented** - the scan only looks at the reactor components (see section 6). |

The threshold is exclusive: `49.9 °C` stores, `50 °C` refuses, matching the locale message ("cool
below 50 °C"). All of these are one scan, `BluespaceDockSystem.FindLiveReactor`.

**Do not gate on fuel rods.** With the restore fix a cold, fuelled reactor stores and reloads
correctly, and forcing the crew to eject five rods before every dock visit is hostile. If the
maintainer wants a rod rule, refuse with a message; never auto-eject player property.

### 2.2 What the file preserves

Preserve the permanent facts, normalise the timers and streams:

* Keep: `Slagged`, `VesselIntegrity`, rod `Depletion`, rod age (relative, never an absolute round
  time), control-rod percentage, mounted rods and their integrity/effectiveness, `ReactorId`, device
  links, the installed parts.
* Normalise on save *and* load, in `BluespaceDockSystem.NormalizeReactorRuntime` (called from
  `ResetRuntimeCooldowns`): the RBMK's `AlertActive`/`NextTick`/`NextFlicker`, and the stormdrive's
  `ReactorEndTimes`/`MeltdownAt`/`WarningState`/`NextProcess`/`NextAlert`/`NextCountdownAnnounce`/
  `NextGravityPull`/`DumpingFuel`. The restored machine is exactly what a normal shutdown produces.
  `DumpingFuel` is the one serialized toggle this catches; the rest are belt-and-braces for the
  gate and for files written by older builds.
* `AlarmStream`/`OverheatStream` are runtime-only audio handles and are not persisted; a live core
  that is safed stops them through the reactor's own `ShutDown`/`Deactivate` path.
* The fuel/control-rod lists are **not** restored by the save hook: the reactors rebuild them from
  their containers in `ComponentStartup` (see section 4).
* Do **not** zero `Temperature`, `DesiredK` or `IsRunning` at save time. The gate already guarantees a
  cold, stopped reactor; zeroing the setpoint throws away the player's configuration, and the pipe
  network comes back empty, so a "faithful" hot restore would be a second state machine that can
  disagree with the live one.

### 2.3 Force dock

`forcedock` exists for "the admin needs this ship stored now". It must not silently defeat the gate.
Recommended: it runs the same reactor check and **refuses**, printing the offending drive (position,
state, heat, fuse remaining), exactly like the other blockers. For admin recovery of a griefed ship,
add an explicit opt-in flag (`--scram`) that calls the reactor's own deactivate path, asserts the
result, stores, and writes an `AdminLog` line at `LogImpact.High` naming the reactor and the fact that
a live core was safed.

---

## 3. Hook points (for the maintainer)

| Where | What |
|---|---|
| `BluespaceDockSystem.FindStoreBlocker` | the gate (implemented as `FindLiveReactor`). Both the dock console and `ForceDockCommand` call it and it returns a locale key. |
| `BluespaceDockSystem.TrySaveShipFile`, next to `ResetRuntimeCooldowns` | the gate runs first, then the shared normalisation (2.2) so every save path, including tests and direct callers, gets the same state. Implemented as `NormalizeReactorRuntime`, called from `ResetRuntimeCooldowns` on save *and* load. |
| `BluespaceDockSystem.RestoreMapInitRegistration` (`:2103`) | the documented place for "MapInit was skipped, rebuild the runtime bookkeeping". The branch-side `ComponentStartup` handlers already do this; a call here is only needed if the maintainer wants it centralised. |
| `BluespaceDockSystem.ResetRuntimeCooldowns` | owns the reactor normalisation (it already runs on both save and load). This is the implemented home of 2.2; do not duplicate it in a reactor save hook. |
| `ForceDockCommand.Execute` | the `--scram` policy above; implemented, and the admin log records the scram. |
| Locale key | `bluespace-dock-error-live-reactor-aboard` (one key for both engines). |

A shared `ShipStoreAttemptEvent { EntityUid Grid; string? Blocker; }` raised from `FindStoreBlocker`
is the cleaner long-term shape: it keeps `_PS/Ships` engine-agnostic and lets each machine subscribe.
A direct scan is fine for two engines.

---

## 4. What the engine branches already fixed

Inside the reactor code, not the save path:

1. `FuelRods` rebuilt from the `rbmk_fuel` container in `ComponentStartup`, with the rods' radiation
   sources disabled.
2. `ControlRods` rebuilt from the bay in `ComponentStartup`, with `RecalculateRods`/`UpdateVisuals`.
3. `Slagged` and `VesselIntegrity` are `[DataField]`s.
4. Rod `Depletion` is saved and rod age is a relative `AgeSeconds`, never `CurTime`.
5. The stormdrive fuse and warning state are cleared on load; a hot core re-arms its own fuse.

The gate (section 2.1) is what closes "store a hot reactor to dodge a meltdown"; these restore fixes
are what make a legitimately cold, stopped reactor store and load correctly.

---

## 5. Tests

* `Content.IntegrationTests/Tests/_PS/BluespaceDockReactorTest.cs` - the gate and its edges:
  cold/running/hot refusal, scram, temperature exactly at the threshold, `AlertActive` alone,
  every stormdrive warning state, maintenance while cold/hot, several reactors on one grid, and
  flatpacks not counting as live cores.
* `Content.IntegrationTests/Tests/_PS/BluespaceDockReactorSaveTest.cs` - real save -> load round
  trips through `TrySaveShipFile`/`TryLoadShipFile`: a cold fuelled RBMK (5 rods, vessel integrity,
  setpoint, no self-start, no meltdown), a slagged RBMK, a stormdrive with 5 control rods (integrity,
  no fuse/warning, `DumpingFuel` cleared), refusal of a running core and the `--scram` path, rod
  depletion/age, and the dangling-`EntityUid.Invalid` container-reference crash class.
* `Content.IntegrationTests/Tests/_NSV13/ReactorBaseSliceTest.cs` - the reactors-only slice: a
  directly spawned RBMK and stormdrive tick with no flatpack, cargo, research or console.
* Master's dock-side coverage for the 2026-11-09 crash stays in
  `BluespaceDockSaveTest.ShipFilesWithInvalidContainerReferencesLoadClean` (`bce651dbe8`); the
  reactor tests above add the reactor-scan and save-path half.

---

## 6. Resolved decisions (2026-11-10) and open gaps

Decided and shipped:

* **50 °C stays** for both engines, and the gate is `>= 50` (exactly 50 is refused). It matches the
  locale text the console shows.
* **A slagged RBMK is storable** and stays slagged; a wreck is legitimate cargo.
* **A direct scan in `FindLiveReactor` is enough** for two engines. `ShipStoreAttemptEvent` is still
  the cleaner long-term shape if a third engine appears, but it is not needed now.
* **`ResetRuntimeCooldowns` owns the reactor timers** (via `NormalizeReactorRuntime`), so save and
  load share one behaviour and a direct `TrySaveShipFile` caller gets it too.
* **No fuel-rod gate**: a cold, fuelled reactor stores fine; rods are never auto-ejected.

Still open:

* The **stormdrive anomaly rule is not implemented.** `IsLiveReactor` does not look for the
  `StormdriveAnomalySurge`/`Sheer`/`Squall` entities the unstable core can tear loose. If those are
  meant to block a store, add a grid scan (or make the anomaly entities carry a marker component) in
  `FindLiveReactor`. They are inert stock anomalies today, so a stored ship carrying one is not a
  safety problem - it is a design question.
* `forcedock --scram` is exercised through `SafeReactors` in tests, not through the console command
  itself (the command needs a shell/admin session). The command's ordering (scram before the
  blockers) is by inspection only.
