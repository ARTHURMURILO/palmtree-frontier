# Ship Combat Rework (tactical/shielding consoles, weapons, torpedoes, shields, bluespace power)

**Status:** In progress (branch `ship-combat`, milestones M1–M11 below).
**Scope:** Palmtree-only (`_PS`). New grid-level ship combat state, two consoles, three weapon
families, torpedoes with steering, directional shields, the bluespace power sink/thruster, ship
alarms and Condition Sirens, plus the `PSShipCombat` research line.
**Touched areas:** `Content.{Shared,Server,Client}/_PS/ShipCombat/`,
`Resources/Prototypes/_PS/ShipCombat/`, `Resources/{Audio,Textures}/_PS/...`,
`Resources/Locale/en-US/_PS/ship-combat.ftl`, marked `// Palmtree` patches in
`Content.Client/UserInterface/Controls/FancyWindow.xaml`, `Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`,
`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs`, `Content.Server/Shuttles/Systems/ThrusterSystem.cs`
(if needed), and `BluespaceDockSystem.RestoreMapInitRegistration`.

## Locked design decisions (maintainer Q&A)

| Area | Decision |
|---|---|
| Condition | GREEN = peacetime lockout (all fire controls disabled), YELLOW = armed, RED = battle stations. Popup confirmation on change. Auto Condition raises Green→Yellow on damage. Condition Sirens active on RED. |
| Lock | One lock per grid, shared by all consoles. 2 s base lock time, `Capacitor` part tiers 2 / 1.5 / 1 / 0.75 s. MASTER SAFE or GREEN = deny beep, no lock. |
| Guns | Device-link sink ports `Primary`/`Secondary`/`Aux`. `Automatic Fire` checkbox (default off) fires the selected group at the lock; `MANUAL FIRE` fires the selected group once. Both require a lock and are disabled on MASTER SAFE/GREEN. |
| Torpedoes | Separate `FIRE TORPEDOES` button below manual fire; always manual (event API left open for future AI ships). Unlimited ammo, per-launcher cooldown, ripple fire. Requires a lock. Launchers are never in auto-fire groups. |
| Power model | STO-adapted: a fixed 200-point pool across Shields/Weapons/Thrusters/Aux, each settable 15–125, sum always 200, baseline 50, `|` marker at 100, defaults 50/50/50/50. Raising one pool lowers the others proportionally; per-pool locks and Attack/Defense/Balanced presets. Budget = the sink's rated throughput (upgradeable via `PowerCell` parts); the battery buffers and everything browns out proportionally when the battery is empty and intake is short. |
| Power effects | Weapons damage `(p+100)/200` (torpedoes unaffected, STO rule); shields regen pulse ×`p/50` and hardness 0.2%/pt damage negation; thrusters thrust `1 + (p−50)×1%`; Aux shares `p/200` of throughput to the ship's grid. Offline below 25 points with the `(!)` alert. |
| Shields | 4 facings. `Up` absorbs while charge > 0; at 0 → `Down` (no absorption from that side); re-arms above 20% of max (restored beep `input_ok_1_clean.ogg`). Regen pulse every 6 s adds `ratedRegen × (p/50)` to **each** facing (STO). Arrows = instant energy redistribution: click a facing to drain the other three into it (up to cap), `ALL` evens out; 10 s cooldown. Placeholder blue hit effect. |
| Alarms | `damagealarm2.ogg` one-shot when a facing collapses (2 s cooldown between collapses); `damagealarm.ogg` on the transition that takes all facings down; every shielding console plays them. `critical.ogg` every 5 s on every tactical console while condition ∈ {YELLOW, RED} and the grid has no powered/intact ship weapon. `consolewarning.ogg` on being locked; `processing*.ogg` during lock; `computerbeep_35.ogg` clear lock; `denybeep1.ogg` denied lock; `computerbeep_71/72.ogg` contacts entering/leaving range (2 s cooldown, only grids **with a shuttle console** count as ships; asteroids/wrecks do not beep but stay lockable). Every console sound plays at the console and is audible to anyone nearby, whether or not the window is open. `alert14/19/18.ogg` torpedo alarms (alert14 one-shot on detection <800; alert19 loops 800→200; alert18, the fast beep, loops <200). |
| Condition Sirens | Powered strobe-light variants (rotating light + looping ambient), active while the grid's condition is RED, constructible and in the tech tree: Voyager (`voy_redalert.ogg`), Buzz (`alert10.ogg`), Soft (`alert09.ogg`), TNG (`tng_red_alert1.ogg`), Deep (`alertklaxon_clean2.ogg`). |
| Research | New `PSShipCombat` tech discipline + nodes, machine/computer boards and lathe recipes for every new machine. |
| Assets | Console rsi copied from `_NF` blackmarket shipyard states (tactical plain, shielding tinted yellow); torpedo = copied `_NF` `frag-emp`; photon = red tesla sprite + `SingularityDistortionComponent`; launch sound `tng_torpedo_clean.ogg`. |

## Architecture

```
Content.Shared/_PS/ShipCombat/     ShipCombatComponent (grid), enums, events, BUI states/messages
Content.Server/_PS/ShipCombat/     ShipCombatSystem, ShipTargetingSystem, ShipWeaponSystem,
                                   ShipTorpedoSystem, ShipShieldSystem, ShipPowerSystem,
                                   ShipAlertSystem, ConditionSirenSystem
Content.Client/_PS/ShipCombat/     TacticalConsoleWindow, ShieldingConsoleWindow, BUIs, radar partial
```

`ShipCombatComponent` lives on the **grid** entity and is ensured by any ship-combat device
(idempotent `ShipCombatSystem.TryEnsureShipCombat`). Saved fields: condition, weapon mode,
auto-fire/condition/retaliation, the four power pools and their locks. Runtime-only (never
serialized): lock target/progress, incoming locks, contacts, inbound torpedoes, shield facing
HP/states, redistribution cooldown.

### Core data (final field list, filled in as milestones land)

```csharp
// Content.Shared/_PS/ShipCombat/Components/ShipCombatComponent.cs
[RegisterComponent]
public sealed partial class ShipCombatComponent : Component
{
    [DataField] public ShipCondition Condition = ShipCondition.Green;
    [DataField] public bool AutomaticCondition = true;
    // later milestones: WeaponMode, AutomaticFire, AutomaticRetaliation, power pools + locks
}
```

Events: `ShipCombatConditionChangedEvent` (M1, **broadcast** — Robust allows only one component-event
subscription per (component, event) pair, and several systems react to a condition change; filter on
`args.Grid`), plus mode/power/shield/lock events in later milestones. All ship-combat actions are
event-driven so a future AI brain can raise the same events without a console
(`ShipCombatLockRequestEvent`, `ShipCombatSetModeEvent`, `ShipCombatFireEvent`, ...).

## Power model (STO adaptation)

| Pool | Range | Baseline | `|` | Effect per point |
|---|---|---|---|---|
| Weapons | 15–125 | 50 | 100 | damage `(p+100)/200`; torpedoes unaffected |
| Shields | 15–125 | 50 | 100 | regen pulse ×`p/50`; hardness +0.2% damage negation |
| Thrusters | 15–125 | 50 | 100 | thrust `1 + (p−50)×1%` (STO uses ±2%) |
| Auxiliary | 15–125 | 50 | 100 | `p/200` of sink throughput feeds the ship's power net |

- Sum is always 200; raising one pool lowers the others proportionally; locks pin a pool; presets
  Attack (100/50/25/25), Defense (50/100/25/25), Balanced (50/50/50/50).
- Sink = SMES-like machine (input node from the generators, output node to the ship grid). Rated
  throughput is the budget; the battery buffers. When the battery is empty and intake is short,
  all effective outputs scale by `available/allocated` (brownout).
- Remote devices (bluespace thrusters, ship weapons, shield generator) draw from the sink without
  wires: they carry `ApcPowerReceiverComponent { needsPower: false }`; `ShipPowerSystem` toggles
  `PowerDisabled` + `PowerChangedEvent`, which existing systems (thrusters) already react to.
- Offline below 25 points (shields/weapons/thrusters), `(!)` alerts per the original request
  (shields below minimum, any weapon unpowered, no/underpowered bluespace thrusters, aux at risk).

## Shields

- Four facings (Fore/Port/Starboard/Aft), fixed capacity per facing (placeholder 300), direction
  from the impact vector / attacker position in ship-local space.
- `Up` absorbs the whole hit while charge > 0 (STO: any energy protects); hits 0 → `Down`; a Down
  facing does not absorb until it regenerates above 20% of max, then re-arms (`Up`).
- Every 6 s: each facing gains `ratedRegen × (p/50)` (clamped). Excess wasted.
- Redistribution (arrows): `^ < > v` drains the other three facings into the selected one up to its
  cap; `ALL` splits the total evenly; 10 s cooldown shown in the UI.
- Absorption hooks: global `BeforeDamageChangedEvent` subscription (projectiles, hitscan, melee —
  direction from `Origin`) + marked `ExplosionSystem` patches for explosion entity/tile damage
  (direction from the epicenter). Placeholder blue hit effect entity at the impact point.

## Consoles

**Tactical (ship targeting) console** — blackmarket shipyard sprite copy, dark red window, radar
range 512 (double the shuttle console), overlays on the IFF view (lock reticle, inbound missile
marker, contact edge arrows) and **hidden/hidden-label IFF player shuttles drawn red with name "?"**
(asteroids, wrecks and other unlabelled non-ship grids keep their normal appearance). Right panel:
condition display + dropdown with confirmation, `LOCK`/`MSL`/`TARGET` status (TARGET shows `NONE`,
the locked grid's name truncated to 24 characters, or `?` for unknown/hidden targets), weapons
dropdown (MASTER SAFE / ALL WEAPONS / PRIMARY / SECONDARY / AUX), `FIRE`, `FIRE TORPEDOES`,
`Automatic Fire`, `Automatic Condition`, `Automatic Retaliation`, `CLEAR LOCK`.

**Shielding (engineering) console** — same radar view, dark yellow window. Right panel: condition
row, facing arrows + `ALL`, four 15–125 sliders with `|` at 100, lock buttons, preset buttons,
per-facing mini-bars, `(!)` alerts.

Both consoles are machines with parts (tactical lock speed uses `Capacitor`).

## Weapons & torpedoes

- Turrets: SLAT sprites, no AI, 45° arc around placement angle, idle rotation inside the arc,
  remote power, fire only with the target in arc. They do **not** shoot through their own grid:
  when the firing line is blocked by the ship's own hull the turret holds fire (a cvar restores
  the original shoot-through-own-grid behaviour).
- Stationary weapons: emitter sprites, exposed space, straight fire, collide with their own grid,
  ignore their own shields, remote power.
- Torpedo launchers: exposed space (reuse `NozzleExposed`-style check), remote power, never
  auto-fire; ripple-fire from the `FIRE TORPEDOES` button.
- Torpedoes: `ProjectileComponent`-based (the dock's `DeleteProjectiles` cleans them up on save),
  slow limited steering at the grid captured at launch, long lifetime. Photon torpedo: 2× speed,
  steering starts at 0.5 s and ramps to max by 4 s, red tesla sprite + singularity distortion.
- Torpedoes render on the tactical IFF as diamonds: **red** for torpedoes from other ships,
  **green** for the console's own ship (server-driven blips in the console state so long-range
  torpedoes are visible beyond PVS).
- Incoming alarms per grid: `alert14.ogg` once when first detected under 800 tiles, looping
  `alert19.ogg` (800–200) then the fast beep `alert18.ogg` (<200); >30° deviation = miss,
  silence; `MSL: YES` while inbound.
- `ShipWeaponComponent.FiresWhileSafe` leaves the door open for future non-hostile weapons
  (traction beams) usable on MASTER SAFE; targets still get the lock warning.

## Dock / save compatibility

- Console runtime references are never `[DataField]`s (locks, contacts, torpedo targets) — they
  are dropped by the engine on save.
- Torpedoes are projectiles → the existing `DeleteProjectiles` save/load cleanup applies.
- `RestoreMapInitRegistration` gets a step that ensures `ShipCombatComponent` and re-registers
  sink/thruster/shield devices for grids saved before/around this feature.
- Tests: full store/retrieve round trip preserving condition, power settings and shield state,
  with no dangling lock references.

## Tests

`Content.IntegrationTests/Tests/_PS/ShipCombatTest.cs` (grid component, condition, events,
ensure/self-heal) plus per-milestone files: `ShipTargetingTest`, `ShipWeaponsTest`,
`ShipTorpedoTest`, `ShipShieldTest`, `ShipPowerTest`, and dock round-trip extensions in the
existing `BluespaceDock*` tests. Automatic prototype validation via `EntityTest`.

## Milestones

1. **M1** Grid component + condition enum/events + ensure/self-heal + tests
2. **M2** Tactical console shell: prototype, radar, dark-red window, condition UI, contact beeps, audio/sprites copied
3. **M3** Locking, LOCK/MSL, auto-condition/retaliation
4. **M4** Power sink + 200-point budget + remote power + bluespace thruster
5. **M5** Shields: facings, 6 s regen, hardness, redistribution, placeholder hit effect
6. **M6** Shielding console: sliders, locks, presets, arrows, mini-bars, `(!)` alerts
7. **M7** Weapons: turrets, stationary, group linking, auto/manual fire
8. **M8** Torpedoes: launchers, torpedo, photon, incoming alarms
9. **M9** Alarms & Condition Sirens
10. **M10** `PSShipCombat` research discipline + boards + lathe recipes
11. **M11** Dock round-trip tests, `.ai` docs, full validation pipeline

## Open questions

None — all resolved with the maintainer before implementation (see the decisions table).
