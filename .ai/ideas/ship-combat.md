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
| Lock | One lock per grid, shared by all consoles. 2 s base lock time, `Capacitor` part tiers 2 / 1.5 / 1 / 0.75 s. MASTER SAFE or GREEN = deny beep, no lock. While a lock is being acquired, further lock requests are ignored (no spam-retargeting); switching targets is allowed again once it completes. Dropping to GREEN clears the lock. |
| Threat display | Contacts that are not on MASTER SAFE or are at YELLOW+ condition render **red** on the tactical IFF. Ships locking this one get a **yellow** cursor; when the locker is armed the cursor flashes **yellow/orange**. The console's own lock target keeps the red square. |
| Damage reaction | Automatic condition (and retaliation) trigger on `BeforeDamageChangedEvent`, so being attacked raises the alert even when armour absorbs the whole hit. |
| Guns | Device-link sink ports `Primary`/`Secondary`/`Aux`. `Automatic Fire` checkbox (default off) fires the selected group at the lock; `MANUAL FIRE` fires the selected group once. Both require a lock and are disabled on MASTER SAFE/GREEN. |
| Torpedoes | Separate `FIRE TORPEDOES` button below manual fire; always manual (event API left open for future AI ships). Unlimited ammo, per-launcher cooldown, ripple fire. Requires a lock. Launchers are never in auto-fire groups. |
| Power model | STO-adapted: a fixed 200-point pool across Shields/Weapons/Thrusters/Aux, each settable 15–125, sum always 200, baseline 50, `|` marker at 100, defaults 50/50/50/50. Raising one pool lowers the others proportionally; per-pool locks and Attack/Defense/Balanced presets. Budget = the sink's rated throughput (upgradeable via `PowerCell` parts); the battery buffers and everything browns out proportionally when the battery is empty and intake is short. |
| Power effects | Weapons damage `(p+100)/200` (torpedoes unaffected, STO rule); shields regen pulse ×`p/50` and hardness 0.2%/pt damage negation; thrusters thrust `1 + (p−50)×1%`; Aux shares `p/200` of throughput to the ship's grid. Offline below 25 points with the `(!)` alert. |
| Shields | 4 facings. `Up` absorbs while charge > 0; at 0 → `Down` (no absorption from that side); re-arms above 20% of max (restored beep `input_ok_1_clean.ogg`). Regen pulse every 6 s adds `ratedRegen × (p/50)` to **each** facing (STO). Arrows = instant energy redistribution: click a facing to drain the other three into it (up to cap), `ALL` evens out; 10 s cooldown. Placeholder blue hit effect. |
| Alarms | `damagealarm2.ogg` one-shot when a facing collapses (2 s cooldown between collapses); `damagealarm.ogg` on the transition that takes all facings down; every shielding console plays them. `critical.ogg` every 5 s on every tactical console while condition ∈ {YELLOW, RED} and the grid has no powered/intact ship weapon. `consolewarning.ogg` on being locked; `processing*.ogg` during lock; `computerbeep_35.ogg` clear lock; `denybeep1.ogg` denied lock; `computerbeep_71/72.ogg` contacts entering/leaving range (2 s cooldown, only grids **with a shuttle console** count as ships; asteroids/wrecks do not beep but stay lockable). Every console sound plays at the console and is audible to anyone nearby, whether or not the window is open. `alert14/19/18.ogg` torpedo alarms (alert14 one-shot on detection <800; alert19 loops 800→200; alert18, the fast beep, loops <200). |
| Condition Sirens | Powered strobe-light variants (strong rotating red light + looping ambient). Each variant is tied to one condition and stays off otherwise; unpowered sirens stay off. Suffix naming: `[YELLOW]` (`alert09.ogg`), `[RED, TOS]` (`alert10.ogg`), `[RED, VOYAGER]` (`voy_redalert.ogg`), `[RED, TNG]` (`tng_red_alert1.ogg`), `[RED, DEEP]` (`alertklaxon_clean2.ogg`). Constructible and in the tech tree (M10). |
| Battle light | A `Poweredlight` variant that switches itself off at RED and back on at YELLOW or GREEN, so ships can black out for battle stations. |
| Display names | The shielding console is displayed as the **engineering console** (no other engineering console exists, so the tactical console keeps its name) and the bluespace power sink as the **bluespace power bypass**. Prototype ids are unchanged for bluespace dock save compatibility. |
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
  Attack (100/50/25/25), Defense (50/100/25/25), Balanced (50/50/50/50). Presets override locks.
- Sink = SMES-like machine (input node from the generators, output node to the ship grid; must be
  anchored). Rated throughput is the budget (100 kW placeholder); the battery buffers. When the
  battery is empty and intake is short, all effective outputs scale by `available/allocated`
  (brownout). The Auxiliary share (`aux/200 × throughput`) is fed back to the grid by the sink's
  **`PowerSupplier`** (the output side is deliberately *not* a `BatteryDischarger`: a battery
  endpoint on both sides can form charger/discharger loops with other batteries, which trips the
  power solver's DEBUG-only network-height assertion and crashes the server). The system drains the
  battery by the energy the grid actually drew.
- Remote devices (bluespace thrusters, later weapons/shields) carry `BluespacePoweredComponent`
  (pool + rated power) and `ApcPowerReceiverComponent { needsPower: false }`; `ShipPowerSystem`
  toggles `PowerDisabled` + performance each tick. Thrusters additionally scale their thrust via
  `ThrusterSystem.SetThrust` (fork method) and keep their parts thrust in
  `BluespaceThrusterComponent.PartsThrust` (saved, captured on MapInit/parts refresh).
- Offline below 25 points (shields/weapons/thrusters), `(!)` alerts per the original request
  (shields below minimum, any weapon unpowered, no/underpowered bluespace thrusters, aux at risk).

## Shields

- Four facings (Fore/Starboard/Aft/Port, in the grid's local frame where +Y is fore), fixed capacity
  per facing from the ship's **powered** generators (placeholder 300 each, 30 regen per pulse).
- `Up` absorbs the whole hit while charge > 0 (STO: any energy protects); hits 0 → `Down`; a Down
  facing does not absorb until it regenerates above 20% of max, then re-arms. The hysteresis keeps
  the collapse alarm from flapping.
- Every 6 s: each facing gains `RegenPerPulse × generator performance` (clamped). Excess wasted.
- Hardness: each shield power point negates 0.2% of the absorbed damage.
- Redistribution (arrows): `^ < > v` drains the other three facings into the selected one up to its
  cap; `ALL` splits the total evenly; 10 s cooldown. Redistributing into a downed facing can re-arm it.
- Absorption hooks: `BeforeDamageChangedEvent` (all entity damage; direction from the attacker,
  explosions with no origin hit the strongest facing) + a marked `ExplosionSystem` hook
  (`ShieldTileDamageAttemptEvent` raised on the grid before floor tiles are destroyed).
  Absorbed hits spawn the `ShieldImpactEffect` placeholder (blue gravity-pulse sprite) and still
  count as being attacked (auto condition/retaliation).
- The shield state lives on the grid (`ShipShieldComponent`, facings saved; pulse timers and
  cooldown runtime) and is created by the shield generator.

## Consoles

**Tactical (ship targeting) console** — blackmarket shipyard sprite copy, dark red window, radar
range 512 (double the shuttle console), overlays on the IFF view (lock reticle, inbound missile
marker, contact edge arrows) and **hidden/hidden-label IFF player shuttles drawn red with name "?"**
(asteroids, wrecks and other unlabelled non-ship grids keep their normal appearance). Right panel:
condition display + dropdown with confirmation, `LOCK`/`MSL`/`TARGET` status (TARGET shows `NONE`,
the locked grid's name truncated to 24 characters, or `?` for unknown/hidden targets), weapons
dropdown (MASTER SAFE / ALL WEAPONS / PRIMARY / SECONDARY / AUX), `FIRE`, `FIRE TORPEDOES`,
`Automatic Fire`, `Automatic Condition`, `Automatic Retaliation`, `CLEAR LOCK`.

**Shielding (engineering) console** — same radar view, dark yellow window (placeholder yellow tint
on the console sprite). Right panel: condition display + dropdown with confirmation, the facing
arrow grid (`^ < ALL > v` plus a redistribution cooldown line), per-facing charge readouts, four
15–125 sliders with the `|` marker at 100, per-pool lock checkboxes, Attack/Defense/Balanced preset
buttons and bold red `/!\` alerts (shields below minimum or an unpowered generator, any unpowered
weapon, no/underpowered bluespace thrusters, Auxiliary below minimum).

Both consoles are machines with parts (tactical lock speed uses `Capacitor`).

## Weapons & torpedoes

- Turrets (M7): SLAT placeholder sprites, no AI, 45° arc around the placement angle, idle sweep
  inside the arc, remote power, rotate towards the locked target and fire once aligned (5°
  tolerance). Turrets **always turn towards the locked target** (auto-fire only decides whether they
  shoot) and weapons do nothing until the lock is *set*: no aiming or firing while a lock is still
  being acquired. `ps.ship_combat.turrets_shoot_through_hull` (**default false**) makes turrets
  **hold fire while the ship's own hull blocks the firing line**: the turret pops `Prevented from
  firing` (at most every 3 s), the tactical console's **status** section shows `WEAPONS: BLOCKED BY
  HULL` and the `shipcombat` log names the blocker (at most every 10 s). Setting the cvar to true
  restores shooting through the ship's own grid (marked `SharedProjectileSystem` and `GunSystem`
  fork hooks keyed on the weapon's runtime `ShootThroughHull`, which `ShipWeaponSystem` derives from
  the cvar and the weapon's `IgnoreOwnGrid` capability). The block check uses the same `Opaque` ray a
  projectile takes, so floors never block but walls, windows, machines - and a hull wall on the
  turret's own tile - do. Two variants: **bolt** (`ShipTurret`, travelling
  projectile) and **beam** (`ShipTurretBeam`, instant hitscan; infinite ammo via a self-recharging
  battery, slower fire rate). Both scale damage with the Weapons pool — projectiles through
  `GunDamageMultiplierSystem`, hitscan through a marked `GunSystem` hook.
- Stationary weapons (M7): emitter placeholder sprites, exposed space (muzzle tile must be space),
  fire straight along their facing when the target is ahead (placeholder ±45° check), collide with
  their own grid, and a ship's own weapons bypass its own shields.
- Torpedo launchers (M8): exposed space (muzzle tile must be space), remote power, never
  auto-fire; `FIRE TORPEDOES` fires one torpedo from every ready launcher (ripple), each launcher
  with its own cooldown and no ammo limit. Needs a set lock, a non-GREEN condition and a non-MASTER
  SAFE weapon mode. Torpedoes launch 1.5 tiles in front of the launcher with the launcher as their
  shooter (so they ignore it on the way out and a misfire at our own ship bypasses our shields).
- Torpedoes (M8): `ProjectileComponent`-based (the dock's `DeleteProjectiles` cleans them up on
  save), steered every tick towards the grid captured at launch with a limited turn rate (10 tiles/s,
  20°/s, 120 s lifetime, 150 heat). Photon torpedo (separate upgraded launcher): 20 tiles/s, 45°/s,
  steering starts at 0.5 s and ramps to full by 4 s, 300 heat, red glow. The limited turn rate means
  a manoeuvring target can make them miss.
- Torpedoes render on the tactical IFF as diamonds: **red** for torpedoes from other ships,
  **green** for the console's own ship (server-driven map-space blips in the console state so
  long-range torpedoes are visible beyond PVS).
- Incoming alarms per grid (M8): `alert14.ogg` once when a torpedo is first detected inbound under
  800 tiles, then repeated `alert19.ogg` (800-200) or the fast `alert18.ogg` beep (<200); a torpedo
  whose heading is more than 30° off counts as a miss and stays silent; `MSL: YES` while inbound.
  All torpedo alarms respect the console's Silent checkbox.
- Weapon groups come from device-link sink ports `ShipWeaponPrimary/Secondary/Aux`; the tactical
  console carries the `ShipWeaponLink` source port. Firing is gated by GREEN, MASTER SAFE, power and
  (for turrets) the firing arc; the `Automatic Fire` checkbox fires the selected group at the lock
  and `MANUAL FIRE` fires it once. Damage scales with the Weapons pool through
  `GunDamageMultiplierComponent` (unpowered weapons cannot fire).
- Aim selection (M7 feedback): the tactical console's `Aim` dropdown picks what the weapons shoot at
  on the locked grid — `RANDOM` (grid centre), `HULL` (anchored structure), `GENERATORS` (power
  sources and sinks), `WEAPONS`, `THRUSTERS` and `CREW` (living things: alive or critical mobs,
  which are not anchored, so the shot goes straight at them). The subsystem is re-picked every 2 s so
  turrets do not jitter between targets.
- Lock range is enforced on the server: `TryStartLock` refuses a target beyond the requesting
  console's radar range (the range comes from the console's own `RadarConsoleComponent`, never from
  the client message), and a lock - held or still being acquired - is dropped automatically the
  moment the target leaves that range.
- Both consoles carry a `Silent` checkbox (off by default): a silent console plays no sounds at all
  (contact beeps, lock sounds, the condition alarm), per console.

## Large scale exceptions

Ship combat happens at ranges far beyond the PVS bubble (~32 tiles), so a few things deliberately
bypass it:

- **Beams**: the vanilla hitscan effects are widened for ship weapons by a marked `GunSystem`
  (`FireEffects`) patch: ship weapon effect coordinates are based on the **map** instead of the firing
  grid (a client far outside the shooter's PVS does not know that grid, so it could not resolve or
  draw grid-relative effects) and the `HitscanEvent` is sent with a 24x PVS range multiplier
  (~600 tiles at the default 25 tile PVS range), covering the whole engagement envelope. The muzzle
  flash, beam line and impact flash therefore render for the victim at full range. This is inherently
  gated on a shot actually firing, because `FireEffects` only runs for real shots.
- **Gunshots** stay vanilla (`Gun.soundGunshot`, PVS limited). An earlier attempt broadcast them from
  `ShipWeaponSystem.TryFire`, which runs every tick while auto-fire is on (the gun's rate limit lives
  inside `AttemptShoot`), so it spammed at tick rate and the beam visual never rendered; that was
  reverted. If far-audible gunshots come back, gate them on the shot actually happening (hook
  `GunShotEvent`, or compare `GunComponent.NextFire` around `AttemptShoot`).
- **Stack overflow guard**: the engine's `TilesEnumerator.MoveNext` recurses once per skipped tile,
  so view-bounded tile overlays (ambient occlusion, roofs) overflow the stack on very large views
  (huge viewport, low render scale, zoomed out). `TileEnumerationGuard` skips views above
  `ps.client.tile_overlay_area_limit` (default 4096 tiles squared). RobustToolbox itself is never
  modified.
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
9. **M9** Alarms & Condition Sirens: condition sirens and battle lights done (see the decisions
   table); shield alarms done (`damagealarm2` per facing collapse at most every 2 s, `damagealarm`
   once when every facing is down, a restored beep when a facing re-arms, on every shielding
   console, Silent-aware) and `critical.ogg` every 5 s on every tactical console while the ship is
   out of peacetime with no powered weapon or torpedo launcher. Per-facing mini-bars still pending.
10. **M10** `PSShipCombat` research discipline + boards + lathe recipes
11. **M11** Dock round-trip tests, `.ai` docs, full validation pipeline

## Open questions

None — all resolved with the maintainer before implementation (see the decisions table).
