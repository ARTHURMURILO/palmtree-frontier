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

### Movement and pathfinding

```lua
local ok, reason = robot.walk_to(10, 4)
if ok then
    while robot.walk_status() == "moving" do
        robot.wait(0.2)
    end
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
inspect a route before committing to it, and `walk_status() == "no_path"` to detect an unreachable
destination.

### Cleanable decals

`robot.decals(radius)` returns cleanable decals within the sensor radius (default `0.75` m). The
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

### Puddles and floor spills

`robot.scan_puddles(radius)` requires a powered sensor **and** a chemical analyzer. It scans uncontained
floor puddles (default radius `1` m) and returns their solution volume, temperature and contents.
It shares the normal `scan()`/`find()` 0.25 s cooldown.

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
    local x, y = robot.position_of(wrench)
    if robot.walk_to(x, y, 1.0) then
        while robot.walk_status() == "moving" do
            robot.wait(0.2)
        end

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

    while robot.walk_status() == "moving" do
        robot.wait(0.2)
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

    while robot.walk_status() == "moving" do
        robot.wait(0.2)
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
            while robot.walk_status() == "moving" do
                robot.wait(0.2)
            end
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
    while robot.walk_status() == "moving" do
        robot.wait(0.2)
    end

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
    while robot.walk_status() == "moving" do
        robot.wait(0.2)
    end

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

## Status and modules

| Function | Returns | Notes |
|---|---|---|
| `robot.self()` | table or `nil` | `handle`, `name`, `x`, `y`, `power`, `max_power`, `powered`, `damage`, `health`, `dead`, `modules`. Does not include `max_health`. |
| `robot.position()` | `x, y` | Assembly position in its current map/grid space. |
| `robot.power()` | `current, max` | Power cell charge. Returns `0, 0` if there is no cell. |
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
| `robot.walk_to(x, y [, range [, smooth]])` | Locomotion, power | `true` or `false, reason` |
| `robot.stop()` | — | `true` |
| `robot.walk_status()` | — | `"idle"`, `"moving"`, `"arrived"` or `"no_path"` |

- `move` takes a direction/vector; it is normalized. `(0, 0)` stops movement.
- `walk_to` uses pathfinding. The target is in local grid/map coordinates. `range` defaults to
  `0.5`, and values below `0.5` are raised to `0.5` to avoid overshoot loops around small targets.
  With `smooth = true` (the default), the arrival tolerance scales with current walk speed up to
  `1.25` tiles and the steering system brakes to roughly 15% of walk speed near the target. Pass
  `smooth = false` for the old fixed-tolerance behavior. `walk_status` reports `arrived` once
  within the effective tolerance and stops the path.
- Direct movement cancels an in-progress path.
- `walk_status` is the simplest way to wait for arrival:

```lua
robot.walk_to(10, 4)
while robot.walk_status() == "moving" do
    robot.wait(0.2)
end
```

## Perception

All perception helpers require a **sensor module** unless noted. Most also require power, consume
action power, and respect line of sight and sensor range.

| Function | Returns | Notes |
|---|---|---|
| `robot.scan([radius])` | list or `false, reason` | All entities in range: `{id, name, kind, distance, x, y}`. Radius defaults to the sensor range and is clamped to it. Has a 0.25 s cooldown. |
| `robot.scan_mobs([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "mob"`. |
| `robot.scan_items([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "item"`. Only loose, unanchored items; installed pipes, vents, scrubbers and machines are excluded. |
| `robot.scan_structures([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "structure"`. Returns installed/anchorable machinery, pipes, vents, scrubbers and constructible machines; walls/windows remain `object`. |
| `robot.scan_doors([radius])` | list or `false, reason` | Same as `scan`, filtered to `kind == "door"`. |
| `robot.find(name [, radius])` | handle or `nil, reason` | Case-insensitive substring match on entity name. Returns the nearest match as a bare handle. Returns `nil, reason` when there is no match, no sensor, or no power. Only scans **uncontained** entities. Intentionally bypasses the scan cooldown check but still costs power and sets the next scan time. |
| `robot.inspect(handle)` | table or `false, reason` | Identity, health, ID, worn equipment, held items and component flags. Requires sensor range and line of sight. |
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
| `robot.scan_puddles([radius])` | list or `false, reason` | Chemical analyzer and powered sensor required. Returns floor puddles with `id`, name, position, volume, temperature and contents. |
| `robot.door_status(handle)` | table or `false, reason` | Door state, bolted/powered/firelock/access flags and `can_open`. |
| `robot.can_open(handle)` | boolean | Shortcut for `door_status(handle).can_open`. |

`dx`/`dy` are offsets from the assembly's current position. Tile lookups are limited by sensor
range and line of sight for each entity.

`find`, `scan`, `scan_mobs` and `scan_items` are capped by the sensor module's maximum scan result
count (normally 16) and return the nearest results first.

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
| `robot.trigger(handle)` | Manipulator, powered, active held item | `true` or `false, reason` |
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
| `robot.use(handle)` | Power, interaction permission, hand range | `true` or `false, reason` |
| `robot.use_item(handle)` | Manipulator, active held item, power | `true` or `false, reason` |
| `robot.interact(handle)` / `robot.harvest(handle)` | Manipulator, power | `true` or `false, reason` |
| `robot.plant(handle)` | Same as `use_item` | `true` or `false, reason` |
| `robot.attack(handle)` | Manipulator, melee weapon, power | `true` or `false, reason` |
| `robot.shoot(handle)` | Manipulator, gun in hand, line of sight | `true` or `false, reason` |
| `robot.inject(handle)` | Manipulator, syringe/injector in active hand, power | `true` or `false, reason` |

- `pickup` requires the target to have an `ItemComponent`.
- `drop` drops the assembly's active held item.
- `use` raises an `ActivateInWorldEvent` (buttons, doors, machines).
- `use_item` raises an `InteractUsingEvent` with the active hand item (seeds, tools, bottles).
- `interact` raises an `InteractHandEvent` (for example, harvesting a hydroponics tray).
- `plant` is an alias for `use_item`.
- `harvest` is an alias for `interact`.
- `attack` uses the active hand's melee weapon.
- `shoot` uses the active hand's gun. The target check uses at least 20 metres of line of sight, or
  the sensor range if larger.

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
    id = { name = "ID Name", job = "Job Title" }, -- optional
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
assembly panel is view-only for everyone else. The owner can transfer ownership by username after
unlocking; admins can use `circuitowner <assembly uid> <username|clear>` to transfer or reset
ownership directly. Ownership transfers lock the assembly and claim the new owner.

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
| Retained console lines | 100 |
| Maximum guarded string size | 1 MiB |
| Event queue | 16 events (oldest dropped) |
| Opaque handles per runtime | 256 |
| Scan cooldown | 0.25 seconds |
| `say` rate limit | 0.5 seconds |
| `radio` rate limit | 1 second |
| Signal/trigger rate limit | 0.5 seconds |
| Transfer rate limit | 0.5 seconds |
| Speech/radio text length | 1–200 characters |
| Transfer/dispense amount | 1000 units per call |
| Decal query radius | Up to sensor range, default 0.75 m |
| `find` / scan result count | Sensor module maximum, normally 16 |
| `wait_event` timeout slices | 50 ms, up to roughly 600 seconds |

## Gotchas

- **`robot.wait` cannot cross a `pcall`/C-call boundary.** Do not call it inside `pcall`. The
  `Coroutine` standard library is not exposed.
- **A program must yield.** Loops without `robot.wait` are stopped after 5 seconds.
- **`find` ignores the scan cooldown.** It still consumes power and advances the cooldown timer, so
  a script can scan and immediately find once, then must wait for the next scan.
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
  `scan_doors` + `door_status` and check `can_open`, `bolted`, `access` and `state`; `walk_status()`
  returns `no_path` when the destination cannot be reached.
- **Decals are tile data, not entities.** `robot.decals()` returns only cleanable decals; use
  `decal_id` with normal cleaning tools/actions. It requires a powered sensor and a grid.
- **Puddles are entities with a solution, not decals.** `robot.scan_puddles()` requires a chemical
  analyzer and a powered sensor, and reports the solution named by `PuddleComponent.SolutionName`
  (normally `puddle`). It only scans uncontained puddles; reagent containers still need `scan()`/
  `find()` plus `analyze_chemicals()`.
- **`item_status`/`solution` work on containers, not just installed beakers.** This is how a robot
  checks a carried bucket, syringe, beaker or flashlight. Flashlight `activated` comes from its
  appearance state and is absent for items without toggleable visuals.

## Maintenance

When changing the API:

1. Update the relevant table and any data-shape example in this file.
2. Update the changelog below.
3. Keep `.ai/systems/circuitry.md` in sync for architecture/hazards.
4. Prefer additive changes; renaming or changing a return shape is a breaking API change and should
   be called out explicitly.
5. If a new hardware module or capability is added, update the hardware table and
   `robot.has_module` aliases.
6. Run the relevant tests:
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
| 2026-11-09 | Replaced the engine editor with a custom inline-syntax-colored Lua editor (line numbers, selection, copy/paste, tab indentation, undo/redo). Added ownership transfer UI/admin command, multi-port signal sources, transfer rate limiting, Bluespace Dock/grid persistence coverage, and puddle chemical analysis. |
