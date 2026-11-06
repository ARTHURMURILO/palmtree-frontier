# Reactor ship persistence

**Status:** **Implemented (2026-11-06).** The data-restore fixes landed on the engine branches and the
policy layer below landed on `experimental`:

- `BluespaceDockSystem.FindLiveReactor` / `IsLiveReactor` is the gate; it is wired into both
  `FindStoreBlocker` (so the console refuses early) and `TrySaveShipFile` (the choke point every
  store goes through).
- `BluespaceDockSystem.SafeReactors` is the scram: `RbmkReactorSystem.ShutDown` (now public) for the
  RBMK and `StormdriveSystem.Deactivate` for the drive, plus the remaining fuse/warning state.
- `forcedock --scram` runs the scram *before* the ordinary checks, so force-dock cannot smuggle a
  live core out either; the admin log records how many cores were safed.
- Locale key: `bluespace-dock-error-live-reactor-aboard`.
- Regression test: `Content.IntegrationTests/Tests/_PS/BluespaceDockReactorTest.cs`.

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
| Stormdrive | `State` is `Idle` or `Maintenance`, `Heat < 50`, `ReactorEndTimes == false`, `WarningState == None`, no `StormdriveAnomalySurge`/`Sheer`/`Squall` on the grid |

**Do not gate on fuel rods.** With the restore fix a cold, fuelled reactor stores and reloads
correctly, and forcing the crew to eject five rods before every dock visit is hostile. If the
maintainer wants a rod rule, refuse with a message; never auto-eject player property.

### 2.2 What the file preserves

Preserve the permanent facts, normalise the timers and streams:

* Keep: `Slagged`, `VesselIntegrity`, rod `Depletion`, rod age (relative, never an absolute round
  time), control-rod percentage, mounted rods and their integrity/effectiveness, `ReactorId`, device
  links, the installed parts.
* Normalise before the save: `NextProcess`, `NextAlert`, `NextGravityPull`, `NextCountdownAnnounce`,
  `MeltdownAt`, `WarningState`, `ReactorEndTimes`, `AlarmStream`, `OverheatStream`. The restored
  machine is exactly what a normal shutdown produces.
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
| `BluespaceDockSystem.FindStoreBlocker` (`:2663`) | the gate. Both the dock console and `ForceDockCommand` already call it and it already returns a locale key. Add a grid scan for `RbmkReactorComponent` / `StormdriveComponent` and return a new key. |
| `BluespaceDockSystem.TrySaveShipFile` (`:663`), next to `ResetRuntimeCooldowns` | normalise each reactor's timers/streams (2.2) so every save path, including tests and direct callers, gets the same state. |
| `BluespaceDockSystem.RestoreMapInitRegistration` (`:2103`) | the documented place for "MapInit was skipped, rebuild the runtime bookkeeping". The branch-side `ComponentStartup` handlers already do this; a call here is only needed if the maintainer wants it centralised. |
| `BluespaceDockSystem.ResetRuntimeCooldowns` (`:2212`) | add the reactors' absolute timers (`NextProcess`, `NextAlert`, `NextGravityPull`, `NextCountdownAnnounce`, `MeltdownAt`) next to the existing AME/thruster entries, if the normalisation above does not cover them. |
| `ForceDockCommand.Execute` (`:101`) | the `--scram` policy above; log it. |
| New locale keys | e.g. `bluespace-dock-error-reactor-hot`, `...-reactor-running`, `...-reactor-fuse`, in `Resources/Locale/en-US/_PS/ships/` (or `bluespace_dock.ftl`). |

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

Until the gate lands, these make a stored ship consistent even though "store a hot reactor to dodge a
meltdown" remains. The gate is what closes that.

---

## 5. Tests

Add to the existing dock fixture (`Content.IntegrationTests/Tests/_PS/BluespaceDockShipSystemsTest.cs`):

* store and retrieve `RbmkNuclearReactorRunning`: `FuelRods.Count == 5`, `Slagged` survives,
  `VesselIntegrity` survives, temperature is cold;
* store and retrieve `StormdriveReactorRunning`: `ControlRods.Count == 5`, `ControlRodIntegrity > 0`
  after ten ticks, `ReactorEndTimes == false`;
* store a slagged RBMK: still `Slagged`, radiation source still enabled;
* the gate: a running/hot reactor refuses the store with the new key;
* `forcedock --scram` on a running reactor: stored, rods on the deck, admin log written.

---

## 6. Open questions for the maintainer

* Is 50 °C the right stormdrive threshold, or should it be "state is Idle/Maintenance" alone?
* Should a slagged RBMK be storable at all, or should the wreck have to be cleaned up first?
* Do we want a `ShipStoreAttemptEvent`, or is a direct scan in `FindStoreBlocker` acceptable for two
  machines?
* Should `ResetRuntimeCooldowns` own the reactor timers, or should each reactor normalise itself in
  its own save hook?
