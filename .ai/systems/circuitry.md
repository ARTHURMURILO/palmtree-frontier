# Circuitry — Programmable Assemblies (Palmtree)

> Permanent implementation memory for the `circuitry` feature. Update this file whenever
> architecture, API surface, file layout or hazards change. The original design discussion is in
> `.ai/workspace/plan-circuitry.md` (local only). The Lua-facing reference lives in
> `.ai/systems/circuitry-lua-api.md`; keep that file authoritative for player-facing API details.

## Status

- Branch: `master` (merged from `circuitry`).
- Phase 0 (Lua sandbox spike): **complete**.
- Phases 1–3 (assembly, Lua brain/API, actions/perception/UI): **implemented, awaiting in-game/UX testing**.
- Phase 4 (construction/content polish) is partial: chassis sprite variants, ChemMaster support,
  module fabrication and the Programmable/Advanced Programmable Circuitry research nodes are in,
  while chassis construction, sound and balance work remain. Phase 5 (hardening) is in progress:
  grid and Bluespace Dock persistence, ownership administration, a Lua kill switch, editor coverage,
  Lua soak/queue tests and a many-assembly stress test are in place.
- Validation: solution build, YAMLLinter, Content.Tests, and 30 targeted `Circuitry*` integration
  tests pass (assembly/Lua, hearing, inspect, BUI open, analyzers/dispenser, ChemMaster, security,
  radio, power budget, scan kinds, signals, item/solution autonomy, decals/doors, puddles, events,
  editor Run auto-save, gas analyzer, grid save/load, Bluespace Dock round-trip, many-assembly
  stress, 16-tile sensor range, dense-room find, sensor-query throttling, synchronous and async walk-to, and synchronous do-after actions).
  The BUI test covers the installed-CPU editor and a CPU held in hand.

### Implemented Phases 1–3

- `CircuitAssembly` chassis prototype with CPU, cell, locomotion, manipulator, voice, sensor, radio
  and beaker slots. `CircuitAssemblyFilled` is the debug/spawn-menu variant with every slot
  pre-filled (basic CPU, microreactor cell, wheels, two manipulators, speaker, sensor, radio, four
  beakers and all three analyzers).
- Part prototypes: basic/advanced CPU, wheels, tracks, thruster, manipulator, speaker, sensor,
  radio module.
- Chassis variants: engineering, janitorial, medical, mining, service, peacekeeping and derelict
  assemblies reuse the corresponding SS13/tg borg chassis states from `Mobs/Silicon/chassis.rsi`.
  They are mechanically identical to `CircuitAssembly`. The empty/filled chassis itself has no
  lathe recipe and stays admin-spawn only during live testing.
- Fabrication/research: all physical modules (basic/advanced CPU, wheels, tracks, thruster,
  manipulator, speaker, sensor, radio, botanical/chemical/medical/gas analyzers) are in the
  `CircuitryParts` exosuit-fabricator pack and also listed in `NFBlueprintsEngineering` so the
  research unlocks pass the tech-tree blueprint consistency test. `NFCircuitry` and
  `NFAdvancedCircuitry` in the NF Engineering tree unlock them; Vim moved to make room and now
  follows the critter-mech node.
- Capability recomputation (hands, speech, movement, flight, sensor range, power draw, beaker),
  power gating and idle/action/movement power costs.
- Power tuning (2026-11): fractional draws. Base idle 0.05, CPU 0.25/0.4, locomotion 0.05/0.1/0.15,
  manipulator 0.05, speaker 0.02, sensor 0.05, radio 0.05, analyzers 0.05. Movement costs
  0.02–0.06 per metre; action cost is 0.02–0.1. A fully loaded assembly idles under 1 charge/sec,
  comfortably below the microreactor's 12/sec self-recharge. Regression test:
  `Content.IntegrationTests/Tests/_PS/CircuitryPowerTest.cs`.
- Direct movement and `NPCSteeringSystem` pathfinding (`robot.move`, `robot.walk_to`). Path
  targets default to a 0.5-tile arrival tolerance and are clamped to at least 0.5. With smoothing
  enabled (default), tolerance scales with current walk speed up to 1.25 tiles and steering brakes
  near the target; `walk_status` stops the path and reports `arrived` once within tolerance,
  preventing overshoot loops around small items. A new path start is rate-limited to 0.25 s, and
  commands targeting within 0.75 tiles of the current path target are no-ops, so follow loops do
  not cancel/restart pathfinding every tick.
- Lua-facing movement is synchronous by default: `walk_to` yields through `robot.wait` until
  arrival, no-path or a 60 s timeout. `walk_to_async` is the fire-and-forget variant. The C#
  pathfinding itself stays asynchronous; no server tick is ever blocked.
- Lua-facing actions that can start do-afters (`use`, `use_item`, `interact`/`harvest`, `plant`,
  `trigger`, `inject`) also wait for completion by default, with 30 s timeouts and cancellation
  reporting. Their `_async` variants expose the old immediate behavior. Instant actions (pickup,
  drop, attack, shoot, transfers, dispensing, ChemMaster, send_signal) are unchanged.
- Sensor module range is 16 tiles. Sensor lookups share a 4-per-second token bucket (burst of 4),
  so a tight `find`/`scan` loop gets `scan cooldown` instead of rescanning hundreds of times per
  resume. Multi-result scans keep the 16-result display cap; single-result `find`/`nearest_*`
  lookups search the whole range and are not hidden by closer entities.
- `LuaBrainSystem` runtime lifecycle, `LuaProgram` execution, source persistence on the CPU item.
- `CircuitLuaApiSystem` with opaque handles, scan/find/inspect, nearest/tile helpers, speech,
  item use/pickup/harvest, guns, melee, 4 indexed beakers, analyzers, reagent transfer and dispenser
  dispensing.
- `CircuitHearingSystem` queues local `EntitySpokeEvent`s; `robot.poll_event()` returns speech
  events with speaker handle, name, ID name/job and message. `robot.wait_event(timeout)` is a Lua
  shim over `poll_event` + `robot.wait` (50 ms slices).
- Sensor conveniences: `scan_mobs`, `scan_items`, `scan_structures`, `scan_doors`,
  `nearest_mob`, `nearest_person`, `nearest_item`, `nearest_structure`, `nearest_door`,
  `get_tile_relative`/`tile`, `entities_on_tile`, `distance`, `position_of`, `decals`, `scan_puddles`,
  `door_status`, `can_open` and `can_reach`. `scan_items` only returns loose, unanchored items;
  installed pipes/vents/scrubbers and `MachineComponent` machines are `structure` (walls/windows
  remain `object`). `position_of` converts a handle into local coordinates for `walk_to`;
  `decals` returns cleanable tile decals; `scan_puddles` uses the chemical analyzer to list floor-spill
  solutions; `door_status` exposes state/bolted/powered/firelock/access and `can_open`;
  `has_job`/`has_access` read nearby ID cards and `wanted_status` reads the sector criminal record
  (status, reason, initiator and history).
- Item and container introspection: `item_status(handle)` and `solution(handle [, name])` expose
  carried buckets/beakers/syringes, solution contents, injector mode and flashlight `activated`.
  Generic `transfer(source, target, amount)` moves reagents between arbitrary containers; existing
  `transfer_to`/`transfer_from` understand injector solutions, and `inject(target)` performs syringe
  injection through the real injector do-after.
- Perception events: speech, emotes (`EmotePerformedEvent` broadcast), radio, device-link signals,
  damage, melee attacks and projectile hits are queued for Lua with source/item/damage metadata.
  The radio module adds `Trigger`/`On`/`Off` device-link sinks and `Pressed`/`On`/`Off` sources;
  `trigger(handle)` uses a held remote signaller and `send_signal([state [, port]])` fires the
  selected source port.
- Botany helpers: `robot.use_item(tray)` seeds/uses tools, `robot.harvest(tray)` maps to
  `InteractHandEvent`, and `robot.water(tray, amount, beaker_slot)` maps to `transfer_to`.
- Analyzer modules: botanical (plant holder + seed values), chemical (all solutions/reagents and
  floor puddles), medical (state, health, damage types/groups) and gas (pipe/vents/canisters/mixers
  + local environment pressure, temperature, gas moles). Analyzer APIs require sensor range/LOS.
  Access
  restricted SeedData/Label/MobState properties are avoided; use public systems/access-safe fields.
- Four beaker slots (`beaker1..4`); `robot.beakers()` lists installed beakers and
  `robot.reagents(slot)` / `transfer_to`/`transfer_from(..., slot)` select one by 1-based index.
- ChemMaster API: `chem_master_status`, `chem_master_set_mode`, `chem_master_set_pill_type`,
  `chem_master_transfer`, `chem_master_create_pills` and `chem_master_bottle` reuse the existing
  server messages, so buffer, pill and bottle rules stay in `ChemMasterSystem`.
- Dispenser helper: `robot.dispense(dispenser, reagent_selector, amount, beaker_slot)` transfers from
  a reagent dispenser's storage directly into an installed beaker. `robot.interact(tray)` harvests;
  `robot.harvest` is an alias; `robot.use_item` plants seeds/uses tools because it raises the normal
  `InteractUsingEvent`; `robot.water` is a transfer alias.
- Radio: `CircuitRadio` is a part with two `EncryptionKeyHolder` slots and `TelecomExempt` (common
  channel works without a telecom server). `robot.radio_channels()` lists only the channels granted
  by installed keys (`id`, `name`, `key`, `frequency`, `default`). `robot.radio(text, channel?)`
  sends on an allowed channel; the channel may be an id, keycode (`;`, `:c`) or localized name and
  defaults to the key holder's default channel. `robot.radio` is rate-limited to one message per
  second and consumes action power. Received messages are queued as `poll_event()` events with
  `type = "radio"`, speaker handle/name/ID info, `message` and `channel` (channel id). The assembly
  has a passive `ActiveRadioComponent` kept in sync by `CircuitAssemblySystem.RefreshRadio`, while
  `CircuitRadioSystem` handles key changes and queues received radio events.
- Security: `Locked`, `OwnerUserId`, `OwnerName` on the assembly. The first lock claims ownership;
  only the owner or an admin can modify/unlock. All insertion, eject, rename, run/stop, program save
  and CPU-editor paths enforce this. Non-owners can still view the panel (view-only). There is no
  player-facing ownership transfer field: the owner unlocks, another player locks to claim, and
  admins can use `circuitowner <assembly> <username|clear>` to transfer or reset ownership. A future
  ID/access module or component can give the assembly its own identity for doors and records.
- Program source: basic CPU cap 256 KiB, advanced 512 KiB; server validates UTF-8 byte size and
  rate-limits saves to one per second per CPU. `CircuitCpuState.Source` is nullable and only sent on
  open/save, not on every 0.5 s status refresh, so large programs do not repeatedly cross the
  network. Lidgren fragments the save message; the server cap is the safety boundary.
- Server BUIs + client assembly panel and code editor. The installed CPU editor is served by the
  chassis (`CircuitCpuUiKey` on `CircuitAssembly`) because opening a BUI on a CPU item inside an item
  slot fails the interaction range assert. A CPU held in hand still uses its own `CircuitCpuUiKey`.
- CPU editor: a content-side `LuaCodeEditor` control replaces the engine `TextEdit`, providing
  inline Lua syntax colors, line numbers, mouse/keyboard selection, copy/cut/paste, Tab-to-spaces,
  auto-indent on Enter, undo/redo (Ctrl+Z/Ctrl+Y), and paste normalization of tabs/newlines. The
  window adds line/byte/limit info, dirty state, Save/Revert, Run/Stop, Clear log, assembly name,
  lock/power state, and console output. Ctrl+S saves and Ctrl+Enter runs from inside the editor.
  Run sends the current editor source and the
  server stores it before starting, so it cannot execute a stale saved program. Editor integration
  test: `Content.IntegrationTests/Tests/_PS/CircuitryEditorTest.cs`.
- Persistence: the chassis overrides `BaseMob`'s `save: false` with `save: true`. Loaded live saves
  keep `mapInit: true`, so the runtime slot caches are rebuilt in `CircuitAssemblySystem.OnStartup`
  rather than only on `MapInitEvent`. `CircuitryPersistenceTest` covers both plain grid save/load and
  a full `BluespaceDockSystem.TrySaveShipFile`/`TryLoadShipFile` round trip with CPU source, modules,
  lock and owner intact.
- Rate limits: `say` 0.5 s, radio 1 s, signal/trigger 0.5 s, injection 0.5 s, reagent transfer
  0.5 s, ChemMaster transfer 0.5 s, ChemMaster pill/bottle production 0.5 s, scan 0.25 s, program
  save 1 s. The `circuitowner` admin command logs administrative ownership changes; player
  transfers are validated server-side by `CanModify`. `circuitry.lua.enabled` is a server CVar kill
  switch that stops all runtimes on the next tick and rejects new starts.
- Custom `BaseWindow` subclasses do **not** draw a background by themselves; both circuitry windows
  include a full-size `PanelContainer StyleClasses="AngleRect"` plus `MouseFilter="Stop"`.

### Hand IDs

Hand IDs must **not** collide with item-slot container IDs. `HandsSystem.AddHand` uses the hand name
as a container ID; if a hand is named `left_arm` while the manipulator item slot is also `left_arm`,
the hand container is the module slot and the hand appears to hold the installed manipulator. Use
the `hand_<slot>` prefix (`hand_left_arm`, `hand_right_arm`) and test pickup after installing an arm.

Known MVP limits: the chassis has no construction/lathe chain (its modules are fabricated); no
access chip, weapon mount, or `on_tick` callbacks. Radio encryption keys are inserted by using them
on the chassis; extraction uses the module's normal screwdriver interaction after ejecting it.
ChemMaster buffer transfer, pill and bottle production are available through the dedicated
`chem_master_*` API. Future work: an identification module/component that gives the assembly its own
job/access identity, rather than only reading nearby ID cards. `robot.find` shares the sensor-query
token bucket with `scan` and the `nearest_*` helpers, so tight loops receive `scan cooldown` instead
of rescanning every iteration. It only sees `LookupFlags.Uncontained` entities: a wrench inside a
toolbox, belt, backpack or hand is invisible to it; use `robot.held()` for the assembly's own items
or inspect the carrier/container. Single-entity lookups return handles: `find` and all `nearest_*`
helpers return a handle, or `nil, reason` (no sensor, no power, cooldown, or no match). Multi-entity
lookups keep their table shape, with each entity entry carrying its handle in `.id`.

## Design summary

A robot is a mob entity with physical module slots and a removable CPU item:

> chassis + installed modules + CPU Lua program

Modules grant capabilities; Lua grants behaviour. There is no wiring graph, pin UI, logic-gate
component or arithmetic/memory circuit component. Logic lives in Lua.

### Assembly slots

| Slot role | Contents | Grants |
|---|---|---|
| `Cpu` | circuit CPU item | Lua execution |
| `Cell` | power cell | power |
| `Locomotion` | wheels/tracks/legs/thruster | movement, pathfinding, flight |
| `Manipulator` | robot arm | hands, pickup, item use, guns |
| `Voice` | speaker | speech |
| `Sensor` | camera/scanner | scan/find/inspect/hearing |
| `Radio` | radio module + encryption keys | channel-scoped radio send/receive |
| `Reagent` | glass beaker | reagent queries/transfer |

Slots are normal `ItemSlots` containers. A `CircuitAssemblyComponent` maps slot IDs to roles and
recomputes capabilities when contents change.

### Components / systems

- `CircuitAssemblyComponent` — slot-role map + runtime capability cache.
- `CircuitPartComponent` — locomotion/manipulator/voice/sensor stats.
- `CircuitCpuComponent` — program source, source/instruction/tick limits, last editor.
- `CircuitAssemblySystem` — slot insertion/ejection, capability recomputation, hands/speech/movement
  gating, power-slot wiring.
- `CircuitMovementSystem` — direct movement for `robot.move`; pathfinding through
  `NPCSteeringSystem` for `robot.walk_to`.
- `CircuitPowerSystem` — idle draw, movement/action charge, power gating.
- `LuaBrainSystem` — one `LuaProgram` per running assembly/CPU, lifecycle, power/pause/death
  handling.
- `CircuitLuaApi` — registers the `robot` table; owns opaque handle map, perception and actions.
- `CircuitHearingSystem` — queues local `EntitySpokeEvent`s for sensor-equipped robots.
- `CircuitRadioSystem` — refreshes `ActiveRadioComponent` when keys change and queues received
  `RadioReceiveEvent`s into the Lua event queue as `radio` events.
- `CircuitAssemblyUiSystem` / `CircuitCpuUiSystem` — BUIs.
- `Content.Client/_PS/Circuitry/UI/*` — assembly panel and code editor.

## Lua runtime facts (Phase 0)

- Package: MoonSharp `2.0.0`, server-only (`Content.Server`), BSD-3-Clause.
- Source is loaded with `Script.LoadString`; never `DoString` on player code.
- The definition chunk runs as a coroutine under `AutoYieldCounter`; `main` is extracted afterwards.
- **Yield semantics:** `AutoYieldCounter` preemption → `YieldRequest` + `Coroutine.State ==
  ForceSuspended`. Explicit `robot.wait` → yielded value directly + `Coroutine.State == Suspended`.
  Completion → `Dead`. Always inspect `Coroutine.State`.
- `robot.wait` cannot yield through `pcall`/C-call boundaries. Do not expose the `Coroutine` stdlib.
- Sandbox: HardSandbox + metatables/error handling; no `io`/`os`/`require`/`load`/`debug`/
  `package`/`collectgarbage`; guarded `string.rep`/`string.format`.
- GLua-style helpers installed from a trusted chunk: `table.HasValue`/`Contains`, `IsEmpty`,
  `Count`, `GetKeys`, `KeyFromValue`, `Random`, `Add`, `RemoveByValue`; `string.Trim`,
  `StartsWith`/`StartWith`, `EndsWith`, `Split`; type shorthands `istable`, `isstring`, `isnumber`,
  `isbool`, `isfunction`. These are Lua-only convenience helpers, not entity APIs; no GLua
  filesystem/networking/entity/hook/userdata surface is exposed.
- Budgets: source cap, `AutoYieldCounter`, per-tick wall-clock budget, no-wait timeout,
  per-resume allocation watchdog.

## Lua API target (Phases 2–3)

```lua
function main()
    robot.say("Booting")
    while true do
        local ev = robot.poll_event()
        if ev and ev.type == "speech" then
            local who = robot.inspect(ev.speaker)
            if ev.message:lower():find("help") and who.job == "Engineer" then
                robot.say("On my way, " .. who.name)
            end
        end
        robot.wait(0.25)
    end
end
```

Implemented/planned methods:

- Core: `robot.log`, `print`, `robot.wait`, `robot.self`, `robot.position`, `robot.power`,
  `robot.health`, `robot.has_module`.
- Movement: `robot.move(dx, dy)`, `robot.walk_to(x, y)`, `robot.stop`, `robot.walk_status`.
- Perception: `robot.scan(radius)`, `robot.scan_mobs`, `robot.scan_items`, `robot.find(name, radius)`,
  `robot.inspect(handle)`, `robot.can_see`, `robot.poll_event`.
- Actions: `robot.pickup`, `robot.drop`, `robot.held`, `robot.use`, `robot.use_item`,
  `robot.attack`, `robot.shoot`.
- Reagents: `robot.reagents`, `robot.transfer_to`, `robot.transfer_from`.
- Handles are opaque integers from scan/self/held; every action validates capability, range and LOS.
- `robot.position()` and `robot.walk_to(x, y)` use the assembly's local grid coordinates. `scan`
  entries return their target's local coordinates; entities on another grid cannot be walked to.
- `inspect` exposes name, kind, species, dead/alive, ID name/job, worn items by slot, held items and
  a small whitelisted component set. It never exposes raw components/EntityUids.
- Hearing is server-side via `EntitySpokeEvent`, local speech only for now, range-filtered, queued
  per robot. `poll_event` drains the queue; `wait_event` is a Lua shim over `poll_event` + `robot.wait`.

## File layout

```
Content.Server/_PS/Circuitry/
  Components/            assembly, part, CPU
  Lua/                   LuaProgram, LuaSandbox, options (Phase 0)
  Systems/               assembly, movement, power, Lua brain, hearing
  UI/                    server BUI systems
  CircuitLuaApi.cs       per-program API + handle map
Content.Shared/_PS/Circuitry/
  CircuitUi.cs           BUI keys/states/messages
Content.Client/_PS/Circuitry/
  UI/                    assembly panel, CPU editor
Resources/Prototypes/_PS/Circuitry/  chassis/parts prototypes
Resources/Locale/en-US/_PS/circuitry.ftl
Content.Tests/_PS/Circuitry/         Lua + assembly tests
Content.IntegrationTests/Tests/_PS/  circuitry integration tests
```

## Validation

```bash
dotnet build SpaceStation14.sln
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
dotnet run --project Content.YAMLLinter -c Debug --no-build
dotnet test Content.Tests/Content.Tests.csproj
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Circuitry"
dotnet run --project Content.Client -c Debug --no-build -- --headless
```

Manual test target: spawn chassis + CPU + cell + wheels + arm + speaker + sensor + beaker, install,
open CPU editor, run a script that speaks, moves, scans, inspects and opens a door with an ID card,
then remove the CPU and verify the script stops.

## Hazards / invariants

- MoonSharp is server-only. Never reference it from `Content.Shared` or `Content.Client`.
- Use `LoadString` + coroutine; never `DoString` for player code.
- Check `Coroutine.State`, not just `YieldResult` (see Phase 0 fact above).
- All player effects go through existing systems (hands, interaction events, gun/melee, solution
  transfer). Do not invent parallel damage/teleport paths.
- Opaque handles only; no `UserData`, no `EntityUid` in Lua.
- Cap script source, scan radius/results, chat/log rates, save/run rates.
- Do not run Lua for paused, dead, unpowered or CPU-less assemblies.
- Keep all new code/resources under `_PS`; mark core-file patches (`Directory.Packages.props`,
  `Content.Server.csproj`, `LEGAL.md`) with `Palmtree` comments.
