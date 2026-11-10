# Circuitry Lua API Reference

> Living reference for the `robot` API available to player-authored circuit assembly programs.
> Update this file whenever a Lua-visible function, argument, return shape, limit or hardware
> requirement changes.
>
> Status: Phases 1–3 plus radio, 2026-11-07. This document describes the API as implemented in
> `Content.Server/_PS/Circuitry/Systems/CircuitLuaApiSystem.cs` and the surrounding runtime.

## Contents

- [Program model](#program-model)
- [Return conventions](#return-conventions)
- [Usage by system](#usage-by-system)
- [Global helpers](#global-helpers)
- [Status and modules](#status-and-modules)
- [Movement](#movement)
- [Perception](#perception)
- [Events](#events)
- [Communication](#communication)
- [Manipulation and combat](#manipulation-and-combat)
- [Chemistry and botany](#chemistry-and-botany)
- [Analyzers](#analyzers)
- [Data shapes](#data-shapes)
- [Hardware requirements](#hardware-requirements)
- [Limits and budgets](#limits-and-budgets)
- [Gotchas](#gotchas)
- [Maintenance](#maintenance)

## Program model

A circuit assembly needs:

1. A **CPU** module containing the program.
2. A **power cell** with charge.
3. Any hardware modules required by the APIs the program calls (sensor, manipulator, radio, etc.).

Programs are Lua source loaded into a MoonSharp hard sandbox. The file must define a global
function named `main`; the runtime calls it when the program starts:

```lua
function main()
    while true do
        local mobs = robot.scan_mobs()
        if mobs and #mobs > 0 then
            robot.say("Contact.")
        end

        robot.wait(1)
    end
end
```

Top-level code runs once while the chunk is being loaded. Define helpers and constants at the top
level; put loops and ongoing behaviour inside `main`.

For testing, spawn the `CircuitAssemblyFilled` prototype (`programmable assembly (Filled)` in the
admin spawn menu). It comes with a CPU, microreactor cell, wheels, two manipulators, speaker,
sensor, radio with a common key, four beakers and all three analyzers already installed.

There is no `on_tick` callback and no wiring graph. A program runs until it finishes, errors, is
stopped by the player/UI, its CPU is removed, or it exceeds a runtime budget. Losing power or
becoming dead suspends the program; it resumes if power and a living state return.

## Return conventions

Most actions return one of:

```lua
true
false, "human-readable reason"
```

Read helpers usually return one of:

```lua
{ ... }                 -- a table or list
false, "reason"         -- a tuple; assign two locals to see the reason
nil, "reason"           -- single-entity lookup failure or no match
```

Use Lua's multiple-return assignment when you care about the failure reason:

```lua
local moved, reason = robot.move(1, 0)
if not moved then
    log("move failed:", reason)
end
```

Handles are opaque integer references to entities. Handle `0` is always the assembly itself.
Handles are only valid while the referenced entity exists and the runtime keeps it in its handle
table. Re-scan if an action reports an invalid handle.

### Entity handle policy

Entity-returning APIs follow two standard shapes:

- **Single-result lookups** — `find`, `nearest_mob`, `nearest_person`, `nearest_item`,
  `nearest_structure`, `nearest_door` — return a bare handle, or `nil, reason`.
- **Multi-result lookups** — `scan`, `scan_mobs`, `scan_items`, `scan_structures`,
  `get_tile_relative`, `entities_on_tile`, `held` — return tables. Each entity entry carries its
  handle in `.id`; the table is not replaced by a bare list.
- `inspect` is a metadata snapshot. Its `held` and `wearing` fields contain display names; call
  `robot.held()` when you need handles to the assembly's own held items.

Always pass the handle itself to an action:

```lua
local wrench = robot.find("wrench")
if wrench then
    robot.pickup(wrench)
end
```

## Usage by system

These compact examples assume the relevant modules are installed and powered. Put them inside
`main` or a helper function.

### Power and status

```lua
local self = robot.self()
log(self.name, self.power, self.max_power, self.powered)
```

### Power transfer

```lua
-- Charge a held or nearby battery, power cell or energy gun.
local gun = robot.find("energy submachine gun")
if gun then
    log("before", robot.battery(gun).charge)
    robot.charge(gun, 100)
end

-- Draw from the nearest APC, battery, SMES or other power cell.
if robot.charge_from(100) then
    log("charged from nearby power")
end
```

| Function | Requires | Returns |
|---|---|---|
| `robot.battery(handle)` | Target has a battery or a slotted power cell | `{charge, max, percent}` or `false, reason` |
| `robot.charge(handle, amount)` | Assembly power cell, target within transfer range and line of sight | `true` or `false, reason` |
| `robot.charge_from(amount [, radius])` | Assembly power cell, nearby APC/battery/power cell | `true` or `false, reason` |

- Amounts are in battery charge units (joules). `charge` transfers only as much as the target is
  missing and the assembly can supply.
- `charge` works on a battery, power cell, battery-powered gun/item, APC or SMES. It can target an
  item in the assembly's manipulator hand or a nearby item/entity.
- `charge_from` searches within `radius` (default `2`, maximum `4` tiles), nearest first, and draws
  from APCs, SMES units, batteries and battery-powered items. It stops when the requested amount is
  transferred or the assembly's cell is full.
- Both directions are rate-limited to one transfer per `0.5` seconds.
- `battery(handle)` is a read-only snapshot. `robot.power()` remains the assembly's own cell charge.

### Movement and pathfinding

```lua
local ok, reason = robot.walk_to(10, 4)
if not ok then
    log("could not walk there:", reason)
end
```

### Perception and inspection

```lua
local wrench = robot.find("wrench")
if wrench then
    local info = robot.inspect(wrench)
    log(info.name, info.kind)
end
```

### Events and hearing

```lua
local ev = robot.wait_event(2)
if ev and ev.type == "speech" then
    log("heard", ev.name, ev.message)
elseif ev and ev.type == "radio" then
    log("radio", ev.channel, ev.message)
end
```

### Speech and radio

```lua
robot.say("Ready.")

local channels = robot.radio_channels()
if channels then
    for _, channel in ipairs(channels) do
        log("channel", channel.id, channel.key)
    end
    robot.radio("Status nominal")
end
```

### Manipulation and combat

```lua
local crowbar = robot.find("crowbar")
if crowbar and robot.pickup(crowbar) then
    local door = robot.find("airlock")
    if door then
        robot.use(door)
    end
    robot.drop()
end
```

### Chemistry and botany

```lua
local tray = robot.find("hydroponics tray")
if tray then
    local plant = robot.analyze_plant(tray)
    if plant and plant.harvest_ready then
        robot.harvest(tray)
    end
    robot.water(tray, 25, 1)
end
```

### Analyzers

```lua
local person = robot.nearest_person(6)
if person then
    local med = robot.analyze_medical(person)
    if med then
        log(med.name, med.state, med.health)
    end
end
```

Atmospherics:

```lua
local pipe = robot.find("gas pipe")
if pipe then
    local gas = robot.analyze_gas(pipe)
    log(gas.name, gas.environment.pressure, gas.environment.temperature)

    for _, mix in ipairs(gas.mixes) do
        log("mix", mix.name, mix.pressure, mix.temperature)
        for _, component in ipairs(mix.gases) do
            log("gas", component.id, component.amount)
        end
    end
end
```

### Item and container status

```lua
local bucket = robot.find("bucket")
if bucket then
    local status = robot.item_status(bucket)
    local solution = status.solutions[1]
    log("bucket", solution.name, solution.volume, #solution.contents)
end
```

For an item already in a manipulator hand, use `robot.held()` to obtain its handle; `find()` only
sees uncontained items on the floor or a table.

### Sinks, beakers and syringes

```lua
local sink = robot.find("sink")
if sink then
    log("filled", tostring(robot.transfer_from(sink, 50, 1)))
end

local syringe = robot.find("syringe")
if syringe then
    robot.pickup(syringe)
    robot.transfer_to(syringe, 15, 1) -- installed beaker slot 1 -> syringe
    -- robot.transfer_from(syringe, 5, 1) -- syringe -> installed beaker slot 1
end
```

### Flashlight status

```lua
local light = robot.find("flashlight")
if light then
    log("light on", tostring(robot.item_status(light).activated))
end
```

### Doors and path blockers

```lua
local door = robot.nearest_door(4)
if door then
    local status = robot.door_status(door)
    log("door", status.state, tostring(status.bolted), tostring(status.access), tostring(status.can_open))
end
```

`walk_to` does not automatically open or unlock doors. Use `scan_doors` and `door_status` to
inspect a route before committing to it. `walk_to` returns `false, "no path"` for an unreachable
destination; `walk_status() == "no_path"` is the equivalent check when using `walk_to_async`.

### Cleanable decals

`robot.decals(radius)` returns cleanable decals within the sensor radius (default `0.75` tiles). The
`decal_id` is the tile decal identifier, not an entity handle.

```lua
local decals = robot.decals(1.5)
if decals then
    for _, decal in ipairs(decals) do
        log("decal", decal.decal, decal.decal_id, decal.x, decal.y)
    end
end
```

Only decals marked cleanable are returned. Cleaning itself is a normal tool/action interaction, not
a Lua API call; use the id with the appropriate mop or cleaning tool.

### Identity, jobs and access

```lua
local person = robot.nearest_person(4)
if person then
    if robot.has_access(person, "Pirate") then
        log("pirate", robot.inspect(person).name)
    elseif robot.has_access(person, "Security") then
        log("nfsd", robot.inspect(person).name)
    end

    if robot.has_job(person, "Deputy") then
        log("deputy on site")
    end

    local wanted = robot.wanted_status(person)
    if wanted and wanted.wanted then
        log("wanted", wanted.reason, wanted.initiator)
    end
end
```

`has_job` accepts a job prototype id (for example `Deputy`, `NFSD` jobs such as `Sheriff`, or
`NFPirate`) or the localized job title. `has_access` checks the ID card's access tags; `Security`
covers NFSD access, while pirates carry the `Pirate` tag. `wanted_status` reads the same sector
criminal record as the wanted-list cartridge and returns the reason recorded by security. All three
require a powered sensor, line of sight and consume one action-power charge. If the target has no
ID card, `has_job`/`has_access` return plain `false`; sensor/power/range failures return
`false, reason`.

Wanted status table:

```lua
{
    status = "Wanted",
    wanted = true,
    reason = "Trespass - Vault",       -- optional
    initiator = "Deputy Name",         -- optional
    history = {
        { crime = "Trespass", initiator = "Deputy Name" },
    },
}
```

### Puddles and floor spills

`robot.scan_puddles(radius)` requires a powered sensor **and** a chemical analyzer. It scans uncontained
floor puddles (default radius `1` tile) and returns their solution volume, temperature and contents.
It shares the normal sensor-query budget.

```lua
local puddles = robot.scan_puddles(2)
if puddles then
    for _, puddle in ipairs(puddles) do
        log("puddle", puddle.id, puddle.name, puddle.volume)
        for _, reagent in ipairs(puddle.contents) do
            log("reagent", reagent.id, reagent.amount)
        end
    end
end
```

Puddles are entities, not decals. Use `robot.scan_puddles()` for floor spills and `robot.decals()` for
cleanable tile stains. Beakers, cans and other reagent containers are found with `scan()`/`find()`
and read with `robot.analyze_chemicals(handle)`.

### Signals and remote signallers

Link a remote signaller's `Pressed` source to the assembly's `Trigger` sink with a network
configurator. Link the assembly's `Pressed` source to a device's sink if the assembly should send
signals to that device.

```lua
local sent = robot.send_signal(true)          -- Pressed source
local sentOn = robot.send_signal(true, "On")  -- On source

local ev = robot.wait_event(2)
if ev and ev.type == "signal" then
    log("signal", ev.port, tostring(ev.state), ev.name)
end

local signaller = robot.find("remote signaller")
if signaller and robot.pickup(signaller) then
    log("trigger", tostring(robot.trigger(signaller)))
end
```

### Find, walk to, and pick up

```lua
local wrench = robot.find("wrench")
if wrench then
    if robot.walk_to(wrench) then
        if robot.can_reach(wrench) then
            robot.pickup(wrench)
        end
    end
end
```

### Fetch an item and return

```lua
function fetchAndReturn(itemName)
    local homeX, homeY = robot.position()
    local item = robot.find(itemName)
    if not item then
        return false, "item not found in sensor range"
    end

    local x, y = robot.position_of(item)
    if not x then
        return false, "item is on another grid"
    end

    if not robot.walk_to(x, y, 0.9) then
        return false, "no path to item"
    end

    if not robot.can_reach(item) then
        return false, "item is out of pickup range"
    end

    if not robot.pickup(item) then
        return false, "pickup failed"
    end

    if not robot.walk_to(homeX, homeY, 0.5) then
        return false, "no path home"
    end

    robot.drop()
    return true
end
```

`find()` only sees uncontained items within the powered sensor range. For a larger room, move
between waypoints and scan again, or target a container and open it first.

### Following a person

```lua
local target = robot.find("Alice")
if target then
    while true do
        local x, y = robot.position_of(target)
        if not x then
            break
        end

        if robot.distance(target) > 2 then
            robot.walk_to(x, y, 1)
        end

        robot.wait(0.5)
    end
end
```

The handle stays valid while the target exists, so the robot can keep following even when the
person leaves sensor range; reacquire them if the entity is removed.

### Treating the most injured person

```lua
local best
local lowest = 1.0

local mobs = robot.scan_mobs(6)
if mobs then
    for _, entry in ipairs(mobs) do
        local med = robot.analyze_medical(entry.id)
        if med and not med.dead and med.max_health > 0 then
            local ratio = med.health / med.max_health
            if ratio < lowest then
                lowest = ratio
                best = entry.id
            end
        end
    end
end

if best then
    local x, y = robot.position_of(best)
    robot.walk_to(x, y, 1.2)

    local syringe = robot.find("syringe")
    if syringe then
        robot.pickup(syringe)
        robot.transfer_to(syringe, 15, 1) -- beaker slot 1
        robot.inject(best)
        robot.wait(2)
    end
end
```

### Mining and space

Mining is feasible in principle with a thruster, a mining tool in a manipulator hand, and an
asteroid grid. The current API is still missing ore scanning, mining-tool-specific helpers and
cargo/storage management, so a mining bot should be treated as an advanced project:

```lua
-- Outline only; requires a thruster and a mining tool in hand.
local rock = robot.find("rock")
if rock then
    local x, y = robot.position_of(rock)
    robot.walk_to(x, y, 1)

    while robot.can_reach(rock) do
        robot.attack(rock)
        robot.wait(1)
        local nextRock = robot.find("rock")
        if not nextRock then
            break
        end
        rock = nextRock
    end
end
```

Space locomotion exists through the thruster module: movement is vector-based and supports flight,
but there is no facing/orientation control, docking autopilot or terrain awareness. Asteroid-grid
mining is plausible; general space navigation is not a good first target. Autonomous botany,
chemistry and medical loops are better supported today.

### Complete loop

```lua
function main()
    while true do
        local items = robot.scan_items(4)
        if items then
            for _, item in ipairs(items) do
                log("item", item.name, item.distance)
            end
        end

        local person = robot.nearest_person(6)
        if person then
            local info = robot.inspect(person)
            if info then
                robot.radio("Contact: " .. info.name)
            end
        end

        robot.wait(2)
    end
end
```

## Global helpers

These are available globally and on `robot`:

| Helper | Description |
|---|---|
| `log(...)` / `print(...)` | Writes tab-separated values to the CPU console. The last 100 lines are retained. |
| `robot.log(...)` | Same as `log`. |
| `robot.wait(seconds)` | Suspends the program for `seconds`. Maximum single wait is 1 hour; negative/NaN values become 0. This is the only safe way to yield; **do not call it inside `pcall`**. |

Example:

```lua
log("battery", robot.power())
robot.wait(0.5)
```

## GLua-style helpers

The sandbox installs a small, safe subset of Garry's Mod table/string extensions and type
shorthands. These are not entity APIs; they run entirely in Lua and do not require hardware or
power.

Global type shorthands:

| Helper | Behavior |
|---|---|
| `istable(value)` | `true` when the value is a Lua table. |
| `isstring(value)` | `true` when the value is a string. |
| `isnumber(value)` | `true` when the value is a number. |
| `isbool(value)` | `true` when the value is a boolean. |
| `isfunction(value)` | `true` when the value is a function/callback. |

| Helper | Behavior |
|---|---|
| `table.HasValue(tbl, value)` | `true` if any value in the table equals `value` (O(n)). |
| `table.Contains(tbl, value)` | Alias of `table.HasValue`, for scripts coming from frameworks that use that name. |
| `table.IsEmpty(tbl)` | `true` when the table has no keys. |
| `table.Count(tbl)` | Number of key/value pairs, including non-sequential keys. |
| `table.GetKeys(tbl)` | New array containing every key. Order is unspecified. |
| `table.KeyFromValue(tbl, value)` | First key whose value equals `value`, or `nil`. |
| `table.Random(tbl)` | Random value and its key, or `nil` for an empty table. |
| `table.Add(target, source)` | Appends `source`'s sequential values to `target` and returns `target`. |
| `table.RemoveByValue(tbl, value)` | Removes the first sequential match and returns its index, or `false`. |
| `string.Trim(input [, pattern])` | Removes leading/trailing matches; default pattern is `%s`. |
| `string.StartsWith(input, prefix)` | Boolean prefix check. |
| `string.StartWith(input, prefix)` | Deprecated GLua alias of `string.StartsWith`. |
| `string.EndsWith(input, suffix)` | Boolean suffix check. |
| `string.Split(input, separator)` | Splits on a plain separator and returns an array of strings. |

```lua
if istable(robot.scan_mobs(4)) and isstring("NFSD") then
    log("type checks ok")
end

local names = table.GetKeys(robot.scan_mobs(4))
if table.HasValue(names, "Security") or table.Contains(names, "Pirate") then
    log("hostile or security presence")
end

local wanted = string.Split("alpha,beta,gamma", ",")
log("parts", tostring(#wanted), wanted[1], wanted[3])

if string.StartsWith("NFSD", "NF") and string.EndsWith("NFSD", "SD") then
    log("trimmed", string.Trim("  NFSD  "))
end
```

These helpers only cover common table/string convenience functions. They do not add GLua's
filesystem, networking, entity, hook, or userdata APIs.

## Status and modules

| Function | Returns | Notes |
|---|---|---|
| `robot.self()` | table or `nil` | `handle`, `name`, `x`, `y`, `power`, `max_power`, `powered`, `damage`, `health`, `dead`, `modules`. Does not include `max_health`. |
| `robot.position()` | `x, y` | Assembly position in its current map/grid space. |
| `robot.power()` | `current, max` | Power cell charge. Returns `0, 0` if there is no cell. |
| `robot.battery(handle)` | `table` | `{charge, max, percent}` for a battery, power cell, APC, SMES or battery-powered item. |
| `robot.charge(handle, amount)` | `true` | `false, reason` |
| `robot.charge_from(amount [, radius])` | `true` | `false, reason` |
| `robot.health()` | table | `damage`, `health`, `max_health`, `dead`. |
| `robot.item_status(handle)` | table | Name/kind, `activated` for toggleable items, `injector` mode/transfer amount, and every solution container. |
| `robot.solution(handle [, name])` | solution table or `false, reason` | Reads a bucket, beaker, syringe or other container. Auto-selects injector/refillable/drainable or a single solution. |
| `robot.has_module(name)` | boolean | Accepts the module aliases below. Returns `false, reason` if the argument is missing or not a string. |

`robot.self().modules` and `robot.has_module(name)` understand these names:

| Capability | Accepted names |
|---|---|
| CPU | `cpu` |
| Power cell | `power`, `cell` |
| Locomotion | `locomotion`, `legs`, `tracks`, `wheels`, `thruster` |
| Flight | `flight`, `flying` |
| Manipulator | `manipulator`, `hand`, `arm` |
| Speaker | `voice`, `speaker` |
| Sensor | `sensor`, `camera` |
| Radio | `radio`, `transceiver` |
| Reagent storage | `reagent`, `beaker` |
| Botanical analyzer | `botanical_analyzer`, `botany` |
| Chemical analyzer | `chemical_analyzer`, `chem` |
| Medical analyzer | `medical_analyzer`, `medical` |
| Gas analyzer | `gas_analyzer`, `gas`, `atmos` |

## Movement

| Function | Requires | Returns |
|---|---|---|
| `robot.move(dx, dy)` | Locomotion, power | `true` or `false, reason` |
| `robot.walk_to(x, y [, range [, smooth]])` / `robot.walk_to(handle)` | Locomotion, power | `true` on arrival, or `false, reason`; yields the script while walking |
| `robot.walk_to_async(x, y [, range [, smooth]])` / `robot.walk_to_async(handle)` | Locomotion, power | `true` once the path starts, or `false, reason` |
| `robot.stop()` | — | `true` |
| `robot.walk_status()` | — | `"idle"`, `"moving"`, `"arrived"` or `"no_path"` |

- `move` takes a direction/vector; it is normalized. `(0, 0)` stops movement.
- `walk_to` uses pathfinding. The target is either local grid/map `x, y` coordinates or a single
  entity handle. A handle resolves to the entity's current position; the entity must be on the same
  grid. Use the handle form to follow a target directly:
  `robot.walk_to(lily)`. The handle form is one argument; for a custom range, resolve
  `position_of(handle)` and pass the coordinates instead.
- `range` defaults to `0.5`, and values below `0.5` are raised to `0.5` to avoid overshoot loops
  around small targets. With `smooth = true` (the default), the arrival tolerance scales with
  current walk speed up to `1.25` tiles and the steering system brakes to roughly 15% of walk speed
  near the target. Pass `smooth = false` for the old fixed-tolerance behavior.
- `walk_to` is **synchronous from the script's point of view**: it starts the path, yields through
  `robot.wait`, and returns `true` when the robot arrives, `false, "no path"` if no route exists,
  or `false, "walk timed out"` after 60 seconds. It does not block the server tick. You do not need
  a `walk_status` loop around it.
- `walk_to_async` is the fire-and-forget version: it returns as soon as the path starts. Use
  `walk_status()` to follow it:

```lua
robot.walk_to_async(10, 4)
while robot.walk_status() == "moving" do
    robot.wait(0.2)
end
```
- A successful path start is rate-limited to one every 0.25 s. Calls while already pathing to a
  target within 0.75 tiles of the new one are treated as a no-op and keep the existing path. This
  prevents a tight follow loop from cancelling and restarting pathfinding every tick.
- Direct movement cancels an in-progress path. `move` is intentionally continuous; it has no
  destination or completion, so `walk_to` is the synchronous movement API.

## Perception

All perception helpers require a **sensor module** unless noted. Most also require power, consume
action power, and respect line of sight and sensor range. The standard sensor module covers **16
tiles**; on normal grids `MapGridComponent.TileSize` is 1, so one tile equals one world unit.

| Function | Returns | Notes |
|---|---|---|
| `robot.scan([radius])` | list or `false, reason` | All entities in range: `{id, name, kind, distance, x, y}`. Radius defaults to the sensor range and is clamped to it. Returns at most the sensor module's scan result cap (200 on the standard sensor), nearest first. Uses the shared sensor-query budget. |
| `robot.scan_mobs([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "mob"`. |
| `robot.scan_items([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "item"`. Only loose, unanchored items; installed pipes, vents, scrubbers and machines are excluded. |
| `robot.scan_structures([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "structure"`. Returns installed/anchorable machinery, pipes, vents, scrubbers and constructible machines; walls/windows remain `object`. |
| `robot.scan_doors([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "door"`. |
| `robot.find(name [, radius])` | handle or `nil, reason` | Case-insensitive substring match on entity name. Returns the nearest match as a bare handle. Returns `nil, reason` when there is no match, no sensor, no power, or the sensor query budget is empty. Only scans **uncontained** entities. Searches every entity in range, not just the multi-scan result cap, so nearby clutter cannot hide a farther named target. |
| `robot.inspect(handle)` | table or `false, reason` | Identity, health, ID (name, job, job id, departments, access tags), worn equipment, held items and component flags. Requires sensor range and line of sight. |
| `robot.has_job(handle, job)` | boolean, or `false, reason` | Case-insensitive match against the ID card's job prototype id/localized title, with a sector-record fallback. Requires sensor range and line of sight. |
| `robot.has_access(handle, access)` | boolean, or `false, reason` | Checks the ID card's access tags/groups. Use tags such as `Security` for NFSD or `Pirate` for pirates. Requires sensor range and line of sight. |
| `robot.wanted_status(handle)` | table or `false, reason` | Reads the sector criminal record: `status`, `wanted`, `reason`, `initiator` and `history`. Requires sensor range and line of sight. |
| `robot.can_see(handle)` | boolean | Sensor module and line of sight within sensor range. Does not charge power. |
| `robot.can_hear(handle)` | boolean | Sensor module; same map and within sensor range. No line-of-sight check. Does not charge power. |
| `robot.nearest_mob([radius])` | handle or `nil, reason` | Powered sensor required. |
| `robot.nearest_person([radius])` | handle or `nil, reason` | Powered sensor required; only entities with humanoid appearance. |
| `robot.nearest_item([radius])` | handle or `nil, reason` | Powered sensor required. Loose items only. |
| `robot.nearest_structure([radius])` | handle or `nil, reason` | Powered sensor required. Installed/anchorable machinery and pipes. |
| `robot.nearest_door([radius])` | handle or `nil, reason` | Powered sensor required. |
| `robot.distance(handle)` | number | Euclidean distance; returns `-1` if the target is on another map. No sensor or power requirement. |
| `robot.position_of(handle)` | `x, y` or `false, reason` | Local grid position for `walk_to`. Fails if the target is on a different grid. |
| `robot.can_reach(handle)` | boolean | Line of sight within hand range. No manipulator requirement. |
| `robot.get_tile_relative([dx, dy])` / `robot.tile(...)` | table or `false, reason` | Powered sensor required. Returns `{x, y, entities}` where `entities` is a list of entity entries. |
| `robot.entities_on_tile([dx, dy])` | list or `false, reason` | Powered sensor required. Returns the entity list directly. |
| `robot.decals([radius])` | list or `false, reason` | Powered sensor required. Returns cleanable decals only: `{decal_id, decal, x, y, cleanable}`. |
| `robot.scan_puddles([radius])` | list or `false, reason` | Chemical analyzer and powered sensor required. Returns floor puddles with `id`, name, position, volume, temperature and contents. Uses the shared sensor-query budget. |
| `robot.door_status(handle)` | table or `false, reason` | Door state, bolted/powered/firelock/access flags and `can_open`. |
| `robot.can_open(handle)` | boolean | Shortcut for `door_status(handle).can_open`. |

`dx`/`dy` are offsets from the assembly's current position. Tile lookups are limited by sensor
range and line of sight for each entity.

Multi-result scans (`scan`, `scan_mobs`, `scan_items`, `scan_structures`, `scan_doors`) are capped
by the sensor module's maximum scan result count (200 on the standard sensor) and return the
nearest results first. The scanner sorts candidates first and only raycasts in nearest-first order
until the cap is filled. Single-result lookups (`find`, `nearest_*`) search the whole sensor range
and are not hidden by closer entities. All of `scan*`, `find`, `nearest_*` and `scan_puddles` draw
from the same four-per-second sensor-query budget.

Example:

```lua
local wrench = robot.find("wrench")
if wrench then
    local info = robot.inspect(wrench)
    if info then
        log("found", info.name, info.kind)
    end
end
```

## Events

| Function | Returns | Notes |
|---|---|---|
| `robot.poll_event()` | event table or `nil` | Removes and returns the oldest queued event, or `nil` if the queue is empty. |
| `robot.wait_event([timeout])` | event table or `nil` | Polls in 50 ms slices until an event arrives or the timeout expires. Defaults to 1 second; capped at roughly 600 seconds by the shim. |

The event queue holds at most 16 events. If it overflows, the oldest event is discarded.

### Event types

Speech heard by the sensor module (not radio):

```lua
{
    type = "speech",
    speaker = <handle>,
    name = "Speaker Name",
    id_name = "ID Name",   -- optional
    job = "Job Title",     -- optional
    message = "spoken text",
}
```

Radio traffic received by the radio module:

```lua
{
    type = "radio",
    speaker = <handle>,
    name = "Speaker Name",
    id_name = "ID Name",   -- optional
    job = "Job Title",     -- optional
    message = "transmitted text",
    channel = "Security",  -- channel prototype id
}
```

Emotes performed within sensor range:

```lua
{
    type = "emote",
    speaker = <handle>,
    name = "Speaker Name",
    message = "scream",   -- localized emote name
    emote = "Scream",     -- emote prototype id
}
```

Damage taken by the assembly:

```lua
{
    type = "damage",
    speaker = <origin handle>, -- optional attacker/origin
    name = "Attacker Name",    -- when the origin exists
    message = "damage",
    damage = { Blunt = 5.0 },  -- damage type -> amount
    damage_total = 5.0,
}
```

Melee attack against the assembly:

```lua
{
    type = "attacked",
    speaker = <attacker handle>,
    name = "Attacker Name",
    message = "crowbar",      -- weapon name
    item = <weapon handle>,
    item_name = "crowbar",
}
```

Projectile hit on the assembly:

```lua
{
    type = "projectile",
    speaker = <shooter handle>, -- optional
    name = "Shooter Name",      -- when the shooter exists
    message = "projectile",
    damage = { Piercing = 12.0 },
    damage_total = 12.0,
}
```

Device-link signal received by the radio module:

```lua
{
    type = "signal",
    speaker = <trigger handle>, -- remote signaller or linked source
    name = "remote signaller",
    message = "Trigger",
    port = "Trigger",
    state = true,               -- false for a Low signal; Momentary counts as true
}
```

Example:

```lua
function main()
    while true do
        local ev = robot.wait_event(1)
        if ev then
            if ev.type == "speech" then
                log("heard", ev.name, ev.message)
            elseif ev.type == "radio" then
                log("radio", ev.channel, ev.name, ev.message)
            elseif ev.type == "emote" then
                log("emote", ev.name, ev.emote)
            elseif ev.type == "damage" then
                log("damage", ev.damage_total)
            elseif ev.type == "signal" then
                log("signal", ev.port, ev.state)
            end
        end
    end
end
```

Speech and emote events require a sensor module, power, and the speaker within sensor range. Radio
and signal events require a powered radio module; radio channels additionally require encryption
keys. The assembly does not queue its own radio transmissions.

## Communication

| Function | Requires | Returns |
|---|---|---|
| `robot.say(text)` | Speaker module, power | `true` or `false, reason` |
| `robot.radio(text [, channel])` | Radio module, power, matching key | `true` or `false, reason` |
| `robot.radio_channels()` | Radio module | list or `false, reason` |
| `robot.trigger(handle)` | Manipulator, powered, active held item | `true` after any do-after, or `false, reason` |
| `robot.send_signal([state [, port]])` | Radio module, power, matching source port | `true` or `false, reason` |

### `say`

- Text is trimmed; length must be 1–200 characters.
- Rate-limited to one message per 0.5 seconds.
- Consumes action power.

### `radio`

- Text is trimmed; length must be 1–200 characters.
- The channel must be granted by an encryption key in the radio module.
- Accepted channel forms:
  - Prototype id: `"Security"`, `"Common"`.
  - Keycode: `";"` for common, `":c"` for command, etc.
  - Localized channel name.
- With no channel argument, the key holder's default channel is used. If no default is marked,
  the first channel in sorted order is used.
- Rate-limited to one transmission per second.
- Consumes action power.

### `radio_channels`

Returns the channels currently granted by the module's installed encryption keys. Returns an empty
list if the module has no keys:

```lua
{
    {
        id = "Common",
        name = "Common",      -- localized channel name
        key = ";",            -- keycode; empty string if none
        frequency = 1459,
        default = true,
    },
    ...
}
```

Keys are inserted by using an encryption key on the chassis. The radio module has two key slots.
To remove keys, eject the module and use a screwdriver on it. The module is telecom-exempt, so
common-channel traffic does not require a powered telecom server.

Example:

```lua
function main()
    local channels = robot.radio_channels()
    if channels then
        for _, channel in ipairs(channels) do
            log("channel", channel.id, channel.key)
        end
    end

    robot.radio("Security sweep complete", "Security")
end
```

### `trigger` and `send_signal`

Installing a radio module gives the assembly a device-link `Trigger`/`On`/`Off` sink and a
`Pressed`/`On`/`Off` source. Use a network configurator to link a remote signaller's `Pressed`
source to the assembly's `Trigger` sink, and the assembly's source ports to any device sink.

- `robot.trigger(handle)` raises use-in-hand on the active held item. Use it for a remote signaller,
  flashlight, or other use-in-hand device.
- `robot.send_signal([state [, port]])` sends `High`/`Low` from the selected source port, default
  `"Pressed"`. It is rate-limited to 0.5 seconds; an unknown port returns `false, reason`.
- Incoming signals appear as `type = "signal"` events with `port` and `state`.

## Manipulation and combat

| Function | Requires | Returns |
|---|---|---|
| `robot.pickup(handle)` | Manipulator, item target, power | `true` or `false, reason` |
| `robot.drop()` | Manipulator, power | `true` or `false, reason` |
| `robot.held()` | — | list of `{id, name}` |
| `robot.use(handle)` | Power, interaction permission, hand range | `true` after any do-after, or `false, reason` |
| `robot.use_item(handle)` | Manipulator, active held item, power | `true` after any do-after, or `false, reason` |
| `robot.interact(handle)` / `robot.harvest(handle)` | Manipulator, power | `true` after any do-after, or `false, reason` |
| `robot.plant(handle)` | Same as `use_item` | `true` after any do-after, or `false, reason` |
| `robot.attack(handle)` | Manipulator, melee weapon, power | `true` or `false, reason` |
| `robot.shoot(handle)` | Manipulator, gun in hand, line of sight | `true` or `false, reason` |
| `robot.inject(handle)` | Manipulator, syringe/injector in active hand, power | `true` after the injection do-after, or `false, reason` |

`use`, `use_item`, `interact`/`harvest`, `plant`, `trigger` and `inject` are synchronous by default:
if the action starts a do-after, they yield until it completes (up to 30 seconds), return
`false, "action cancelled"` if it is interrupted, and `false, "action timed out"` if it never
finishes. Their `_async` variants (`use_async`, `use_item_async`, `interact_async`,
`harvest_async`, `plant_async`, `trigger_async`, `inject_async`) return immediately after the event
is handled. `pickup`, `drop`, `attack`, `shoot`, transfers, dispensing, ChemMaster actions and
`send_signal` are already instantaneous and have no async variants. `action_busy()`,
`action_marker()` and `action_status(marker)` are the low-level tracking helpers used by the
synchronous wrappers; normal scripts should use the high-level functions.

- `pickup` requires the target to have an `ItemComponent`.
- `drop` drops the assembly's active held item.
- `use` raises an `ActivateInWorldEvent` (buttons, doors, machines).
- `use_item` raises an `InteractUsingEvent` with the active hand item (seeds, tools, bottles).
- `interact` raises an `InteractHandEvent` (for example, harvesting a hydroponics tray).
- `plant` is an alias for `use_item`.
- `harvest` is an alias for `interact`.
- `attack` uses the active hand's melee weapon.
- `shoot` uses the active hand's gun. The target check uses at least 20 metres of line of sight, or
  the sensor range if larger; the target does **not** need to be within manipulator hand range.

## Chemistry and botany

Circuit assemblies have four fixed beaker slots, addressed as 1–4. Slot 1 is the default for APIs
that take an optional `slot`.

| Function | Requires | Returns |
|---|---|---|
| `robot.beakers()` | — | list of solution tables; empty if no beakers |
| `robot.reagents([slot])` | A beaker in that slot | solution table or `false, reason` |
| `robot.transfer_to(target, amount [, slot])` | Power, hand range, refillable target, beaker | `true` or `false, reason` |
| `robot.transfer_from(target, amount [, slot])` | Power, hand range, drainable target, beaker | `true` or `false, reason` |
| `robot.water(target, amount [, slot])` | Same as `transfer_to` | `true` or `false, reason` |
| `robot.transfer(source, target, amount)` | Manipulator, power, both containers in reach | `true` or `false, reason` |
| `robot.dispense(dispenser [, selector, amount, slot])` | Manipulator, reagent dispenser, beaker | `true` or `false, reason` |

- `transfer_to` moves reagents **from the beaker into the target**.
- `transfer_from` moves reagents **from the target into the beaker**.
- `water` is an alias for `transfer_to`.
- Amounts are capped at 1000 units.
- `transfer` moves reagents between any two containers, such as a carried bucket and a syringe.
- `transfer_to`/`transfer_from` also understand syringes and droppers by using their injector solution.
- `dispense` reads a reagent dispenser's storage and transfers into a beaker:
  - `selector` can be a string matched against the stored container's name or reagent prototype,
    or a 1-based numeric index. If omitted, the first stored container is used.
  - `amount` defaults to 10.
  - `slot` defaults to 1.
- `beakers()` and `reagents()` include `slot`, `volume`, `max_volume`, `temperature` and
  `contents` (`{id, amount}` entries).

```lua
function main()
    local beaker = robot.reagents(1)
    if beaker and beaker.volume < 50 then
        local dispenser = robot.find("chemical dispenser")
        if dispenser then
            robot.dispense(dispenser, "water", 25, 1)
        end
    end
end
```

### ChemMaster

The ChemMaster API drives the real machine UI messages, so buffer limits, pill dosage limits and
output-container rules stay in `ChemMasterSystem`.

| Function | Requires | Returns |
|---|---|---|
| `robot.chem_master_status(handle)` | Manipulator, power, hand range | table or `false, reason` |
| `robot.chem_master_set_mode(handle, mode)` | Manipulator, power, hand range | `true` or `false, reason` |
| `robot.chem_master_set_pill_type(handle, type)` | Manipulator, power, hand range; `0..19` | `true` or `false, reason` |
| `robot.chem_master_transfer(handle, reagent, amount, from_buffer)` | Manipulator, power, hand range | `true` or `false, reason` |
| `robot.chem_master_create_pills(handle, dosage, number [, label])` | Manipulator, power, pill output container, buffer | `true` or `false, reason` |
| `robot.chem_master_bottle(handle, dosage [, label])` | Manipulator, power, bottle output container, buffer | `true` or `false, reason` |

- `mode` is `"transfer"` or `"discard"`.
- `amount` for `chem_master_transfer` must be one of `1, 5, 10, 15, 20, 25, 30, 50, 100` or
  `"all"`. `from_buffer` is `true` to move buffer -> input beaker, `false` for input -> buffer.
- `chem_master_create_pills` respects the machine's `pill_dosage_limit` and the output container's
  storage space. `chem_master_bottle` fills the output bottle's `drink` solution.
- Status returns the current mode, sorting, pill type/limit, `buffer`, `input`, `output`,
  `output_name` and `output_items` (pill count).

```lua
{
    name = "ChemMaster 4000",
    mode = "transfer",              -- "transfer" or "discard"
    sorting = "none",
    pill_type = 0,
    pill_dosage_limit = 20,
    buffer = { volume = 40.0, max_volume = 1000.0, temperature = 293.15, contents = {...} },
    input = { name = "beaker", volume = 0.0, ... },  -- optional
    output = { name = "bottle", volume = 10.0, ... }, -- optional
    output_name = "pill canister",                    -- optional
    output_items = 2,                                 -- optional, pill count
}
```

```lua
local master = robot.find("chem", 4)
if master then
    local status = robot.chem_master_status(master)
    log("buffer", status.buffer.volume)

    robot.chem_master_transfer(master, "Cryoxadone", 50, false)
    robot.wait(0.6)
    robot.chem_master_create_pills(master, 5, 2, "cryo")
end
```

## Analyzers

Analyzer APIs require their matching analyzer module, a sensor module, power, and sensor
range/line of sight. Each call consumes action power.

| Function | Module | Returns |
|---|---|---|
| `robot.analyze_plant(handle)` | Botanical analyzer | table or `false, reason` |
| `robot.analyze_chemicals(handle)` | Chemical analyzer | table or `false, reason` |
| `robot.analyze_medical(handle)` | Medical analyzer | table or `false, reason` |
| `robot.analyze_gas(handle)` | Gas analyzer | table or `false, reason` |

`analyze_plant` accepts a hydroponics tray (`PlantHolder`) or a seed packet (`SeedComponent`).
`analyze_chemicals` enumerates every solution container on the target.
`analyze_medical` accepts a damageable or mob entity.
`analyze_gas` accepts pipes, vents, scrubbers, filters, mixers, canisters, gas tanks and similar
atmospherics devices. It returns the device's gas mixes plus the local environmental mixture.

## Data shapes

### Scan entry

```lua
{
    id = <handle>,
    name = "Entity Name",
    kind = "mob" | "door" | "item" | "structure" | "reagent" | "object",
    distance = 2.4,
    x = 10.0,
    y = 5.0,
}
```

### Tile entry

`get_tile_relative` returns:

```lua
{
    x = 10.0,
    y = 6.0,
    entities = {
        { id = <handle>, name = "Entity Name", kind = "item", x = 10.0, y = 6.0 },
    },
}
```

### Inspect result

```lua
{
    name = "Entity Name",
    kind = "mob",
    species = "Human",          -- only for humanoid appearance entities
    damage = 0.0,
    health = 200.0,
    max_health = 200.0,
    dead = false,
    id = {                        -- optional
        name = "ID Name",
        job = "Job Title",        -- localized title
        job_id = "Deputy",        -- job prototype id, if known
        departments = { "Security" },
        access = { "Security", "Brig", "Maintenance" },
    },
    wearing = {                  -- inventory slot -> item name
        head = "Hat",
        outerClothing = "Coat",
    },
    held = { "Item One", "Item Two" }, -- names, not handles
    components = {
        mob = true,
        item = false,
        door = false,
        reagent = false,
    },
}
```

Use `robot.held()` when you need handles to the assembly's own held items. `inspect().held`
contains only names for the inspected entity.

### Solution table

```lua
{
    slot = 1,
    volume = 25.0,
    max_volume = 50.0,
    temperature = 293.15,
    contents = {
        { id = "Water", amount = 25.0 },
    },
}
```

`analyze_chemicals` adds a `name` field to each solution table and returns:

```lua
{
    name = "Beaker",
    solutions = {
        { name = "beaker", slot = 0, volume = 25.0, ... },
    },
}
```

### Item status

```lua
{
    name = "Bucket",
    kind = "item",
    activated = false,          -- present for toggleable items such as flashlights
    injector = {                -- present for syringes/droppers
        mode = "Draw",          -- Draw or Inject
        transfer_amount = 15.0,
    },
    solutions = {
        {
            name = "drink",
            volume = 50.0,
            max_volume = 250.0,
            temperature = 293.15,
            contents = {
                { id = "Water", amount = 50.0 },
            },
        },
    },
}
```

`robot.solution(handle [, name])` returns one solution table with `name` included. If a container has
multiple solutions and no injector/refillable/drainable hint, pass the solution name explicitly.

### Door status

```lua
{
    state = "Closed", -- Closed, Closing, Open, Opening, Welded, Denying, Emagging
    bolted = false,
    powered = true,
    fire_locked = false,
    access = false,   -- whether the assembly's carried access would pass the reader
    can_open = false,
}
```

`inspect(door)` also includes this table under `.door`.

### Cleanable decal entry

```lua
{
    decal_id = 12,     -- tile decal identifier, not an entity handle
    decal = "Dirt",    -- decal prototype id
    x = 10.0,
    y = 5.0,
    cleanable = true,
}
```

### Puddle entry

```lua
{
    id = 17,             -- entity handle, usable with analyze_chemicals/walk_to
    name = "puddle",
    x = 10.0,
    y = 5.0,
    distance = 1.2,
    solution = "puddle",
    volume = 10.0,
    max_volume = 1000.0,
    temperature = 293.15,
    contents = {
        { id = "Vomit", amount = 10.0 },
    },
}
```

### Plant and seed analysis

A plant tray result:

```lua
{
    name = "Hydroponics Tray",
    is_seed = false,
    dead = false,
    health = 100.0,
    water = 80.0,
    nutrition = 70.0,
    toxins = 0.0,
    pests = 0.0,
    weeds = 0.0,
    age = 12.0,
    harvest_ready = true,
    mutation_level = 0.0,
    improper_heat = false,
    improper_pressure = false,
    improper_light = false,
    seed = { ... },
}
```

A seed packet result:

```lua
{
    name = "Wheat Seeds",
    is_seed = true,
    seed = { ... },
}
```

Seed table fields:

`name`, `display_name`, `yield`, `potency`, `endurance`, `lifespan`, `maturation`, `production`,
`growth_stages`, `nutrient_consumption`, `water_consumption`, `ideal_heat`, `heat_tolerance`,
`ideal_light`, `light_tolerance`, `toxins_tolerance`, `low_pressure_tolerance`,
`high_pressure_tolerance`, `pest_tolerance`, `weed_tolerance`, `weed_high_level_threshold`,
`viable`, `seedless`, `chemicals`, `exude_gases`, `consume_gases`, `mutation_prototypes`.

### Medical analysis

```lua
{
    name = "Patient",
    species = "Human",       -- if available
    state = "Alive" | "Critical" | "Dead",
    damage = 12.0,
    health = 188.0,
    max_health = 200.0,
    dead = false,
    damage_types = {
        Blunt = 12.0,
    },
    damage_groups = {
        Brute = 12.0,
    },
}
```

`damage_types` and `damage_groups` are present when the target has a `DamageableComponent`.

### Gas analysis

```lua
{
    name = "gas pipe (Straight)",
    device_flipped = false,
    environment = {
        name = "environment",
        volume = 2500.0,
        pressure = 101.3,
        temperature = 293.15,
        gases = {
            { id = "Nitrogen", name = "nitrogen", amount = 78.0, color = "#A52A2A" },
            { id = "Oxygen", name = "oxygen", amount = 21.0, color = "#FFFFFF" },
        },
    },
    mixes = {
        {
            name = "pipe",
            volume = 100.0,
            pressure = 250.0,
            temperature = 310.0,
            gases = {
                { id = "Plasma", name = "plasma", amount = 12.0, color = "#FF0000" },
            },
        },
    },
}
```

`environment` is the atmosphere where the target sits. `mixes` contains the target device's own
mixes, such as pipe nodes or a canister's tank. Gas names are localized display names; `id` is the
stable gas prototype id.

## Hardware requirements

| Capability | Module | What it enables |
|---|---|---|
| CPU | `CircuitCpuBasic` / `CircuitCpuAdvanced` | Program execution and storage. |
| Power | Power cell | All powered actions and movement. |
| Locomotion | Wheels, tracks, thruster | `move`, `walk_to`; thruster also enables flight. |
| Manipulator | `CircuitManipulator` | Hands, pickup/drop, item use, melee, guns, dispensing, harvesting. |
| Speaker | `CircuitSpeaker` | `say`. |
| Sensor | `CircuitSensor` | Scan/find/inspect, hearing, nearest helpers, tile helpers, analyzer range. |
| Radio | `CircuitRadio` | `radio`, `radio_channels`, radio events, device-link `Trigger`/`On`/`Off` sinks and `Pressed`/`On`/`Off` sources for remote signallers. Two encryption-key slots. |
| Analyzer | Botanical / chemical / medical / gas module | Matching `analyze_*` API. Requires a sensor for range/line of sight. |
| Reagent | Glass beaker in `beaker1..4` | `beakers`, `reagents`, transfers, dispensing. |

Power draws are deliberately fractional. A fully loaded assembly (CPU, tracks, two manipulators,
speaker, sensor, radio, three analyzers) idles under 1 charge/second, which is far below a
microreactor cell's 12 charge/second self-recharge. Movement costs only 0.02–0.06 charge per metre,
and actions cost at most about 0.1 charge each. Do not raise module draws back to whole units
without checking the microreactor budget.

Assemblies can be locked by their owner. While locked, only the owner or an admin can insert/eject
modules, rename the assembly, run/stop programs, save programs, or change encryption keys. The
assembly panel is view-only for everyone else. To transfer ownership, the owner unlocks and the new
owner locks the assembly; admins can use `circuitowner <assembly uid> <username|clear>` to transfer
or reset ownership directly. A future identification module/component can give the assembly its own
job/access identity instead of only reading nearby ID cards.

## Server configuration

The server kill switch is `circuitry.lua.enabled` (default `true`). Setting it false rejects new
starts and stops/clears every running Lua runtime on the next tick. Re-enabling it does not restart
programs; scripts always require an explicit Run.

## Limits and budgets

| Limit | Value |
|---|---|
| Basic CPU source cap | 256 KiB (UTF-8 bytes) |
| Advanced CPU source cap | 512 KiB (UTF-8 bytes) |
| Program save rate limit | 1 save per second per CPU |
| Lua instructions before automatic yield | 10,000 basic / 20,000 advanced |
| Per-tick program budget | 0.5 ms basic / 0.75 ms advanced |
| Maximum time without `robot.wait` | 5 seconds |
| Maximum single `robot.wait` | 1 hour |
| `walk_to` timeout | 60 seconds |
| Synchronous action timeout | 30 seconds (`use`, `use_item`, `interact`, `harvest`, `plant`, `trigger`, `inject`) |
| Retained console lines | 100 |
| Maximum guarded string size | 1 MiB |
| Event queue | 16 events (oldest dropped) |
| Opaque handles per runtime | 256 |
| Sensor query budget | 4 per second, burst of 4 (`scan`, `find`, `nearest_*`, `scan_puddles`) |
| `say` rate limit | 0.5 seconds |
| `radio` rate limit | 1 second |
| Power transfer rate limit | 0.5 seconds |
| Power transfer range | 2 tiles by default, 4 maximum |
| Signal/trigger rate limit | 0.5 seconds |
| Transfer rate limit | 0.5 seconds |
| ChemMaster transfer rate limit | 0.5 seconds |
| ChemMaster pill/bottle rate limit | 0.5 seconds |
| Speech/radio text length | 1–200 characters |
| Transfer/dispense amount | 1000 units per call |
| Decal query radius | Up to sensor range, default 0.75 tiles |
| Multi-result scan count | Sensor module maximum, 200 on the standard sensor |
| `wait_event` timeout slices | 50 ms, up to roughly 600 seconds |

## Gotchas

- **`robot.wait`, `walk_to` and synchronous actions cannot cross a `pcall`/C-call boundary.**
  Do not call them inside `pcall`. The `Coroutine` standard library is not exposed.
- **A program must yield.** Loops without `robot.wait` are stopped after 5 seconds.
- **Sensor lookups share a query budget.** `scan`, `find`, `nearest_*` and `scan_puddles` draw
  from the same 4-per-second bucket. A tight loop receives `false/nil, "scan cooldown"` for extra
  calls instead of rescanning hundreds of times per tick. Add `robot.wait(0.25)` to loops.
- **Structures are not items.** Gas pipes, vents, scrubbers, cables and constructible machines
  often inherit `BaseItem`, but they have `ItemComponent` for construction only. `GetKind` treats
  anchored/anchorable entities as `structure` and `MachineComponent` entities as `structure`;
  `scan_items` therefore returns only loose, unanchored items. Use `scan_structures` when you want
  pipes, vents, scrubbers or machines. Walls and windows remain `object` and are not returned by
  `scan_structures`.
- **`find` only sees uncontained entities.** A wrench inside a toolbox, locker, belt, backpack, or
  a mob's hand is intentionally skipped by the entity lookup. Use `robot.held()` for the
  assembly's own held items, `robot.inspect(person)` to see a person's held item names, or target
  the container. A wrench lying on the floor or a table is uncontained and should be found.
- **Single-result lookups return a handle or `nil, reason`.** `find` and all `nearest_*` helpers
  always put the handle first on success; if there is no sensor, no power, or no match, the first
  return is `nil` and the second explains why.
- **Signal events are device-link signals, not radio chatter.** Install a radio module, link a
  remote signaller to the assembly's `Trigger` sink with a network configurator, and listen for
  `type = "signal"` events. `send_signal` uses the assembly's `Pressed` source; `trigger` presses a
  held remote signaller or other use-in-hand item.
- **Handles are not permanent.** They are runtime-local and capped at 256. If an entity is deleted
  or evicted from the handle table, re-acquire it with `scan`/`find`/`nearest_*`.
- **Analyzer APIs are not free.** They require the analyzer module, a sensor module, power, and
  line of sight, and consume action power.
- **Radio channels come only from encryption keys.** `radio_channels()` is the authoritative list;
  do not hard-code channel ids in scripts without checking. The module ships with a common key by
  default.
- **Radio reception is text-only.** Programs receive the transmitted message text and channel id;
  they do not receive chat formatting or range-degraded variants. Radio range/frequency/server
  checks still apply before an event is queued.
- **Beaker slots are 1-based and fixed.** `beaker1..4` are independent item slots. Slot 1 is the
  default for `reagents`, `transfer_to`, `transfer_from`, `water` and `dispense`.
- **`inspect().held` contains names; `robot.held()` contains `{id, name}` entries.** Use the latter
  when you need to act on an item.
- **`robot.use` is not `robot.use_item`.** `use` activates the target; `use_item` uses the active
  hand item on the target.
- **`robot.get_tile_relative`/`tile` and `entities_on_tile` require a powered sensor.** They return
  `false, reason` when the sensor is missing, unpowered, or the tile is out of range.
- **`walk_to` does not open or unlock doors.** There is no automatic path-blocker event. Use
  `scan_doors` + `door_status` and check `can_open`, `bolted`, `access` and `state`; `walk_to`
  returns `false, "no path"` when the destination cannot be reached. Open the door, then issue
  `walk_to` again: a new command clears the previous `NoPath` state and retries pathfinding.
- **`walk_to` waits; `walk_to_async` does not.** A script that wants to do other work while moving
  must use `walk_to_async`; a script that just wants to go somewhere should use `walk_to` and drop
  the old `walk_status` loop.
- **Actions with do-afters wait by default.** If a construction, injection or tool use is
  interrupted, the default function returns `false, "action cancelled"`. Use the `_async` variant
  if the script must continue while the action runs; the next synchronous action waits for the
  previous one to finish first.
- **Decals are tile data, not entities.** `robot.decals()` returns only cleanable decals; use
  `decal_id` with normal cleaning tools/actions. It requires a powered sensor and a grid.
- **Puddles are entities with a solution, not decals.** `robot.scan_puddles()` requires a chemical
  analyzer and a powered sensor, and reports the solution named by `PuddleComponent.SolutionName`
  (normally `puddle`). It only scans uncontained puddles; reagent containers still need `scan()`/
  `find()` plus `analyze_chemicals()`.
- **`item_status`/`solution` work on containers, not just installed beakers.** This is how a robot
  checks a carried bucket, syringe, beaker or flashlight. Flashlight `activated` comes from its
  appearance state and is absent for items without toggleable visuals.

## Complete return shapes

This is the exhaustive per-call contract. Earlier sections explain when to use each call; this
section is the canonical list of what comes back. `handle` means an opaque number, `nil` means Lua
`nil`, and `false, reason` means two return values.

### Reusable shapes

Entity entry returned by `scan*`:

```lua
{
    id = 17,             -- handle
    name = "Crate",
    kind = "item",       -- "mob", "item", "structure", "door", "reagent" or "object"
    distance = 1.2,
    x = 10.0,            -- local grid position
    y = 5.0,
}
```

Entity entry returned by `entities_on_tile` (no `distance`):

```lua
{ id = 17, name = "Crate", kind = "item", x = 10.0, y = 5.0 }
```

Handle entry:

```lua
{ id = 17, name = "Crowbar" }
```

Solution table:

```lua
{
    slot = 1,            -- optional; 1-4 for beakers, 0 for generic solution/item_status
    name = "beaker",     -- optional; solution or container name
    handle = 17,         -- optional; beaker handle
    volume = 25.0,
    max_volume = 50.0,
    temperature = 293.15,
    contents = {
        { id = "Water", amount = 25.0 },
    },
}
```

Inspect result:

```lua
{
    name = "Entity Name",
    kind = "mob",
    species = "Human",           -- optional; humanoid appearance only
    damage = 0.0,
    health = 200.0,
    max_health = 200.0,
    dead = false,
    id = {                       -- optional; ID card found on/in the target
        name = "ID Name",
        job = "Deputy",          -- localized title
        job_id = "Deputy",       -- optional; job prototype id
        departments = { "Security" }, -- optional
        access = { "Security", "Brig", "Maintenance" }, -- optional
    },
    wearing = {                  -- inventory slot -> item name
        head = "Hat",
        outerClothing = "Coat",
    },
    held = { "Item One", "Item Two" }, -- names, not handles
    components = {
        mob = true,
        item = false,
        door = false,
        reagent = false,
    },
    door = {                     -- optional; same shape as door_status
        state = "Closed",
        bolted = false,
        powered = true,
        fire_locked = false,
        access = true,
        can_open = true,
    },
}
```

Self result (`robot.self()`):

```lua
{
    handle = 0,
    name = "programmable assembly",
    x = 10.0,
    y = 5.0,
    power = 85.5,
    max_power = 100.0,
    powered = true,
    damage = 0.0,
    health = 200.0,
    dead = false,
    modules = {
        "cpu", "power", "locomotion", "flight", "manipulator",
        "voice", "sensor", "radio", "reagent",
        "botanical_analyzer", "chemical_analyzer", "medical_analyzer",
    },
}
```

Health result (`robot.health()`):

```lua
{
    damage = 25.0,
    health = 175.0,
    max_health = 200.0,
    dead = false,
}
```

Tile result:

```lua
{
    x = 10.0,
    y = 5.0,
    entities = {
        { id = 17, name = "Crate", kind = "item", x = 10.0, y = 5.0 },
    },
}
```

Cleanable decal entry:

```lua
{
    decal_id = 12,       -- tile decal id, not an entity handle
    decal = "Dirt",
    x = 10.0,
    y = 5.0,
    cleanable = true,
}
```

Puddle entry:

```lua
{
    id = 17,
    name = "puddle",
    x = 10.0,
    y = 5.0,
    distance = 1.2,
    solution = "puddle",
    volume = 10.0,
    max_volume = 1000.0,
    temperature = 293.15,
    contents = {
        { id = "Vomit", amount = 10.0 },
    },
}
```

Door status:

```lua
{
    state = "Closed",
    bolted = false,
    powered = true,
    fire_locked = false,
    access = true,       -- whether the assembly's carried access passes the door
    can_open = true,
}
```

Item status:

```lua
{
    name = "syringe",
    kind = "item",
    activated = false,   -- optional; toggleable items only
    injector = {         -- optional; injectors only
        mode = "Draw",
        transfer_amount = 5.0,
    },
    solutions = {        -- optional; solution containers only
        {
            slot = 0,
            name = "injector",
            volume = 10.0,
            max_volume = 15.0,
            temperature = 293.15,
            contents = { { id = "Water", amount = 10.0 } },
        },
    },
}
```

Radio channel entry:

```lua
{
    id = "Security",
    name = "Security",
    key = ";",
    frequency = 1359,
    default = true,
}
```

Wanted status:

```lua
{
    status = "Wanted",
    wanted = true,
    reason = "Trespass - Vault",       -- optional
    initiator = "Deputy Name",         -- optional
    history = {
        { crime = "Trespass", initiator = "Deputy Name" },
    },
}
```

Event table:

```lua
{
    type = "speech",      -- "speech", "radio", "emote", "signal", "damage", ...
    speaker = 17,         -- optional; source handle when still valid
    name = "Speaker",      -- optional
    id_name = "ID Name",  -- optional
    job = "Engineer",     -- optional
    message = "...",      -- always present; may be empty for non-speech events
    channel = "Security", -- optional; radio events
    port = "Trigger",     -- optional; signal events
    state = true,         -- optional; signal events
    emote = "Scream",     -- optional; emote events
    item = 18,            -- optional; item handle
    item_name = "Crowbar",-- optional
    damage = {            -- optional; damage events
        Blunt = 5.0,
    },
    damage_total = 5.0,
}
```

Plant/tray analysis:

```lua
{
    name = "Hydroponics Tray",
    is_seed = false,
    dead = false,
    health = 100.0,
    water = 50.0,
    nutrition = 50.0,
    toxins = 0.0,
    pests = 0.0,
    weeds = 0.0,
    age = 120.0,
    harvest_ready = false,
    mutation_level = 0.0,
    improper_heat = false,
    improper_pressure = false,
    improper_light = false,
    seed = { -- seed table, below
        name = "wheat",
        display_name = "wheat",
        yield = 3,
        potency = 1.0,
        endurance = 1.0,
        lifespan = 1.0,
        maturation = 1.0,
        production = 1.0,
        growth_stages = 1.0,
        nutrient_consumption = 1.0,
        water_consumption = 1.0,
        ideal_heat = 293.15,
        heat_tolerance = 10.0,
        ideal_light = 1.0,
        light_tolerance = 1.0,
        toxins_tolerance = 1.0,
        low_pressure_tolerance = 1.0,
        high_pressure_tolerance = 1.0,
        pest_tolerance = 1.0,
        weed_tolerance = 1.0,
        weed_high_level_threshold = 1.0,
        viable = true,
        seedless = false,
        chemicals = { "Nutriment" },
        exude_gases = { "Oxygen" },
        consume_gases = { "CarbonDioxide" },
        mutation_prototypes = { "WheatMutation" },
    },
}
```

Seed analysis (`analyze_plant(seed)`):

```lua
{
    name = "wheat seeds",
    is_seed = true,
    seed = { -- same seed table as above },
}
```

Chemical analysis:

```lua
{
    name = "beaker",
    solutions = {
        {
            slot = 0,
            name = "beaker",
            volume = 25.0,
            max_volume = 50.0,
            temperature = 293.15,
            contents = { { id = "Water", amount = 25.0 } },
        },
    },
}
```

Medical analysis:

```lua
{
    name = "Test Human",
    species = "Human",   -- optional
    state = "Alive",     -- "Alive", "Critical" or "Dead"
    damage = 0.0,
    health = 200.0,
    max_health = 200.0,
    dead = false,
    damage_types = {     -- optional; damageable targets only
        Blunt = 0.0,
        Heat = 0.0,
    },
    damage_groups = {    -- optional
        Brute = 0.0,
        Burn = 0.0,
    },
}
```

Gas analysis:

```lua
{
    name = "canister",
    environment = { -- gas mix table, below
        name = "environment",
        volume = 2500.0,
        pressure = 101.3,
        temperature = 293.15,
        gases = {
            { id = "Nitrogen", name = "nitrogen", amount = 80.0, color = "#FF0000" },
        },
    },
    mixes = {
        { -- gas mix table
            name = "tank",
            volume = 1000.0,
            pressure = 250.0,
            temperature = 310.0,
            gases = {
                { id = "Plasma", name = "plasma", amount = 12.0, color = "#FF0000" },
            },
        },
    },
    device_flipped = false,
}
```

ChemMaster status:

```lua
{
    name = "ChemMaster 4000",
    mode = "transfer",              -- "transfer" or "discard"
    sorting = "none",               -- "none", "alphabetical", "quantity", "latest"
    pill_type = 0,
    pill_dosage_limit = 20,
    buffer = {                      -- solution table without slot
        name = "buffer",
        volume = 40.0,
        max_volume = 1000.0,
        temperature = 293.15,
        contents = { { id = "Cryoxadone", amount = 40.0 } },
    },
    input = {                       -- optional; current input container
        name = "beaker",
        volume = 0.0,
        max_volume = 50.0,
        temperature = 293.15,
        contents = {},
    },
    output = {                      -- optional; bottle output solution
        name = "bottle",
        volume = 10.0,
        max_volume = 30.0,
        temperature = 293.15,
        contents = { { id = "Cryoxadone", amount = 10.0 } },
    },
    output_name = "pill canister",  -- optional; output item name
    output_items = 2,               -- optional; pill count for storage outputs
}
```

### Return structure vocabulary

The per-call table uses these shorthand names. `?` marks an optional field, `...` means another
element of the same shape, and all positions are local grid coordinates in tiles.

| Shorthand | Exact Lua structure |
|---|---|
| `handle` | `number`; opaque and runtime-local. `0` is the assembly itself. |
| `boolean` | `true` or `false`. |
| `false, reason` | `false, string`; two return values, not a table. |
| `nil, reason` | `nil, string`; two return values. |
| `x, y` | `number, number`. |
| `power, max_power` | `number, number`. |
| `battery table` | `{ charge = number, max = number, percent = number }`. |
| `elapsed seconds` | `number`. |
| `entity entry` | `{ id = handle, name = string, kind = string, distance = number, x = number, y = number }` |
| `entity entry (tile)` | `{ id = handle, name = string, kind = string, x = number, y = number }` |
| `handle entry` | `{ id = handle, name = string }` |
| `solution table` | `{ slot? = number, name? = string, handle? = handle, volume = number, max_volume = number, temperature = number, contents = { { id = string, amount = number }, ... } }` |
| `self table` | `{ handle = 0, name = string, x = number, y = number, power = number, max_power = number, powered = boolean, damage = number, health = number, dead = boolean, modules = { string, ... } }` |
| `health table` | `{ damage = number, health = number, max_health = number, dead = boolean }` |
| `inspect table` | `{ name = string, kind = string, species? = string, damage = number, health = number, max_health = number, dead = boolean, id? = { name = string, job = string, job_id? = string, departments? = { string, ... }, access? = { string, ... } }, wearing = { [slot] = string }, held = { string, ... }, components = { mob = boolean, item = boolean, door = boolean, reagent = boolean }, door? = door status table }` |
| `tile table` | `{ x = number, y = number, entities = { entity entry (tile), ... } }` |
| `door status table` | `{ state = string, bolted = boolean, powered = boolean, fire_locked = boolean, access = boolean, can_open = boolean }` |
| `item status table` | `{ name = string, kind = string, activated? = boolean, injector? = { mode = string, transfer_amount = number }, solutions? = { solution table with name, ... } }` |
| `wanted table` | `{ status = string, wanted = boolean, reason? = string, initiator? = string, history = { { crime = string, initiator? = string }, ... } }` |
| `event table` | `{ type = string, speaker? = handle, name? = string, id_name? = string, job? = string, message = string, channel? = string, port? = string, state? = boolean, emote? = string, item? = handle, item_name? = string, damage? = { [damageType] = number }, damage_total? = number }`; `message` may be empty for non-speech events. |
| `plant table` | `{ name = string, is_seed = false, dead = boolean, health = number, water = number, nutrition = number, toxins = number, pests = number, weeds = number, age = number, harvest_ready = boolean, mutation_level = number, improper_heat = boolean, improper_pressure = boolean, improper_light = boolean, seed = seed table }` |
| `seed table` | `{ name = string, display_name = string, yield = number, potency = number, endurance = number, lifespan = number, maturation = number, production = number, growth_stages = number, nutrient_consumption = number, water_consumption = number, ideal_heat = number, heat_tolerance = number, ideal_light = number, light_tolerance = number, toxins_tolerance = number, low_pressure_tolerance = number, high_pressure_tolerance = number, pest_tolerance = number, weed_tolerance = number, weed_high_level_threshold = number, viable = boolean, seedless = boolean, chemicals = { string, ... }, exude_gases = { string, ... }, consume_gases = { string, ... }, mutation_prototypes = { string, ... } }` |
| `seed item table` | `{ name = string, is_seed = true, seed = seed table }` |
| `chemical analysis table` | `{ name = string, solutions = { solution table with name, ... } }` |
| `medical table` | `{ name = string, species? = string, state = string, damage = number, health = number, max_health = number, dead = boolean, damage_types? = { [damageType] = number }, damage_groups? = { [damageGroup] = number } }` |
| `gas table` | `{ name = string, environment = gas mix table, mixes = { gas mix table, ... }, device_flipped = boolean }` |
| `gas mix table` | `{ name = string, volume = number, pressure = number, temperature = number, gases = { { id = string, name = string, amount = number, color = string }, ... } }` |
| `ChemMaster status table` | `{ name = string, mode = string, sorting = string, pill_type = number, pill_dosage_limit = number, buffer = solution table, input? = solution table, output? = solution table, output_name? = string, output_items? = number }` |
| `list of entity entries` | `{ entity entry, ... }` |
| `list of entity entries without distance` | `{ entity entry (tile), ... }` |
| `list of handle entries` | `{ handle entry, ... }`; used by `robot.held()` |
| `list of solution tables` | `{ solution table, ... }`; `robot.beakers()` entries add `slot` and `handle` |
| `list of decal entries` | `{ { decal_id = number, decal = string, x = number, y = number, cleanable = true }, ... }` |
| `list of puddle entries` | `{ { id = handle, name = string, x = number, y = number, distance = number, solution = string, volume = number, max_volume = number, temperature = number, contents = { { id = string, amount = number }, ... } }, ... }` |
| `list of radio channel entries` | `{ { id = string, name = string, key = string, frequency = number, default = boolean }, ... }` |

### Per-call return contracts

| Call | Success return | Failure return |
|---|---|---|
| `robot.self()` | self table | `nil` if the assembly is missing |
| `robot.position()` | `x, y` | — |
| `robot.power()` | `power, max_power` | — |
| `robot.battery(handle)` | battery table | `false, reason` |
| `robot.charge(handle, amount)` | `true` | `false, reason` |
| `robot.charge_from(amount [, radius])` | `true` | `false, reason` |
| `robot.health()` | health table | — |
| `robot.has_module(name)` | boolean | `false, reason` for invalid arguments |
| `robot.log(...)` / `print(...)` | `nil` | — |
| `robot.wait(seconds)` | elapsed seconds (`number`) | — |
| `robot.move(dx, dy)` | `true` | `false, reason` |
| `robot.walk_to(x, y [, range, smooth])` / `robot.walk_to(handle)` | `true` on arrival | `false, reason` |
| `robot.walk_to_async(x, y [, range, smooth])` / `robot.walk_to_async(handle)` | `true` once the path starts | `false, reason` |
| `robot.stop()` | `true` | — |
| `robot.walk_status()` | `"idle"`, `"moving"`, `"arrived"` or `"no_path"` | — |
| `robot.scan([radius])` | list of entity entries | `false, reason` |
| `robot.scan_mobs([radius])` | list of entity entries | `false, reason` |
| `robot.scan_items([radius])` | list of entity entries | `false, reason` |
| `robot.scan_structures([radius])` | list of entity entries | `false, reason` |
| `robot.scan_doors([radius])` | list of entity entries | `false, reason` |
| `robot.find(name [, radius])` | `handle` | `nil, reason` |
| `robot.inspect(handle)` | inspect table | `false, reason` |
| `robot.can_see(handle)` | boolean | — |
| `robot.can_hear(handle)` | boolean | — |
| `robot.nearest_mob([radius])` | `handle` | `nil, reason` |
| `robot.nearest_person([radius])` | `handle` | `nil, reason` |
| `robot.nearest_item([radius])` | `handle` | `nil, reason` |
| `robot.nearest_structure([radius])` | `handle` | `nil, reason` |
| `robot.nearest_door([radius])` | `handle` | `nil, reason` |
| `robot.distance(handle)` | number; `-1` across maps | — |
| `robot.position_of(handle)` | `x, y` | `false, reason` |
| `robot.can_reach(handle)` | boolean | — |
| `robot.get_tile_relative([dx, dy])` / `robot.tile(...)` | tile table | `false, reason` |
| `robot.entities_on_tile([dx, dy])` | list of entity entries without `distance` | `false, reason` |
| `robot.decals([radius])` | list of decal entries | `false, reason` |
| `robot.scan_puddles([radius])` | list of puddle entries | `false, reason` |
| `robot.has_job(handle, job)` | boolean | `false, reason` for sensor/power/range; plain `false` for no ID |
| `robot.has_access(handle, access)` | boolean | `false, reason` for sensor/power/range; plain `false` for no ID |
| `robot.wanted_status(handle)` | wanted table | `false, reason` |
| `robot.solution(handle [, name])` | solution table | `false, reason` |
| `robot.item_status(handle)` | item status table | `false, reason` |
| `robot.door_status(handle)` | door status table | `false, reason` |
| `robot.can_open(handle)` | boolean | — |
| `robot.poll_event()` | event table | `nil` when empty |
| `robot.wait_event([timeout])` | event table | `nil` on timeout |
| `robot.say(text)` | `true` | `false, reason` |
| `robot.radio(text [, channel])` | `true` | `false, reason` |
| `robot.radio_channels()` | list of radio channel entries | `false, reason` |
| `robot.pickup(handle)` | `true` | `false, reason` |
| `robot.drop()` | `true` | `false, reason` |
| `robot.held()` | list of handle entries | — |
| `robot.use(handle)` | `true` after any do-after | `false, reason` |
| `robot.use_item(handle)` | `true` after any do-after | `false, reason` |
| `robot.attack(handle)` | `true` | `false, reason` |
| `robot.shoot(handle)` | `true` | `false, reason` |
| `robot.interact(handle)` / `robot.harvest(handle)` | `true` after any do-after | `false, reason` |
| `robot.plant(handle)` | `true` after any do-after | `false, reason` |
| `robot.water(target, amount [, slot])` | `true` | `false, reason` |
| `robot.trigger(handle)` | `true` after any do-after | `false, reason` |
| `robot.send_signal([state [, port]])` | `true` | `false, reason` |
| `robot.inject(handle)` | `true` after the do-after | `false, reason` |
| `robot.use_async` / `use_item_async` / `interact_async` / `harvest_async` / `plant_async` / `trigger_async` / `inject_async` | `true` | `false, reason` |
| `robot.action_busy()` | boolean | — |
| `robot.action_marker()` / `robot.action_status(marker)` | action tracking helpers | — |
| `robot.beakers()` | list of solution tables with `slot`/`handle` | — |
| `robot.reagents([slot])` | solution table | `false, reason` |
| `robot.transfer_to(target, amount [, slot])` | `true` | `false, reason` |
| `robot.transfer_from(target, amount [, slot])` | `true` | `false, reason` |
| `robot.transfer(source, target, amount)` | `true` | `false, reason` |
| `robot.dispense(dispenser [, selector, amount, slot])` | `true` | `false, reason` |
| `robot.analyze_plant(handle)` | plant or seed table | `false, reason` |
| `robot.analyze_chemicals(handle)` | chemical analysis table | `false, reason` |
| `robot.analyze_medical(handle)` | medical table | `false, reason` |
| `robot.analyze_gas(handle)` | gas table | `false, reason` |
| `robot.chem_master_status(handle)` | ChemMaster status table | `false, reason` |
| `robot.chem_master_set_mode(handle, mode)` | `true` | `false, reason` |
| `robot.chem_master_set_pill_type(handle, type)` | `true` | `false, reason` |
| `robot.chem_master_transfer(handle, reagent, amount, from_buffer)` | `true` | `false, reason` |
| `robot.chem_master_create_pills(handle, dosage, number [, label])` | `true` | `false, reason` |
| `robot.chem_master_bottle(handle, dosage [, label])` | `true` | `false, reason` |

`false, reason` always means two separate Lua return values, not a table. For calls documented as
returning a bare boolean, the failure value is just `false`.


## Maintenance

When changing the API:

1. Update the relevant table and any data-shape example in this file.
2. Add every new bare API name to `LuaCodeEditor.ApiFunctions` in
   `Content.Client/_PS/Circuitry/UI/LuaCodeEditor.cs` so the in-game editor colors it.
3. Update the changelog below.
4. Keep `.ai/systems/circuitry.md` in sync for architecture/hazards.
5. Prefer additive changes; renaming or changing a return shape is a breaking API change and should
   be called out explicitly.
6. If a new hardware module or capability is added, update the hardware table and
   `robot.has_module` aliases.
7. Run the relevant tests:
   - `dotnet test Content.Tests/Content.Tests.csproj -c Debug --filter "FullyQualifiedName~LuaProgramTest"`
   - `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~Circuitry"`

## Changelog

| Date | Change |
|---|---|
| 2026-11-07 | Initial API reference for Phases 1–3 and radio: status, movement, perception, events, speech, radio with encryption-key channels, manipulation/combat, four beakers, transfers, dispenser, botanical/chemical/medical analyzers. |
| 2026-11-07 | Power tuning reduced idle/action/movement draws by 10–20× so a microreactor sustains a fully loaded assembly. `find` was documented as uncontained-only and given explicit failure reasons. Added `CircuitryPowerTest` and `CircuitAssemblyFilled`. |
| 2026-11-07 | Fixed `scan_items` classifying installed pipes/vents/scrubbers as items. Added `scan_structures` and `nearest_structure`; walls/windows remain `object`, constructible machines are structures. Added `CircuitryScanKindsTest`. |
| 2026-11-07 | Standardized single-entity lookups on handles: `find` and all `nearest_*` helpers return a handle or `nil, reason`. Added a handle policy section and per-subsystem usage examples. |
| 2026-11-07 | Added `position_of`, `item_status`, `solution`, generic `transfer`, syringe filling/injection (`inject`), follow/treatment examples, and device-link signal send/receive (`trigger`, `send_signal`) through the radio module. |
| 2026-11-07 | Added emote, damage, attacked and projectile perception events; cleanable-decal queries; `scan_doors`, `door_status` and `can_open`; beaker solution handles. |
| 2026-11-07 | Added the gas analyzer module and `analyze_gas` for pipes, vents, scrubbers, canisters, filters, mixers and local atmosphere readings. |
| 2026-11-09 | Added GLua-style table/string helpers: `table.HasValue`/`Contains`, `IsEmpty`, `Count`, `GetKeys`, `KeyFromValue`, `Random`, `Add`, `RemoveByValue`; `string.Trim`, `StartsWith`/`StartWith`, `EndsWith`, `Split`; `istable`/`isstring`/`isnumber`/`isbool`/`isfunction`. |
| 2026-11-09 | Replaced the engine editor with a custom inline-syntax-colored Lua editor (line numbers, selection, copy/paste, tab indentation, undo/redo). Added multi-port signal sources, transfer rate limiting, Bluespace Dock/grid persistence coverage, puddle chemical analysis, ChemMaster automation, job/access/wanted queries, and chassis variants. |
| 2026-11-10 | Standard sensor range is now 16 tiles. `find`/`nearest_*` search the full range instead of only the 16 nearest entities. Sensor lookups share a 4-per-second token bucket, and `walk_to` rate-limits/retargets path starts so tight follow loops cannot spam pathfinding or trip the allocation watchdog. |
| 2026-11-10 | Multi-result scans now cap at 200 results instead of 16. The scanner raycasts nearest-first only until the cap is filled, so the larger result set does not multiply line-of-sight checks. |
| 2026-11-10 | Added battery inspection and power transfer APIs: `battery`, `charge`, and `charge_from`. Assemblies can draw from nearby APCs/batteries and charge held or nearby cells/guns. |
| 2026-11-10 | Fixed `shoot` incorrectly requiring the target to be within manipulator hand range; ranged attacks now use sight range. |
| 2026-11-10 | `walk_to` and `walk_to_async` now accept a single entity handle as an alternative to `x, y`, so scripts can walk directly to `find`/`nearest_*` results. |
| 2026-11-10 | Lua-facing actions are synchronous by default. `walk_to` yields until arrival/no-path/timeout and `walk_to_async` is the fire-and-forget variant. `use`, `use_item`, `interact`/`harvest`, `plant`, `trigger` and `inject` wait for their do-after and expose `_async` variants; instant actions are unchanged. |
