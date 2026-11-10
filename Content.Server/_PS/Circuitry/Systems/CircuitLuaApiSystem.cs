using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._PS.Circuitry.Components;
using Content.Server._PS.Security;
using Content.Server.Access.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Construction.Components;
using Content.Server.Decals;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Hands.Systems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Radio.EntitySystems;
using Content.Server.Weapons.Melee;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._PS.Circuitry;
using Content.Shared.Access.Components;
using Content.Shared.Atmos;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Construction.Components;
using Content.Shared.CriminalRecords;
using Content.Shared.Damage;
using Content.Shared.Decals;
using Content.Shared.DeviceLinking;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NodeContainer;
using Content.Shared.PowerCell;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Security;
using Content.Shared.Speech;
using Content.Shared.Storage;
using Content.Shared.Toggleable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using MoonSharp.Interpreter;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: registers the `robot` Lua table for one assembly runtime. All entity references are
/// opaque integer handles and every action goes through an existing gameplay system.
/// </summary>
public sealed class CircuitLuaApiSystem : EntitySystem
{
    private const string WaitEventSource = """
        return function(timeout)
            local t = tonumber(timeout) or 1
            local steps = math.max(1, math.min(12000, math.floor(t / 0.05)))
            for i = 1, steps do
                local ev = robot.poll_event()
                if ev then return ev end
                robot.wait(0.05)
            end
            return nil
        end
        """;

    private const string WalkToSource = """
        return function(x, y, range, smooth)
            local ok, reason = robot.walk_to_async(x, y, range, smooth)
            if not ok then
                -- Yield even on failure so a retry loop cannot spin without ever waiting.
                robot.wait(0.1)
                return false, reason
            end

            -- walk_to_async may have been an immediate "already here" no-op.
            local status = robot.walk_status()
            if status == "arrived" then
                -- Yield one tick so a follow loop cannot spin when the target is already adjacent.
                robot.wait(0)
                return true
            end
            if status == "no_path" then
                robot.wait(0.1)
                return false, "no path"
            end

            local elapsed = 0
            while status == "moving" do
                elapsed = elapsed + robot.wait(0.1)
                if elapsed > 60 then
                    robot.stop()
                    return false, "walk timed out"
                end
                status = robot.walk_status()
            end

            if status == "arrived" then return true end
            if status == "no_path" then
                robot.wait(0.1)
                return false, "no path"
            end
            robot.wait(0.1)
            return false, "movement stopped"
        end
        """;

    /// <summary>
    /// Wraps an instant action callback so it waits for any do-after the action started. The
    /// original callback is exposed as the matching `_async` function. The wrapper yields through
    /// `robot.wait`, so a long construction or injection does not block the server tick.
    /// </summary>
    private const string ActionWrapperSource = """
        return function(async_fn)
            local function wait_current()
                local elapsed = 0
                while robot.action_busy() do
                    elapsed = elapsed + robot.wait(0.05)
                    if elapsed > 30 then return false, "action timed out" end
                end
                return true
            end

            return function(...)
                -- Finish a previous action first so its completed/cancelled entry cannot be
                -- mistaken for this action's result.
                local idle, idleReason = wait_current()
                if not idle then return false, idleReason end

                local marker = robot.action_marker()
                local ok, reason = async_fn(...)
                if not ok then return false, reason end

                local elapsed = 0
                while true do
                    local status = robot.action_status(marker)
                    if status == "running" then
                        elapsed = elapsed + robot.wait(0.05)
                        if elapsed > 30 then return false, "action timed out" end
                    elseif status == "cancelled" then
                        return false, "action cancelled"
                    else
                        return true
                    end
                end
            end
        end
        """;

    private readonly HashSet<EntityUid> _lookupSet = new();

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly AccessSystem _accessSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AtmosphereSystem _atmo = default!;
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly CircuitAssemblySystem _assembly = default!;
    [Dependency] private readonly CircuitMovementSystem _movement = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly IdCardSystem _idCardSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InjectorSystem _injector = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly MeleeWeaponSystem _melee = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WantedStatusSystem _wanted = default!;

    public void Configure(Script script, Table robot, CircuitLuaRuntime runtime)
    {
        runtime.Script = script;

        robot.Set("self", DynValue.NewCallback((_, _) => BuildSelf(runtime), "self"));
        robot.Set("position", DynValue.NewCallback((_, _) => BuildPosition(runtime), "position"));
        robot.Set("power", DynValue.NewCallback((_, _) => BuildPower(runtime), "power"));
        robot.Set("health", DynValue.NewCallback((_, _) => BuildHealth(runtime), "health"));
        robot.Set("has_module", DynValue.NewCallback((_, args) => HasModule(runtime, args), "has_module"));

        robot.Set("move", DynValue.NewCallback((_, args) => Move(runtime, args), "move"));
        robot.Set("walk_to_async", DynValue.NewCallback((_, args) => WalkTo(runtime, args), "walk_to_async"));
        robot.Set("stop", DynValue.NewCallback((_, _) => Stop(runtime), "stop"));
        robot.Set("walk_status", DynValue.NewCallback((_, _) => WalkStatus(runtime), "walk_status"));

        // Blocking-to-Lua wrapper: starts the async path, then yields until the robot arrives.
        // This keeps walk_to from blocking the server thread while making scripts much simpler.
        var walkToBuilder = script.LoadString(WalkToSource, null, "circuit-walk-to");
        robot.Set("walk_to", script.Call(walkToBuilder));

        robot.Set("scan", DynValue.NewCallback((_, args) => Scan(runtime, args, null), "scan"));
        robot.Set("scan_mobs", DynValue.NewCallback((_, args) => Scan(runtime, args, "mob"), "scan_mobs"));
        robot.Set("scan_items", DynValue.NewCallback((_, args) => Scan(runtime, args, "item"), "scan_items"));
        robot.Set("scan_structures", DynValue.NewCallback((_, args) => Scan(runtime, args, "structure"), "scan_structures"));
        robot.Set("scan_doors", DynValue.NewCallback((_, args) => Scan(runtime, args, "door"), "scan_doors"));
        robot.Set("find", DynValue.NewCallback((_, args) => Find(runtime, args), "find"));
        robot.Set("inspect", DynValue.NewCallback((_, args) => Inspect(runtime, args), "inspect"));
        robot.Set("can_see", DynValue.NewCallback((_, args) => CanSee(runtime, args), "can_see"));
        robot.Set("can_hear", DynValue.NewCallback((_, args) => CanHear(runtime, args), "can_hear"));
        robot.Set("nearest_mob", DynValue.NewCallback((_, args) => Nearest(runtime, args, "mob"), "nearest_mob"));
        robot.Set("nearest_person", DynValue.NewCallback((_, args) => NearestPerson(runtime, args), "nearest_person"));
        robot.Set("nearest_item", DynValue.NewCallback((_, args) => Nearest(runtime, args, "item"), "nearest_item"));
        robot.Set("nearest_structure", DynValue.NewCallback((_, args) => Nearest(runtime, args, "structure"), "nearest_structure"));
        robot.Set("nearest_door", DynValue.NewCallback((_, args) => Nearest(runtime, args, "door"), "nearest_door"));
        robot.Set("distance", DynValue.NewCallback((_, args) => Distance(runtime, args), "distance"));
        robot.Set("position_of", DynValue.NewCallback((_, args) => PositionOf(runtime, args), "position_of"));
        robot.Set("can_reach", DynValue.NewCallback((_, args) => CanReach(runtime, args), "can_reach"));
        robot.Set("get_tile_relative", DynValue.NewCallback((_, args) => GetTileRelative(runtime, args), "get_tile_relative"));
        robot.Set("tile", DynValue.NewCallback((_, args) => GetTileRelative(runtime, args), "tile"));
        robot.Set("entities_on_tile", DynValue.NewCallback((_, args) => EntitiesOnTile(runtime, args), "entities_on_tile"));
        robot.Set("decals", DynValue.NewCallback((_, args) => Decals(runtime, args), "decals"));
        robot.Set("scan_puddles", DynValue.NewCallback((_, args) => Puddles(runtime, args), "scan_puddles"));
        robot.Set("has_job", DynValue.NewCallback((_, args) => HasJob(runtime, args), "has_job"));
        robot.Set("has_access", DynValue.NewCallback((_, args) => HasAccess(runtime, args), "has_access"));
        robot.Set("wanted_status", DynValue.NewCallback((_, args) => WantedStatus(runtime, args), "wanted_status"));
        robot.Set("solution", DynValue.NewCallback((_, args) => Solution(runtime, args), "solution"));
        robot.Set("item_status", DynValue.NewCallback((_, args) => ItemStatus(runtime, args), "item_status"));
        robot.Set("door_status", DynValue.NewCallback((_, args) => DoorStatus(runtime, args), "door_status"));
        robot.Set("can_open", DynValue.NewCallback((_, args) => CanOpen(runtime, args), "can_open"));
        robot.Set("poll_event", DynValue.NewCallback((_, _) => PollEvent(runtime), "poll_event"));

        // A small Lua shim gives wait_event without adding scheduler reentrancy: it polls in
        // 50 ms slices and yields through the normal robot.wait path.
        var waitEventBuilder = script.LoadString(WaitEventSource, null, "circuit-wait-event");
        robot.Set("wait_event", script.Call(waitEventBuilder));

        robot.Set("action_busy", DynValue.NewCallback((_, _) => ActionBusy(runtime), "action_busy"));
        robot.Set("action_marker", DynValue.NewCallback((_, _) => ActionMarker(runtime), "action_marker"));
        robot.Set("action_status", DynValue.NewCallback((_, args) => ActionStatus(runtime, args), "action_status"));

        var actionWrapperFactory = script.Call(
            script.LoadString(ActionWrapperSource, null, "circuit-action-wrapper"));

        void RegisterAction(string name, string asyncName, DynValue callback)
        {
            robot.Set(asyncName, callback);
            robot.Set(name, script.Call(actionWrapperFactory, callback));
        }

        robot.Set("say", DynValue.NewCallback((_, args) => Say(runtime, args), "say"));
        robot.Set("radio", DynValue.NewCallback((_, args) => Radio(runtime, args), "radio"));
        robot.Set("radio_channels", DynValue.NewCallback((_, _) => RadioChannels(runtime), "radio_channels"));
        robot.Set("pickup", DynValue.NewCallback((_, args) => Pickup(runtime, args), "pickup"));
        robot.Set("drop", DynValue.NewCallback((_, _) => Drop(runtime), "drop"));
        robot.Set("held", DynValue.NewCallback((_, _) => Held(runtime), "held"));

        var useCallback = DynValue.NewCallback((_, args) => Use(runtime, args), "use_async");
        RegisterAction("use", "use_async", useCallback);

        var useItemCallback = DynValue.NewCallback((_, args) => UseItem(runtime, args), "use_item_async");
        RegisterAction("use_item", "use_item_async", useItemCallback);
        robot.Set("plant_async", useItemCallback);
        robot.Set("plant", script.Call(actionWrapperFactory, useItemCallback));

        robot.Set("attack", DynValue.NewCallback((_, args) => Attack(runtime, args), "attack"));
        robot.Set("shoot", DynValue.NewCallback((_, args) => Shoot(runtime, args), "shoot"));

        var interactCallback = DynValue.NewCallback((_, args) => Interact(runtime, args), "interact_async");
        RegisterAction("interact", "interact_async", interactCallback);
        robot.Set("harvest_async", interactCallback);
        robot.Set("harvest", script.Call(actionWrapperFactory, interactCallback));

        robot.Set("water", DynValue.NewCallback((_, args) => Transfer(runtime, args, true), "water"));

        var triggerCallback = DynValue.NewCallback((_, args) => Trigger(runtime, args), "trigger_async");
        RegisterAction("trigger", "trigger_async", triggerCallback);

        robot.Set("send_signal", DynValue.NewCallback((_, args) => SendSignal(runtime, args), "send_signal"));

        var injectCallback = DynValue.NewCallback((_, args) => Inject(runtime, args), "inject_async");
        RegisterAction("inject", "inject_async", injectCallback);

        robot.Set("analyze_plant", DynValue.NewCallback((_, args) => AnalyzePlant(runtime, args), "analyze_plant"));
        robot.Set("analyze_chemicals", DynValue.NewCallback((_, args) => AnalyzeChemicals(runtime, args), "analyze_chemicals"));
        robot.Set("analyze_medical", DynValue.NewCallback((_, args) => AnalyzeMedical(runtime, args), "analyze_medical"));
        robot.Set("analyze_gas", DynValue.NewCallback((_, args) => AnalyzeGas(runtime, args), "analyze_gas"));
        robot.Set("chem_master_status", DynValue.NewCallback((_, args) => ChemMasterStatus(runtime, args), "chem_master_status"));
        robot.Set("chem_master_set_mode", DynValue.NewCallback((_, args) => ChemMasterSetMode(runtime, args), "chem_master_set_mode"));
        robot.Set("chem_master_set_pill_type", DynValue.NewCallback((_, args) => ChemMasterSetPillType(runtime, args), "chem_master_set_pill_type"));
        robot.Set("chem_master_transfer", DynValue.NewCallback((_, args) => ChemMasterTransfer(runtime, args), "chem_master_transfer"));
        robot.Set("chem_master_create_pills", DynValue.NewCallback((_, args) => ChemMasterCreatePills(runtime, args), "chem_master_create_pills"));
        robot.Set("chem_master_bottle", DynValue.NewCallback((_, args) => ChemMasterBottle(runtime, args), "chem_master_bottle"));
        robot.Set("dispense", DynValue.NewCallback((_, args) => Dispense(runtime, args), "dispense"));

        robot.Set("beakers", DynValue.NewCallback((_, _) => Beakers(runtime), "beakers"));
        robot.Set("reagents", DynValue.NewCallback((_, args) => Reagents(runtime, args), "reagents"));
        robot.Set("transfer_to", DynValue.NewCallback((_, args) => Transfer(runtime, args, true), "transfer_to"));
        robot.Set("transfer_from", DynValue.NewCallback((_, args) => Transfer(runtime, args, false), "transfer_from"));
        robot.Set("transfer", DynValue.NewCallback((_, args) => TransferBetween(runtime, args), "transfer"));
    }

    private DynValue BuildSelf(CircuitLuaRuntime runtime)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return DynValue.NewNil();

        var table = NewTable(runtime);
        table.Set("handle", DynValue.NewNumber(0));
        table.Set("name", DynValue.NewString(Name(runtime.Assembly)));

        var xform = Transform(runtime.Assembly);
        table.Set("x", DynValue.NewNumber(xform.Coordinates.Position.X));
        table.Set("y", DynValue.NewNumber(xform.Coordinates.Position.Y));

        var (power, maxPower) = GetPower(runtime.Assembly);
        table.Set("power", DynValue.NewNumber(power));
        table.Set("max_power", DynValue.NewNumber(maxPower));
        table.Set("powered", DynValue.NewBoolean(component.Powered));

        var (damage, maxHealth) = GetHealth(runtime.Assembly);
        table.Set("damage", DynValue.NewNumber(damage));
        table.Set("health", DynValue.NewNumber(MathF.Max(0f, maxHealth - damage)));
        table.Set("dead", DynValue.NewBoolean(_mobState.IsDead(runtime.Assembly)));

        var modules = NewTable(runtime);
        var index = 1;
        if (component.Capabilities.HasFlag(CircuitCapability.Cpu))
            modules.Set(index++, DynValue.NewString("cpu"));
        if (component.Capabilities.HasFlag(CircuitCapability.Power))
            modules.Set(index++, DynValue.NewString("power"));
        if (component.Capabilities.HasFlag(CircuitCapability.Locomotion))
            modules.Set(index++, DynValue.NewString("locomotion"));
        if (component.Capabilities.HasFlag(CircuitCapability.Flight))
            modules.Set(index++, DynValue.NewString("flight"));
        if (component.Capabilities.HasFlag(CircuitCapability.Manipulator))
            modules.Set(index++, DynValue.NewString("manipulator"));
        if (component.Capabilities.HasFlag(CircuitCapability.Voice))
            modules.Set(index++, DynValue.NewString("voice"));
        if (component.Capabilities.HasFlag(CircuitCapability.Sensor))
            modules.Set(index++, DynValue.NewString("sensor"));
        if (component.Capabilities.HasFlag(CircuitCapability.Radio))
            modules.Set(index++, DynValue.NewString("radio"));
        if (component.Capabilities.HasFlag(CircuitCapability.Reagent))
            modules.Set(index++, DynValue.NewString("reagent"));
        if (component.Capabilities.HasFlag(CircuitCapability.BotanicalAnalyzer))
            modules.Set(index++, DynValue.NewString("botanical_analyzer"));
        if (component.Capabilities.HasFlag(CircuitCapability.ChemicalAnalyzer))
            modules.Set(index++, DynValue.NewString("chemical_analyzer"));
        if (component.Capabilities.HasFlag(CircuitCapability.MedicalAnalyzer))
            modules.Set(index++, DynValue.NewString("medical_analyzer"));

        table.Set("modules", DynValue.NewTable(modules));
        return DynValue.NewTable(table);
    }

    private DynValue BuildPosition(CircuitLuaRuntime runtime)
    {
        var xform = Transform(runtime.Assembly);
        return DynValue.NewTuple(
            DynValue.NewNumber(xform.Coordinates.Position.X),
            DynValue.NewNumber(xform.Coordinates.Position.Y));
    }

    private DynValue BuildPower(CircuitLuaRuntime runtime)
    {
        var (power, maxPower) = GetPower(runtime.Assembly);
        return DynValue.NewTuple(DynValue.NewNumber(power), DynValue.NewNumber(maxPower));
    }

    private DynValue BuildHealth(CircuitLuaRuntime runtime)
    {
        var (damage, maxHealth) = GetHealth(runtime.Assembly);
        var table = NewTable(runtime);
        table.Set("damage", DynValue.NewNumber(damage));
        table.Set("health", DynValue.NewNumber(MathF.Max(0f, maxHealth - damage)));
        table.Set("max_health", DynValue.NewNumber(maxHealth));
        table.Set("dead", DynValue.NewBoolean(_mobState.IsDead(runtime.Assembly)));
        return DynValue.NewTable(table);
    }

    private DynValue HasModule(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 0, out var module) ||
            !TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
        {
            return Bool(false, "has_module expects a module name");
        }

        var has = module.ToLowerInvariant() switch
        {
            "cpu" => component.Capabilities.HasFlag(CircuitCapability.Cpu),
            "power" or "cell" => component.Capabilities.HasFlag(CircuitCapability.Power),
            "locomotion" or "legs" or "tracks" or "wheels" or "thruster" =>
                component.Capabilities.HasFlag(CircuitCapability.Locomotion),
            "flight" or "flying" => component.Capabilities.HasFlag(CircuitCapability.Flight),
            "manipulator" or "hand" or "arm" => component.Capabilities.HasFlag(CircuitCapability.Manipulator),
            "voice" or "speaker" => component.Capabilities.HasFlag(CircuitCapability.Voice),
            "sensor" or "camera" => component.Capabilities.HasFlag(CircuitCapability.Sensor),
            "radio" or "transceiver" => component.Capabilities.HasFlag(CircuitCapability.Radio),
            "reagent" or "beaker" => component.Capabilities.HasFlag(CircuitCapability.Reagent),
            "botanical_analyzer" or "botany" => component.Capabilities.HasFlag(CircuitCapability.BotanicalAnalyzer),
            "chemical_analyzer" or "chem" => component.Capabilities.HasFlag(CircuitCapability.ChemicalAnalyzer),
            "medical_analyzer" or "medical" => component.Capabilities.HasFlag(CircuitCapability.MedicalAnalyzer),
            "gas_analyzer" or "gas" or "atmos" => component.Capabilities.HasFlag(CircuitCapability.GasAnalyzer),
            _ => false,
        };

        return DynValue.NewBoolean(has);
    }

    private DynValue Move(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetNumber(args, 0, out var dx) || !TryGetNumber(args, 1, out var dy))
            return Bool(false, "move expects dx, dy");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return Bool(false, "assembly is missing");

        if (!component.Powered)
            return Bool(false, "no power");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        return Bool(_movement.TryStartDirect(runtime.Assembly, new Vector2((float) dx, (float) dy), component));
    }

    private DynValue WalkTo(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetNumber(args, 0, out var x) || !TryGetNumber(args, 1, out var y))
            return Bool(false, "walk_to expects x, y");

        var range = args.Count > 2 && args[2].Type == DataType.Number ? (float) args[2].Number : 0.5f;
        var smooth = args.Count <= 3 || args[3].Type != DataType.Boolean || args[3].Boolean;

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return Bool(false, "assembly is missing");

        if (!component.Powered)
            return Bool(false, "no power");

        var xform = Transform(runtime.Assembly);
        var parent = xform.GridUid ?? xform.MapUid;
        if (parent == null)
            return Bool(false, "assembly is not on a grid or map");

        var target = new EntityCoordinates(parent.Value, new Vector2((float) x, (float) y));

        if (_timing.CurTime < runtime.NextWalk)
            return Bool(false, "walk cooldown");

        var result = _movement.TryWalkTo(runtime.Assembly, target, range, smooth, component);
        switch (result)
        {
            case CircuitWalkResult.Started:
                runtime.NextWalk = _timing.CurTime + TimeSpan.FromSeconds(0.25);
                return Bool(true);
            case CircuitWalkResult.AlreadyThere:
            case CircuitWalkResult.AlreadyPathing:
                return Bool(true);
            case CircuitWalkResult.NoPower:
                return Bool(false, "no power");
            default:
                return Bool(false, "cannot start walking");
        }
    }

    private DynValue Stop(CircuitLuaRuntime runtime)
    {
        _movement.Stop(runtime.Assembly);
        return Bool(true);
    }

    private DynValue WalkStatus(CircuitLuaRuntime runtime)
    {
        return DynValue.NewString(_movement.GetWalkStatus(runtime.Assembly));
    }

    private DynValue Scan(CircuitLuaRuntime runtime, CallbackArguments args, string? kind)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return Bool(false, "assembly is missing");

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
            return Bool(false, "no sensor module");

        if (!component.Powered)
            return Bool(false, "no power");

        var radius = args.Count > 0 && args[0].Type == DataType.Number
            ? (float) args[0].Number
            : component.SensorRange;

        radius = Math.Clamp(radius, 0.1f, component.SensorRange);

        if (!TryConsumeSensorQuery(runtime))
            return Bool(false, "scan cooldown");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var entities = ScanEntities(runtime, radius, kind, component.MaxScanResults > 0 ? component.MaxScanResults : 16);
        var table = NewTable(runtime);
        var index = 1;

        foreach (var (uid, distance) in entities)
        {
            var entry = NewTable(runtime);
            entry.Set("id", DynValue.NewNumber(runtime.GetHandle(uid)));
            entry.Set("name", DynValue.NewString(Name(uid)));
            entry.Set("kind", DynValue.NewString(GetKind(uid)));
            entry.Set("distance", DynValue.NewNumber(distance));

            var xform = Transform(uid);
            entry.Set("x", DynValue.NewNumber(xform.Coordinates.Position.X));
            entry.Set("y", DynValue.NewNumber(xform.Coordinates.Position.Y));

            table.Set(index++, DynValue.NewTable(entry));
        }

        return DynValue.NewTable(table);
    }

    private DynValue Find(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 0, out var name) || string.IsNullOrWhiteSpace(name))
            return LookupFailure("find expects a non-empty name");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return LookupFailure("assembly is missing");

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
            return LookupFailure("no sensor module");

        if (!component.Powered)
            return LookupFailure("no power");

        var radius = args.Count > 1 && args[1].Type == DataType.Number
            ? (float) args[1].Number
            : component.SensorRange;

        radius = Math.Clamp(radius, 0.1f, component.SensorRange);

        if (!TryConsumeSensorQuery(runtime))
            return LookupFailure("scan cooldown");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return LookupFailure("no power");

        if (!TryFindNearestInRange(runtime, radius, null, false, name, out var found))
            return LookupFailure("no matching entity in range");

        return DynValue.NewNumber(runtime.GetHandle(found));
    }

    /// <summary>
    /// Lookup functions return a handle, or nil plus a reason. The reason keeps a failed sensor
    /// or an empty scan distinguishable without mixing in a boolean first return.
    /// </summary>
    private static DynValue LookupFailure(string reason)
    {
        return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString(reason));
    }

    private DynValue Inspect(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Sensor))
        {
            return Bool(false, "no sensor module");
        }

        if (!component.Powered)
            return Bool(false, "no power");

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.SensorRange))
            return Bool(false, "target is out of sensor range or not visible");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Name(target)));
        table.Set("kind", DynValue.NewString(GetKind(target)));

        if (TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            table.Set("species", DynValue.NewString(humanoid.Species.ToString()));

        var (damage, maxHealth) = GetHealth(target);
        table.Set("damage", DynValue.NewNumber(damage));
        table.Set("health", DynValue.NewNumber(MathF.Max(0f, maxHealth - damage)));
        table.Set("max_health", DynValue.NewNumber(maxHealth));
        table.Set("dead", DynValue.NewBoolean(_mobState.IsDead(target)));

        if (_idCardSystem.TryFindIdCard(target, out var idCard))
        {
            var id = NewTable(runtime);
            id.Set("name", DynValue.NewString(idCard.Comp.FullName ?? Name(target)));
            id.Set("job", DynValue.NewString(idCard.Comp.LocalizedJobTitle));

            if (idCard.Comp.JobPrototype is { } jobProto)
                id.Set("job_id", DynValue.NewString(jobProto.Id));

            if (idCard.Comp.JobDepartments.Count > 0)
            {
                var departments = NewTable(runtime);
                var departmentIndex = 1;
                foreach (var department in idCard.Comp.JobDepartments)
                    departments.Set(departmentIndex++, DynValue.NewString(department.Id));
                id.Set("departments", DynValue.NewTable(departments));
            }

            if (_accessSystem.TryGetTags(idCard.Owner) is { } accessTags)
            {
                var access = NewTable(runtime);
                var accessIndex = 1;
                foreach (var tag in accessTags)
                    access.Set(accessIndex++, DynValue.NewString(tag.Id));
                id.Set("access", DynValue.NewTable(access));
            }

            table.Set("id", DynValue.NewTable(id));
        }

        var wearing = NewTable(runtime);
        if (_inventory.TryGetSlots(target, out var slots))
        {
            foreach (var slot in slots)
            {
                if (_inventory.TryGetSlotEntity(target, slot.Name, out var item))
                    wearing.Set(slot.Name, DynValue.NewString(Name(item.Value)));
            }
        }

        table.Set("wearing", DynValue.NewTable(wearing));

        var heldTable = NewTable(runtime);
        if (TryComp<HandsComponent>(target, out var hands))
        {
            var heldIndex = 1;
            foreach (var held in _hands.EnumerateHeld((target, hands)))
                heldTable.Set(heldIndex++, DynValue.NewString(Name(held)));
        }

        table.Set("held", DynValue.NewTable(heldTable));

        var components = NewTable(runtime);
        components.Set("mob", DynValue.NewBoolean(HasComp<MobStateComponent>(target)));
        components.Set("item", DynValue.NewBoolean(HasComp<ItemComponent>(target)));
        components.Set("door", DynValue.NewBoolean(HasComp<DoorComponent>(target)));
        components.Set("reagent", DynValue.NewBoolean(HasComp<SolutionContainerManagerComponent>(target)));
        table.Set("components", DynValue.NewTable(components));

        if (HasComp<DoorComponent>(target))
            table.Set("door", DynValue.NewTable(BuildDoorStatus(runtime, target)));

        return DynValue.NewTable(table);
    }

    private DynValue HasJob(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 1, out var job) || string.IsNullOrWhiteSpace(job))
            return Bool(false, "has_job expects a job id or title");

        if (!TryGetSensorTarget(runtime, args, out var target, out _, out var error))
            return Bool(false, error);

        if (_idCardSystem.TryFindIdCard(target, out var card))
        {
            if (card.Comp.JobPrototype is { } jobProto &&
                jobProto.Id.Equals(job, StringComparison.OrdinalIgnoreCase))
            {
                return Bool(true);
            }

            if (string.Equals(card.Comp.LocalizedJobTitle, job, StringComparison.OrdinalIgnoreCase))
                return Bool(true);
        }

        if (_wanted.TryGetJob(target, out var recordJob) &&
            recordJob.Id.Equals(job, StringComparison.OrdinalIgnoreCase))
        {
            return Bool(true);
        }

        return Bool(false);
    }

    private DynValue HasAccess(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 1, out var access) || string.IsNullOrWhiteSpace(access))
            return Bool(false, "has_access expects an access id");

        if (!TryGetSensorTarget(runtime, args, out var target, out _, out var error))
            return Bool(false, error);

        if (!_idCardSystem.TryFindIdCard(target, out var card))
            return Bool(false);

        if (_accessSystem.TryGetTags(card.Owner) is { } tags &&
            tags.Any(tag => tag.Id.Equals(access, StringComparison.OrdinalIgnoreCase)))
        {
            return Bool(true);
        }

        if (TryComp<AccessComponent>(card.Owner, out var accessComponent) &&
            accessComponent.Groups.Any(group => group.Id.Equals(access, StringComparison.OrdinalIgnoreCase)))
        {
            return Bool(true);
        }

        return Bool(false);
    }

    private DynValue WantedStatus(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetSensorTarget(runtime, args, out var target, out _, out var error))
            return Bool(false, error);

        if (!_wanted.TryGetCriminalRecord(target, out var record))
            return Bool(false, "no criminal record");

        var table = NewTable(runtime);
        table.Set("status", DynValue.NewString(record.Status.ToString()));
        table.Set("wanted", DynValue.NewBoolean(record.Status == SecurityStatus.Wanted));
        if (!string.IsNullOrWhiteSpace(record.Reason))
            table.Set("reason", DynValue.NewString(record.Reason));
        if (!string.IsNullOrWhiteSpace(record.InitiatorName))
            table.Set("initiator", DynValue.NewString(record.InitiatorName));

        var history = NewTable(runtime);
        var index = 1;
        foreach (var entry in record.History)
        {
            var item = NewTable(runtime);
            item.Set("crime", DynValue.NewString(entry.Crime));
            if (!string.IsNullOrWhiteSpace(entry.InitiatorName))
                item.Set("initiator", DynValue.NewString(entry.InitiatorName));
            history.Set(index++, DynValue.NewTable(item));
        }

        table.Set("history", DynValue.NewTable(history));
        return DynValue.NewTable(table);
    }

    private bool TryGetSensorTarget(
        CircuitLuaRuntime runtime,
        CallbackArguments args,
        out EntityUid target,
        out CircuitAssemblyComponent component,
        out string error)
    {
        target = default;
        component = null!;
        error = string.Empty;

        if (!TryGetHandle(runtime, args, 0, out target))
        {
            error = "invalid entity handle";
            return false;
        }

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var assemblyComponent))
        {
            error = "assembly is missing";
            return false;
        }

        component = assemblyComponent;

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
        {
            error = "no sensor module";
            return false;
        }

        if (!component.Powered)
        {
            error = "no power";
            return false;
        }

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.SensorRange))
        {
            error = "target is out of sensor range or not visible";
            return false;
        }

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
        {
            error = "no power";
            return false;
        }

        return true;
    }

    private DynValue CanSee(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target) ||
            !TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Sensor))
        {
            return DynValue.NewBoolean(false);
        }

        return DynValue.NewBoolean(_interaction.InRangeUnobstructed(runtime.Assembly, target, component.SensorRange));
    }

    private DynValue CanHear(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target) ||
            !TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Sensor))
        {
            return DynValue.NewBoolean(false);
        }

        var origin = _transform.GetMapCoordinates(runtime.Assembly);
        var other = _transform.GetMapCoordinates(target);
        return DynValue.NewBoolean(origin.MapId == other.MapId &&
                                   (origin.Position - other.Position).Length() <= component.SensorRange);
    }

    private DynValue Nearest(CircuitLuaRuntime runtime, CallbackArguments args, string kind)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return LookupFailure("assembly is missing");

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
            return LookupFailure("no sensor module");

        if (!component.Powered)
            return LookupFailure("no power");

        var radius = args.Count > 0 && args[0].Type == DataType.Number
            ? (float) args[0].Number
            : component.SensorRange;

        radius = Math.Clamp(radius, 0.1f, component.SensorRange);

        if (!TryConsumeSensorQuery(runtime))
            return LookupFailure("scan cooldown");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return LookupFailure("no power");

        if (!TryFindNearestInRange(runtime, radius, kind, false, null, out var nearest))
            return LookupFailure("no matching entity in range");

        return DynValue.NewNumber(runtime.GetHandle(nearest));
    }

    private DynValue NearestPerson(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return LookupFailure("assembly is missing");

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
            return LookupFailure("no sensor module");

        if (!component.Powered)
            return LookupFailure("no power");

        var radius = args.Count > 0 && args[0].Type == DataType.Number
            ? (float) args[0].Number
            : component.SensorRange;

        radius = Math.Clamp(radius, 0.1f, component.SensorRange);

        if (!TryConsumeSensorQuery(runtime))
            return LookupFailure("scan cooldown");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return LookupFailure("no power");

        if (!TryFindNearestInRange(runtime, radius, "mob", true, null, out var person))
            return LookupFailure("no matching person in range");

        return DynValue.NewNumber(runtime.GetHandle(person));
    }

    private DynValue Distance(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        var origin = _transform.GetMapCoordinates(runtime.Assembly);
        var other = _transform.GetMapCoordinates(target);
        if (origin.MapId != other.MapId)
            return DynValue.NewNumber(-1);

        return DynValue.NewNumber((origin.Position - other.Position).Length());
    }

    private DynValue PositionOf(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        var self = Transform(runtime.Assembly);
        var other = Transform(target);
        if (self.GridUid != other.GridUid || self.MapUid != other.MapUid)
            return Bool(false, "target is on a different grid");

        return DynValue.NewTuple(
            DynValue.NewNumber(other.Coordinates.Position.X),
            DynValue.NewNumber(other.Coordinates.Position.Y));
    }

    private DynValue CanReach(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return DynValue.NewBoolean(false);

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return DynValue.NewBoolean(false);

        return DynValue.NewBoolean(
            _interaction.InRangeUnobstructed(runtime.Assembly, target, component.HandRange));
    }

    private DynValue GetTileRelative(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        var dx = args.Count > 0 && args[0].Type == DataType.Number ? (float) args[0].Number : 0f;
        var dy = args.Count > 1 && args[1].Type == DataType.Number ? (float) args[1].Number : 0f;

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Sensor) ||
            !component.Powered)
        {
            return Bool(false, "no powered sensor module");
        }

        var xform = Transform(runtime.Assembly);
        var coords = xform.Coordinates.Offset(new Vector2(dx, dy));
        if ((xform.Coordinates.Position - coords.Position).Length() > component.SensorRange)
            return Bool(false, "tile is outside sensor range");

        var table = NewTable(runtime);
        table.Set("x", DynValue.NewNumber(coords.Position.X));
        table.Set("y", DynValue.NewNumber(coords.Position.Y));
        table.Set("entities", DynValue.NewTable(BuildTileEntities(runtime, coords, component)));

        return DynValue.NewTable(table);
    }

    private DynValue EntitiesOnTile(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        var dx = args.Count > 0 && args[0].Type == DataType.Number ? (float) args[0].Number : 0f;
        var dy = args.Count > 1 && args[1].Type == DataType.Number ? (float) args[1].Number : 0f;

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Sensor) ||
            !component.Powered)
        {
            return Bool(false, "no powered sensor module");
        }

        var xform = Transform(runtime.Assembly);
        var coords = xform.Coordinates.Offset(new Vector2(dx, dy));
        if ((xform.Coordinates.Position - coords.Position).Length() > component.SensorRange)
            return Bool(false, "tile is outside sensor range");

        return DynValue.NewTable(BuildTileEntities(runtime, coords, component));
    }

    private DynValue Decals(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Sensor) ||
            !component.Powered)
        {
            return Bool(false, "no powered sensor module");
        }

        var xform = Transform(runtime.Assembly);
        if (xform.GridUid is not { } grid)
            return Bool(false, "assembly is not on a grid");

        var radius = args.Count > 0 && args[0].Type == DataType.Number
            ? (float) args[0].Number
            : 0.75f;

        radius = Math.Clamp(radius, 0.1f, component.SensorRange);

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var found = _decals.GetDecalsInRange(
            grid,
            xform.Coordinates.Position,
            radius,
            decal => decal.Cleanable);

        var table = NewTable(runtime);
        var index = 1;
        foreach (var (decalId, decal) in found)
        {
            var entry = NewTable(runtime);
            entry.Set("decal_id", DynValue.NewNumber(decalId));
            entry.Set("decal", DynValue.NewString(decal.Id));
            entry.Set("x", DynValue.NewNumber(decal.Coordinates.X));
            entry.Set("y", DynValue.NewNumber(decal.Coordinates.Y));
            entry.Set("cleanable", DynValue.NewBoolean(true));
            table.Set(index++, DynValue.NewTable(entry));
        }

        return DynValue.NewTable(table);
    }

    private DynValue Puddles(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return Bool(false, "assembly is missing");

        if (!component.Capabilities.HasFlag(CircuitCapability.ChemicalAnalyzer))
            return Bool(false, "no chemical analyzer module");

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
            return Bool(false, "no sensor module");

        if (!component.Powered)
            return Bool(false, "no power");

        if (!TryConsumeSensorQuery(runtime))
            return Bool(false, "scan cooldown");

        var radius = args.Count > 0 && args[0].Type == DataType.Number
            ? (float) args[0].Number
            : 1f;

        radius = Math.Clamp(radius, 0.1f, component.SensorRange);

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var found = new List<(EntityUid Uid, float Distance)>();
        var origin = _transform.GetMapCoordinates(runtime.Assembly);
        _lookupSet.Clear();
        _lookup.GetEntitiesInRange(runtime.Assembly, radius, _lookupSet, LookupFlags.Uncontained);

        foreach (var uid in _lookupSet)
        {
            if (uid == runtime.Assembly || Deleted(uid) || !HasComp<PuddleComponent>(uid))
                continue;

            if (!_interaction.InRangeUnobstructed(runtime.Assembly, uid, radius))
                continue;

            var other = _transform.GetMapCoordinates(uid);
            if (origin.MapId != other.MapId)
                continue;

            found.Add((uid, (origin.Position - other.Position).Length()));
        }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var max = component.MaxScanResults > 0 ? component.MaxScanResults : 16;
        if (found.Count > max)
            found.RemoveRange(max, found.Count - max);

        var table = NewTable(runtime);
        var index = 1;
        foreach (var (puddle, distance) in found)
        {
            if (!TryComp<PuddleComponent>(puddle, out var puddleComponent) ||
                !TryComp<SolutionContainerManagerComponent>(puddle, out var manager) ||
                !_solutions.TryGetSolution((puddle, manager), puddleComponent.SolutionName, out _, out var solution))
            {
                continue;
            }

            var entry = BuildSolutionTable(runtime, null, solution);
            entry.Set("id", DynValue.NewNumber(runtime.GetHandle(puddle)));
            entry.Set("name", DynValue.NewString(Name(puddle)));
            entry.Set("x", DynValue.NewNumber(Transform(puddle).Coordinates.Position.X));
            entry.Set("y", DynValue.NewNumber(Transform(puddle).Coordinates.Position.Y));
            entry.Set("distance", DynValue.NewNumber(distance));
            entry.Set("solution", DynValue.NewString(puddleComponent.SolutionName));
            table.Set(index++, DynValue.NewTable(entry));
        }

        return DynValue.NewTable(table);
    }

    private DynValue DoorStatus(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!HasComp<DoorComponent>(target))
            return Bool(false, "target is not a door");

        return DynValue.NewTable(BuildDoorStatus(runtime, target));
    }

    private DynValue CanOpen(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return DynValue.NewBoolean(false);

        if (!HasComp<DoorComponent>(target))
            return DynValue.NewBoolean(false);

        var status = BuildDoorStatus(runtime, target);
        return status.Get("can_open");
    }

    private Table BuildDoorStatus(CircuitLuaRuntime runtime, EntityUid target)
    {
        var table = NewTable(runtime);

        var state = DoorState.Closed;
        if (_appearance.TryGetData<DoorState>(target, DoorVisuals.State, out var doorState))
            state = doorState;

        var bolted = TryComp<DoorBoltComponent>(target, out var bolts) && bolts.BoltsDown;
        var powered = !TryComp<AirlockComponent>(target, out var airlock) || airlock.Powered;
        var fireLocked = TryComp<FirelockComponent>(target, out var firelock) && firelock.IsLocked;
        var access = _access.IsAllowed(runtime.Assembly, target);

        table.Set("state", DynValue.NewString(state.ToString()));
        table.Set("bolted", DynValue.NewBoolean(bolted));
        table.Set("powered", DynValue.NewBoolean(powered));
        table.Set("fire_locked", DynValue.NewBoolean(fireLocked));
        table.Set("access", DynValue.NewBoolean(access));
        table.Set("can_open",
            DynValue.NewBoolean(state == DoorState.Open || (!bolted && powered && !fireLocked && access)));

        return table;
    }

    private Table BuildTileEntities(
        CircuitLuaRuntime runtime,
        EntityCoordinates coordinates,
        CircuitAssemblyComponent component)
    {
        var result = NewTable(runtime);
        _lookupSet.Clear();
        _lookup.GetEntitiesInRange(coordinates, 0.6f, _lookupSet, LookupFlags.Uncontained);

        var index = 1;
        foreach (var uid in _lookupSet)
        {
            if (uid == runtime.Assembly || Deleted(uid))
                continue;

            if (!_interaction.InRangeUnobstructed(runtime.Assembly, uid, component.SensorRange))
                continue;

            var entry = BuildEntityEntry(runtime, uid);
            result.Set(index++, DynValue.NewTable(entry));
        }

        return result;
    }

    private Table BuildHandleEntry(CircuitLuaRuntime runtime, EntityUid uid)
    {
        var entry = NewTable(runtime);
        entry.Set("id", DynValue.NewNumber(runtime.GetHandle(uid)));
        entry.Set("name", DynValue.NewString(Name(uid)));
        return entry;
    }

    private Table BuildEntityEntry(CircuitLuaRuntime runtime, EntityUid uid)
    {
        var entry = BuildHandleEntry(runtime, uid);
        entry.Set("kind", DynValue.NewString(GetKind(uid)));

        var xform = Transform(uid);
        entry.Set("x", DynValue.NewNumber(xform.Coordinates.Position.X));
        entry.Set("y", DynValue.NewNumber(xform.Coordinates.Position.Y));
        return entry;
    }

    private DynValue PollEvent(CircuitLuaRuntime runtime)
    {
        if (!runtime.Events.TryDequeue(out var ev))
            return DynValue.NewNil();

        var table = NewTable(runtime);
        table.Set("type", DynValue.NewString(ev.Type));

        if (ev.Source.IsValid() && Exists(ev.Source))
        {
            table.Set("speaker", DynValue.NewNumber(runtime.GetHandle(ev.Source)));
            table.Set("name", DynValue.NewString(Name(ev.Source)));

            if (TryGetIdInfo(ev.Source, out var idName, out var job))
            {
                if (idName != null)
                    table.Set("id_name", DynValue.NewString(idName));
                if (job != null)
                    table.Set("job", DynValue.NewString(job));
            }
        }

        table.Set("message", DynValue.NewString(ev.Message));
        if (ev.Channel != null)
            table.Set("channel", DynValue.NewString(ev.Channel));
        if (ev.Port != null)
            table.Set("port", DynValue.NewString(ev.Port));
        if (ev.State != null)
            table.Set("state", DynValue.NewBoolean(ev.State.Value));
        if (ev.Emote != null)
            table.Set("emote", DynValue.NewString(ev.Emote));
        if (ev.Item is { } item && Exists(item))
        {
            table.Set("item", DynValue.NewNumber(runtime.GetHandle(item)));
            table.Set("item_name", DynValue.NewString(Name(item)));
        }
        if (ev.Damage is { Count: > 0 } damage)
        {
            var damageTable = NewTable(runtime);
            var total = 0f;
            foreach (var (type, amount) in damage)
            {
                damageTable.Set(type, DynValue.NewNumber(amount));
                total += amount;
            }
            table.Set("damage", DynValue.NewTable(damageTable));
            table.Set("damage_total", DynValue.NewNumber(total));
        }

        return DynValue.NewTable(table);
    }

    private DynValue Say(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 0, out var message))
            return Bool(false, "say expects text");

        message = message.Trim();
        if (message.Length == 0 || message.Length > 200)
            return Bool(false, "say text must be 1-200 characters");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Voice))
        {
            return Bool(false, "no speaker module");
        }

        if (!component.Powered)
            return Bool(false, "no power");

        if (_timing.CurTime < runtime.NextChat)
            return Bool(false, "speech rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextChat = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        _chat.TrySendInGameICMessage(
            runtime.Assembly,
            message,
            InGameICChatType.Speak,
            hideChat: false,
            hideLog: false,
            shell: null,
            player: null,
            nameOverride: null,
            checkRadioPrefix: false,
            ignoreActionBlocker: false);

        return Bool(true);
    }

    /// <summary>
    /// Lists the channels granted by encryption keys installed in the radio module.
    /// </summary>
    private DynValue RadioChannels(CircuitLuaRuntime runtime)
    {
        if (!TryGetRadioModule(runtime, out _, out _, out var keys, out var error))
            return Bool(false, error);

        var table = NewTable(runtime);
        var index = 1;

        foreach (var id in keys.Channels.OrderBy(channel => channel, StringComparer.Ordinal))
        {
            if (!_proto.TryIndex<RadioChannelPrototype>(id, out var channel))
                continue;

            var entry = NewTable(runtime);
            entry.Set("id", DynValue.NewString(channel.ID));
            entry.Set("name", DynValue.NewString(channel.LocalizedName));
            entry.Set("key", DynValue.NewString(channel.KeyCode == '\0'
                ? string.Empty
                : channel.KeyCode.ToString()));
            entry.Set("frequency", DynValue.NewNumber(channel.Frequency));
            entry.Set("default", DynValue.NewBoolean(keys.DefaultChannel == id));
            table.Set(index++, DynValue.NewTable(entry));
        }

        return DynValue.NewTable(table);
    }

    /// <summary>
    /// Sends a radio message. The channel must be provided by an installed encryption key.
    /// The channel may be a channel id, a keycode (such as ";" or ":c"), or a localized name.
    /// With no channel argument, the key holder's default channel is used.
    /// </summary>
    private DynValue Radio(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 0, out var message))
            return Bool(false, "radio expects text");

        message = message.Trim();
        if (message.Length == 0 || message.Length > 200)
            return Bool(false, "radio text must be 1-200 characters");

        if (!TryGetRadioModule(runtime, out var component, out var module, out var keys, out var error))
            return Bool(false, error);

        if (!component.Powered)
            return Bool(false, "no power");

        if (keys.Channels.Count == 0)
            return Bool(false, "radio has no encryption keys");

        string? requested = null;
        if (args.Count > 1 && args[1].Type == DataType.String)
            requested = args[1].String;

        if (!TryResolveRadioChannel(keys, requested, out var channel, out error))
            return Bool(false, error);

        if (_timing.CurTime < runtime.NextRadio)
            return Bool(false, "radio rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextRadio = _timing.CurTime + TimeSpan.FromSeconds(1);
        _radio.SendRadioMessage(runtime.Assembly, message, channel, module, frequency: channel.Frequency);
        return Bool(true);
    }

    private bool TryGetRadioModule(
        CircuitLuaRuntime runtime,
        out CircuitAssemblyComponent component,
        out EntityUid module,
        out EncryptionKeyHolderComponent keys,
        out string error)
    {
        component = default!;
        module = default;
        keys = default!;
        error = string.Empty;

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var assemblyComponent))
        {
            error = "assembly is missing";
            return false;
        }

        component = assemblyComponent;

        if (!component.Capabilities.HasFlag(CircuitCapability.Radio) ||
            component.Radio is not { } radioModule)
        {
            error = "no radio module";
            return false;
        }

        module = radioModule;

        if (!TryComp<EncryptionKeyHolderComponent>(radioModule, out var keyHolder))
        {
            error = "radio module has no encryption key holder";
            return false;
        }

        keys = keyHolder;
        return true;
    }

    private bool TryResolveRadioChannel(
        EncryptionKeyHolderComponent keys,
        string? requested,
        out RadioChannelPrototype channel,
        out string error)
    {
        channel = default!;
        error = string.Empty;

        if (keys.Channels.Count == 0)
        {
            error = "radio has no encryption keys";
            return false;
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            var defaultId = keys.DefaultChannel is { } configured && keys.Channels.Contains(configured)
                ? configured
                : keys.Channels.OrderBy(id => id, StringComparer.Ordinal).First();

            if (!_proto.TryIndex<RadioChannelPrototype>(defaultId, out var defaultChannel))
            {
                error = "default radio channel is invalid";
                return false;
            }

            channel = defaultChannel;
            return true;
        }

        var wanted = requested.Trim();
        var keyCode = wanted.StartsWith(':') ? wanted[1..] : wanted;
        var keyChar = keyCode.Length == 1 ? keyCode[0] : '\0';

        foreach (var id in keys.Channels)
        {
            if (!_proto.TryIndex<RadioChannelPrototype>(id, out var candidate))
                continue;

            var matchesId = string.Equals(candidate.ID, wanted, StringComparison.OrdinalIgnoreCase);
            var matchesName = string.Equals(candidate.LocalizedName, wanted, StringComparison.OrdinalIgnoreCase);
            var matchesKey = keyChar != '\0' &&
                             candidate.KeyCode != '\0' &&
                             char.ToLowerInvariant(candidate.KeyCode) == char.ToLowerInvariant(keyChar);

            if (!matchesId && !matchesName && !matchesKey)
                continue;

            channel = candidate;
            return true;
        }

        error = $"channel '{wanted}' is not available from the installed encryption keys";
        return false;
    }

    private DynValue Pickup(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!CanManipulate(runtime, target, out var component, out var error))
            return Bool(false, error);

        if (!TryComp<ItemComponent>(target, out _))
            return Bool(false, "target is not an item");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        return Bool(_hands.TryPickupAnyHand(runtime.Assembly, target, checkActionBlocker: true, animateUser: false));
    }

    private DynValue Drop(CircuitLuaRuntime runtime)
    {
        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Manipulator))
        {
            return Bool(false, "no manipulator module");
        }

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        return Bool(_hands.TryDrop(runtime.Assembly, checkActionBlocker: true));
    }

    private DynValue Held(CircuitLuaRuntime runtime)
    {
        var table = NewTable(runtime);
        var index = 1;

        if (TryComp<HandsComponent>(runtime.Assembly, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((runtime.Assembly, hands)))
            {
                var entry = NewTable(runtime);
                entry.Set("id", DynValue.NewNumber(runtime.GetHandle(held)));
                entry.Set("name", DynValue.NewString(Name(held)));
                table.Set(index++, DynValue.NewTable(entry));
            }
        }

        return DynValue.NewTable(table);
    }

    private DynValue Solution(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!TryComp<SolutionContainerManagerComponent>(target, out var manager))
            return Bool(false, "target has no solution containers");

        string? name = args.Count > 1 && args[1].Type == DataType.String
            ? args[1].String
            : null;

        if (string.IsNullOrWhiteSpace(name))
            name = null;

        if (name == null)
        {
            if (TryComp<InjectorComponent>(target, out var injector))
                name = injector.SolutionName;
            else if (TryComp<RefillableSolutionComponent>(target, out var refillable))
                name = refillable.Solution;
            else if (TryComp<DrainableSolutionComponent>(target, out var drainable))
                name = drainable.Solution;
            else
            {
                var names = _solutions.EnumerateSolutions((target, manager))
                    .Where(entry => entry.Name != null)
                    .Select(entry => entry.Name!)
                    .ToList();

                if (names.Count == 1)
                    name = names[0];
            }
        }

        if (name == null)
            return Bool(false, "multiple solutions; specify a solution name");

        if (!_solutions.TryGetSolution((target, manager), name, out _, out var solution))
            return Bool(false, "solution not found");

        var table = BuildSolutionTable(runtime, 0, solution);
        table.Set("name", DynValue.NewString(name));
        return DynValue.NewTable(table);
    }

    private DynValue ItemStatus(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Name(target)));
        table.Set("kind", DynValue.NewString(GetKind(target)));

        if (_appearance.TryGetData<bool>(target, ToggleableVisuals.Enabled, out var activated))
            table.Set("activated", DynValue.NewBoolean(activated));

        if (TryComp<InjectorComponent>(target, out var injector))
        {
            var injectorTable = NewTable(runtime);
            injectorTable.Set("mode", DynValue.NewString(injector.ToggleState.ToString()));
            injectorTable.Set("transfer_amount", DynValue.NewNumber(injector.TransferAmount.Float()));
            table.Set("injector", DynValue.NewTable(injectorTable));
        }

        if (TryComp<SolutionContainerManagerComponent>(target, out var manager))
        {
            var solutions = NewTable(runtime);
            var index = 1;
            foreach (var (name, solutionEntity) in _solutions.EnumerateSolutions((target, manager)))
            {
                if (name == null)
                    continue;

                var entry = BuildSolutionTable(runtime, 0, solutionEntity.Comp.Solution);
                entry.Set("name", DynValue.NewString(name));
                solutions.Set(index++, DynValue.NewTable(entry));
            }

            table.Set("solutions", DynValue.NewTable(solutions));
        }

        return DynValue.NewTable(table);
    }

    private DynValue Interact(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Manipulator) ||
            !component.Powered)
        {
            return Bool(false, "a powered manipulator is required");
        }

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.HandRange))
            return Bool(false, "target is out of reach or not visible");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var ev = new InteractHandEvent(runtime.Assembly, target);
        RaiseLocalEvent(target, ev);
        return Bool(ev.Handled);
    }

    private DynValue AnalyzePlant(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryAnalyzerTarget(runtime, args, CircuitCapability.BotanicalAnalyzer, "botanical analyzer",
                out var target, out _, out var error))
        {
            return Bool(false, error);
        }

        if (TryComp<PlantHolderComponent>(target, out var holder))
        {
            if (holder.Seed == null)
                return Bool(false, "plant tray is empty");

            var table = NewTable(runtime);
            table.Set("name", DynValue.NewString(Name(target)));
            table.Set("is_seed", DynValue.NewBoolean(false));
            table.Set("dead", DynValue.NewBoolean(holder.Dead));
            table.Set("health", DynValue.NewNumber(holder.Health));
            table.Set("water", DynValue.NewNumber(holder.WaterLevel));
            table.Set("nutrition", DynValue.NewNumber(holder.NutritionLevel));
            table.Set("toxins", DynValue.NewNumber(holder.Toxins));
            table.Set("pests", DynValue.NewNumber(holder.PestLevel));
            table.Set("weeds", DynValue.NewNumber(holder.WeedLevel));
            table.Set("age", DynValue.NewNumber(holder.Age));
            table.Set("harvest_ready", DynValue.NewBoolean(holder.Harvest));
            table.Set("mutation_level", DynValue.NewNumber(holder.MutationLevel));
            table.Set("improper_heat", DynValue.NewBoolean(holder.ImproperHeat));
            table.Set("improper_pressure", DynValue.NewBoolean(holder.ImproperPressure));
            table.Set("improper_light", DynValue.NewBoolean(holder.ImproperLight));
            table.Set("seed", DynValue.NewTable(BuildSeedTable(runtime, holder.Seed)));
            return DynValue.NewTable(table);
        }

        if (TryComp<SeedComponent>(target, out var seedComponent) &&
            _botany.TryGetSeed(seedComponent, out var seed))
        {
            var table = NewTable(runtime);
            table.Set("name", DynValue.NewString(Name(target)));
            table.Set("is_seed", DynValue.NewBoolean(true));
            table.Set("seed", DynValue.NewTable(BuildSeedTable(runtime, seed)));
            return DynValue.NewTable(table);
        }

        return Bool(false, "target is not a plant or seed");
    }

    private Table BuildSeedTable(CircuitLuaRuntime runtime, SeedData seed)
    {
        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Loc.GetString(seed.Name)));
        table.Set("display_name", DynValue.NewString(Loc.GetString(seed.DisplayName)));
        table.Set("yield", DynValue.NewNumber(seed.Yield));
        table.Set("potency", DynValue.NewNumber(seed.Potency));
        table.Set("endurance", DynValue.NewNumber(seed.Endurance));
        table.Set("lifespan", DynValue.NewNumber(seed.Lifespan));
        table.Set("maturation", DynValue.NewNumber(seed.Maturation));
        table.Set("production", DynValue.NewNumber(seed.Production));
        table.Set("growth_stages", DynValue.NewNumber(seed.GrowthStages));
        table.Set("nutrient_consumption", DynValue.NewNumber(seed.NutrientConsumption));
        table.Set("water_consumption", DynValue.NewNumber(seed.WaterConsumption));
        table.Set("ideal_heat", DynValue.NewNumber(seed.IdealHeat));
        table.Set("heat_tolerance", DynValue.NewNumber(seed.HeatTolerance));
        table.Set("ideal_light", DynValue.NewNumber(seed.IdealLight));
        table.Set("light_tolerance", DynValue.NewNumber(seed.LightTolerance));
        table.Set("toxins_tolerance", DynValue.NewNumber(seed.ToxinsTolerance));
        table.Set("low_pressure_tolerance", DynValue.NewNumber(seed.LowPressureTolerance));
        table.Set("high_pressure_tolerance", DynValue.NewNumber(seed.HighPressureTolerance));
        table.Set("pest_tolerance", DynValue.NewNumber(seed.PestTolerance));
        table.Set("weed_tolerance", DynValue.NewNumber(seed.WeedTolerance));
        table.Set("weed_high_level_threshold", DynValue.NewNumber(seed.WeedHighLevelThreshold));
        table.Set("viable", DynValue.NewBoolean(seed.Viable));
        table.Set("seedless", DynValue.NewBoolean(seed.Seedless || seed.PermanentlySeedless));

        var chemicals = NewTable(runtime);
        var index = 1;
        foreach (var chemical in seed.Chemicals.Keys)
            chemicals.Set(index++, DynValue.NewString(chemical));
        table.Set("chemicals", DynValue.NewTable(chemicals));

        table.Set("exude_gases", DynValue.NewTable(BuildStringList(runtime, seed.ExudeGasses.Keys.Select(gas => gas.ToString()))));
        table.Set("consume_gases", DynValue.NewTable(BuildStringList(runtime, seed.ConsumeGasses.Keys.Select(gas => gas.ToString()))));
        table.Set("mutation_prototypes", DynValue.NewTable(BuildStringList(runtime, seed.MutationPrototypes.Select(proto => proto.ToString()))));
        return table;
    }

    private DynValue Dispense(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!CanManipulate(runtime, target, out var component, out var error))
            return Bool(false, error);

        if (!TryComp<ReagentDispenserComponent>(target, out _) ||
            !TryComp<StorageComponent>(target, out var storage))
        {
            return Bool(false, "target is not a reagent dispenser");
        }

        if (storage.StoredItems.Count == 0)
            return Bool(false, "dispenser has no reagent containers");

        EntityUid? source = null;

        if (args.Count > 1 && args[1].Type == DataType.String)
        {
            var wanted = args[1].String;
            foreach (var stored in storage.StoredItems.Keys)
            {
                if (Name(stored).Contains(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    source = stored;
                    break;
                }

                if (_solutions.TryGetDrainableSolution(stored, out _, out var storedSolution) &&
                    storedSolution.Contents.Any(reagent =>
                        reagent.Reagent.Prototype.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
                {
                    source = stored;
                    break;
                }
            }
        }
        else if (args.Count > 1 && args[1].Type == DataType.Number)
        {
            var index = (int) args[1].Number;
            if (index >= 1 && index <= storage.StoredItems.Count)
                source = storage.StoredItems.Keys.ElementAt(index - 1);
        }
        else
        {
            source = storage.StoredItems.Keys.FirstOrDefault();
        }

        if (source is not { } sourceContainer || !sourceContainer.IsValid())
            return Bool(false, "no matching reagent container");

        if (!_solutions.TryGetDrainableSolution(sourceContainer, out _, out var sourceSolution))
            return Bool(false, "reagent container is not drainable");

        var beakerSlot = GetOptionalIndex(args, 3, 1);
        if (!TryGetReagentSolution(runtime, beakerSlot, out var destination, out _, out var beakerError))
            return Bool(false, beakerError);

        var amount = args.Count > 2 && args[2].Type == DataType.Number
            ? (float) args[2].Number
            : 10f;

        if (amount <= 0)
            return Bool(false, "dispense amount must be positive");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        return Bool(_solutions.TryTransferSolution(
            destination,
            sourceSolution,
            FixedPoint2.New(Math.Min(amount, 1000f))));
    }

    private DynValue AnalyzeChemicals(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryAnalyzerTarget(runtime, args, CircuitCapability.ChemicalAnalyzer, "chemical analyzer",
                out var target, out _, out var error))
        {
            return Bool(false, error);
        }

        if (!TryComp<SolutionContainerManagerComponent>(target, out var manager))
            return Bool(false, "target has no solution containers");

        var solutions = NewTable(runtime);
        var index = 1;
        foreach (var (name, solutionEntity) in _solutions.EnumerateSolutions((target, manager)))
        {
            var entry = BuildSolutionTable(runtime, 0, solutionEntity.Comp.Solution);
            entry.Set("name", DynValue.NewString(name ?? "default"));
            solutions.Set(index++, DynValue.NewTable(entry));
        }

        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Name(target)));
        table.Set("solutions", DynValue.NewTable(solutions));
        return DynValue.NewTable(table);
    }

    private DynValue ChemMasterStatus(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetChemMaster(runtime, args, out var target, out var chemMaster, out _, out var error))
            return Bool(false, error);

        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Name(target)));
        table.Set("mode", DynValue.NewString(chemMaster.Mode == ChemMasterMode.Transfer ? "transfer" : "discard"));
        table.Set("sorting", DynValue.NewString(chemMaster.SortingType switch
        {
            ChemMasterSortingType.Alphabetical => "alphabetical",
            ChemMasterSortingType.Quantity => "quantity",
            ChemMasterSortingType.Latest => "latest",
            _ => "none",
        }));
        table.Set("pill_type", DynValue.NewNumber(chemMaster.PillType));
        table.Set("pill_dosage_limit", DynValue.NewNumber(chemMaster.PillDosageLimit));

        if (_solutions.TryGetSolution(target, SharedChemMaster.BufferSolutionName, out _, out var buffer))
        {
            var bufferTable = BuildSolutionTable(runtime, null, buffer);
            bufferTable.Set("name", DynValue.NewString(SharedChemMaster.BufferSolutionName));
            table.Set("buffer", DynValue.NewTable(bufferTable));
        }

        var input = _itemSlots.GetItemOrNull(target, SharedChemMaster.InputSlotName);
        if (input is { } inputUid &&
            TryComp<FitsInDispenserComponent>(inputUid, out var fits) &&
            _solutions.TryGetSolution(inputUid, fits.Solution, out _, out var inputSolution))
        {
            var inputTable = BuildSolutionTable(runtime, null, inputSolution);
            inputTable.Set("name", DynValue.NewString(Name(inputUid)));
            table.Set("input", DynValue.NewTable(inputTable));
        }

        var output = _itemSlots.GetItemOrNull(target, SharedChemMaster.OutputSlotName);
        if (output is { } outputUid)
        {
            table.Set("output_name", DynValue.NewString(Name(outputUid)));

            if (_solutions.TryGetSolution(outputUid, SharedChemMaster.BottleSolutionName, out _, out var outputSolution))
            {
                var outputTable = BuildSolutionTable(runtime, null, outputSolution);
                outputTable.Set("name", DynValue.NewString(Name(outputUid)));
                table.Set("output", DynValue.NewTable(outputTable));
            }

            if (TryComp<StorageComponent>(outputUid, out var storage))
                table.Set("output_items", DynValue.NewNumber(storage.Container.ContainedEntities.Count));
        }

        return DynValue.NewTable(table);
    }

    private DynValue ChemMasterSetMode(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 1, out var mode))
            return Bool(false, "chem_master_set_mode expects transfer or discard");

        if (!TryGetChemMaster(runtime, args, out var target, out _, out _, out var error))
            return Bool(false, error);

        var parsed = mode.ToLowerInvariant() switch
        {
            "transfer" => ChemMasterMode.Transfer,
            "discard" => ChemMasterMode.Discard,
            _ => (ChemMasterMode?) null,
        };

        if (parsed is not { } value)
            return Bool(false, "chem_master_set_mode expects transfer or discard");

        RaiseChemMasterMessage(runtime, target, new ChemMasterSetModeMessage(value));
        return Bool(true);
    }

    private DynValue ChemMasterSetPillType(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetNumber(args, 1, out var pillType) ||
            pillType < 0 || pillType >= SharedChemMaster.PillTypes)
        {
            return Bool(false, $"pill type must be between 0 and {SharedChemMaster.PillTypes - 1}");
        }

        if (!TryGetChemMaster(runtime, args, out var target, out _, out _, out var error))
            return Bool(false, error);

        RaiseChemMasterMessage(runtime, target, new ChemMasterSetPillTypeMessage((uint) pillType));
        return Bool(true);
    }

    private DynValue ChemMasterTransfer(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetString(args, 1, out var reagent))
            return Bool(false, "chem_master_transfer expects a reagent id");

        if (!TryGetChemMasterAmount(args, 2, out var amount))
            return Bool(false, "amount must be 1, 5, 10, 15, 20, 25, 30, 50, 100 or all");

        var fromBuffer = args.Count > 3 && args[3].Type == DataType.Boolean && args[3].Boolean;

        if (!_proto.HasIndex<ReagentPrototype>(reagent))
            return Bool(false, $"unknown reagent '{reagent}'");

        if (!TryGetChemMaster(runtime, args, out var target, out _, out var component, out var error))
            return Bool(false, error);

        if (_timing.CurTime < runtime.NextTransfer)
            return Bool(false, "transfer rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextTransfer = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        RaiseChemMasterMessage(runtime, target,
            new ChemMasterReagentAmountButtonMessage(new ReagentId(reagent, null), amount, fromBuffer));
        return Bool(true);
    }

    private DynValue ChemMasterCreatePills(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetNumber(args, 1, out var dosage) || !TryGetNumber(args, 2, out var number) ||
            dosage <= 0 || number <= 0 || dosage > uint.MaxValue || number > uint.MaxValue)
        {
            return Bool(false, "chem_master_create_pills expects positive dosage and number");
        }

        var label = args.Count > 3 && args[3].Type == DataType.String ? args[3].String : string.Empty;
        if (label.Length > SharedChemMaster.LabelMaxLength)
            return Bool(false, "label is too long");

        if (!TryGetChemMaster(runtime, args, out var target, out _, out var component, out var error))
            return Bool(false, error);

        if (dosage > Comp<ChemMasterComponent>(target).PillDosageLimit)
            return Bool(false, "dosage exceeds the ChemMaster limit");

        var output = _itemSlots.GetItemOrNull(target, SharedChemMaster.OutputSlotName);
        if (output is not { } outputUid || !TryComp<StorageComponent>(outputUid, out _))
            return Bool(false, "no pill output container");

        if (!_solutions.TryGetSolution(target, SharedChemMaster.BufferSolutionName, out _, out var buffer) ||
            buffer.Volume.Float() < dosage * number)
        {
            return Bool(false, "buffer does not contain enough reagents");
        }

        if (_timing.CurTime < runtime.NextAction)
            return Bool(false, "action rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextAction = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        RaiseChemMasterMessage(runtime, target,
            new ChemMasterCreatePillsMessage((uint) dosage, (uint) number, label));
        return Bool(true);
    }

    private DynValue ChemMasterBottle(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetNumber(args, 1, out var dosage) || dosage <= 0 || dosage > uint.MaxValue)
            return Bool(false, "chem_master_bottle expects a positive dosage");

        var label = args.Count > 2 && args[2].Type == DataType.String ? args[2].String : string.Empty;
        if (label.Length > SharedChemMaster.LabelMaxLength)
            return Bool(false, "label is too long");

        if (!TryGetChemMaster(runtime, args, out var target, out _, out var component, out var error))
            return Bool(false, error);

        var output = _itemSlots.GetItemOrNull(target, SharedChemMaster.OutputSlotName);
        if (output is not { } outputUid ||
            !_solutions.TryGetSolution(outputUid, SharedChemMaster.BottleSolutionName, out _, out var bottle) ||
            bottle.AvailableVolume.Float() < dosage)
        {
            return Bool(false, "no bottle output or not enough space");
        }

        if (!_solutions.TryGetSolution(target, SharedChemMaster.BufferSolutionName, out _, out var buffer) ||
            buffer.Volume.Float() < dosage)
        {
            return Bool(false, "buffer does not contain enough reagents");
        }

        if (_timing.CurTime < runtime.NextAction)
            return Bool(false, "action rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextAction = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        RaiseChemMasterMessage(runtime, target, new ChemMasterOutputToBottleMessage((uint) dosage, label));
        return Bool(true);
    }

    private bool TryGetChemMaster(
        CircuitLuaRuntime runtime,
        CallbackArguments args,
        out EntityUid target,
        out ChemMasterComponent chemMaster,
        out CircuitAssemblyComponent assemblyComponent,
        out string error)
    {
        target = default;
        chemMaster = null!;
        assemblyComponent = null!;
        error = string.Empty;

        if (!TryGetHandle(runtime, args, 0, out target))
        {
            error = "invalid entity handle";
            return false;
        }

        if (!CanManipulate(runtime, target, out assemblyComponent, out error))
            return false;

        if (!TryComp<ChemMasterComponent>(target, out var foundChemMaster))
        {
            error = "target is not a ChemMaster";
            return false;
        }

        chemMaster = foundChemMaster;
        return true;
    }

    private void RaiseChemMasterMessage<T>(CircuitLuaRuntime runtime, EntityUid target, T message)
        where T : BoundUserInterfaceMessage
    {
        message.Actor = runtime.Assembly;
        message.UiKey = ChemMasterUiKey.Key;
        RaiseLocalEvent(target, message);
    }

    private static bool TryGetChemMasterAmount(CallbackArguments args, int index, out ChemMasterReagentAmount amount)
    {
        amount = default;
        if (index >= args.Count)
            return false;

        if (args[index].Type == DataType.String &&
            args[index].String.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            amount = ChemMasterReagentAmount.All;
            return true;
        }

        if (args[index].Type != DataType.Number)
            return false;

        var value = (int) args[index].Number;
        if (value != args[index].Number)
            return false;

        foreach (var candidate in Enum.GetValues<ChemMasterReagentAmount>())
        {
            if (candidate == ChemMasterReagentAmount.All)
                continue;

            if ((int) candidate == value)
            {
                amount = candidate;
                return true;
            }
        }

        return false;
    }

    private DynValue AnalyzeMedical(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryAnalyzerTarget(runtime, args, CircuitCapability.MedicalAnalyzer, "medical analyzer",
                out var target, out _, out var error))
        {
            return Bool(false, error);
        }

        if (!TryComp<DamageableComponent>(target, out var damageable) &&
            !HasComp<MobStateComponent>(target))
        {
            return Bool(false, "target is not a medical subject");
        }

        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Name(target)));

        if (TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            table.Set("species", DynValue.NewString(humanoid.Species.ToString()));

        var state = _mobState.IsDead(target)
            ? "Dead"
            : _mobState.IsCritical(target)
                ? "Critical"
                : "Alive";
        table.Set("state", DynValue.NewString(state));

        var (damage, maxHealth) = GetHealth(target);
        table.Set("damage", DynValue.NewNumber(damage));
        table.Set("health", DynValue.NewNumber(MathF.Max(0f, maxHealth - damage)));
        table.Set("max_health", DynValue.NewNumber(maxHealth));
        table.Set("dead", DynValue.NewBoolean(_mobState.IsDead(target)));

        if (damageable != null)
        {
            var damageTypes = NewTable(runtime);
            foreach (var (type, amount) in damageable.Damage.DamageDict)
                damageTypes.Set(type, DynValue.NewNumber(amount.Float()));
            table.Set("damage_types", DynValue.NewTable(damageTypes));

            var damageGroups = NewTable(runtime);
            foreach (var (group, amount) in damageable.DamagePerGroup)
                damageGroups.Set(group, DynValue.NewNumber(amount.Float()));
            table.Set("damage_groups", DynValue.NewTable(damageGroups));
        }

        return DynValue.NewTable(table);
    }

    private DynValue AnalyzeGas(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryAnalyzerTarget(runtime, args, CircuitCapability.GasAnalyzer, "gas analyzer",
                out var target, out _, out var error))
        {
            return Bool(false, error);
        }

        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(Name(target)));

        var environment = _atmo.GetContainingMixture(target, true);
        table.Set("environment", DynValue.NewTable(BuildGasMixTable(runtime, "environment", environment)));

        var mixes = NewTable(runtime);
        var index = 1;
        var scan = new GasAnalyzerScanEvent();
        RaiseLocalEvent(target, scan);

        if (scan.GasMixtures != null)
        {
            foreach (var (name, mixture) in scan.GasMixtures)
            {
                if (mixture == null)
                    continue;

                mixes.Set(index++, DynValue.NewTable(BuildGasMixTable(runtime, name, mixture)));
            }
        }
        else if (TryComp<NodeContainerComponent>(target, out var nodeContainer))
        {
            foreach (var (nodeName, node) in nodeContainer.Nodes)
            {
                if (node is not PipeNode pipe || pipe.Air.Volume <= 0f)
                    continue;

                var pipeAir = pipe.Air.Clone();
                pipeAir.Multiply(pipe.Volume / pipe.Air.Volume);
                pipeAir.Volume = pipe.Volume;
                mixes.Set(index++, DynValue.NewTable(BuildGasMixTable(runtime, nodeName, pipeAir)));
            }
        }

        table.Set("mixes", DynValue.NewTable(mixes));
        table.Set("device_flipped", DynValue.NewBoolean(scan.DeviceFlipped));
        return DynValue.NewTable(table);
    }

    private Table BuildGasMixTable(CircuitLuaRuntime runtime, string name, GasMixture? mixture)
    {
        var table = NewTable(runtime);
        table.Set("name", DynValue.NewString(name));

        var gases = NewTable(runtime);
        var gasIndex = 1;

        if (mixture != null)
        {
            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                var amount = mixture[i];
                if (amount <= 0.01f)
                    continue;

                var gas = _atmo.GetGas(i);
                var entry = NewTable(runtime);
                entry.Set("id", DynValue.NewString(gas.ID));
                entry.Set("name", DynValue.NewString(Loc.GetString(gas.Name)));
                entry.Set("amount", DynValue.NewNumber(amount));
                entry.Set("color", DynValue.NewString(gas.Color));
                gases.Set(gasIndex++, DynValue.NewTable(entry));
            }

            table.Set("volume", DynValue.NewNumber(mixture.Volume));
            table.Set("pressure", DynValue.NewNumber(mixture.Pressure));
            table.Set("temperature", DynValue.NewNumber(mixture.Temperature));
        }
        else
        {
            table.Set("volume", DynValue.NewNumber(0));
            table.Set("pressure", DynValue.NewNumber(0));
            table.Set("temperature", DynValue.NewNumber(0));
        }

        table.Set("gases", DynValue.NewTable(gases));
        return table;
    }

    private bool TryAnalyzerTarget(
        CircuitLuaRuntime runtime,
        CallbackArguments args,
        CircuitCapability capability,
        string label,
        out EntityUid target,
        out CircuitAssemblyComponent component,
        out string error)
    {
        target = default;
        component = null!;
        error = string.Empty;

        if (!TryGetHandle(runtime, args, 0, out target))
        {
            error = "invalid entity handle";
            return false;
        }

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var assemblyComponent))
        {
            error = "assembly is missing";
            return false;
        }

        component = assemblyComponent;

        if (!component.Capabilities.HasFlag(capability))
        {
            error = $"no {label} module";
            return false;
        }

        if (!component.Capabilities.HasFlag(CircuitCapability.Sensor))
        {
            error = "no sensor module";
            return false;
        }

        if (!component.Powered)
        {
            error = "no power";
            return false;
        }

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.SensorRange))
        {
            error = "target is out of sensor range or not visible";
            return false;
        }

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
        {
            error = "no power";
            return false;
        }

        return true;
    }

    private Table BuildStringList(CircuitLuaRuntime runtime, IEnumerable<string> values)
    {
        var table = NewTable(runtime);
        var index = 1;
        foreach (var value in values)
            table.Set(index++, DynValue.NewString(value));
        return table;
    }

    private DynValue Use(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return Bool(false, "assembly is missing");

        if (!component.Powered)
            return Bool(false, "no power");

        if (!_actionBlocker.CanInteract(runtime.Assembly, target))
            return Bool(false, "cannot interact");

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.HandRange))
            return Bool(false, "target is out of reach or not visible");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var ev = new ActivateInWorldEvent(runtime.Assembly, target, true);
        RaiseLocalEvent(target, ev);
        return Bool(ev.Handled);
    }

    private DynValue Trigger(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Manipulator) ||
            !component.Powered)
        {
            return Bool(false, "a powered manipulator is required");
        }

        if (!_hands.TryGetActiveItem((runtime.Assembly, (HandsComponent?) null), out var held) ||
            held.Value != target)
        {
            return Bool(false, "target must be in the active hand");
        }

        if (_timing.CurTime < runtime.NextSignal)
            return Bool(false, "signal rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextSignal = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        var ev = new UseInHandEvent(runtime.Assembly);
        RaiseLocalEvent(target, ev);
        return Bool(ev.Handled);
    }

    private DynValue SendSignal(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        var state = true;
        if (args.Count > 0)
        {
            if (args[0].Type == DataType.Boolean)
                state = args[0].Boolean;
            else if (args[0].Type == DataType.Number)
                state = args[0].Number != 0;
            else
                return Bool(false, "send_signal expects a boolean");
        }

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Radio) ||
            component.Radio is null)
        {
            return Bool(false, "no radio module");
        }

        if (!component.Powered)
            return Bool(false, "no power");

        if (!TryComp<DeviceLinkSourceComponent>(runtime.Assembly, out var source))
            return Bool(false, "no signal transmitter");

        var port = "Pressed";
        if (args.Count > 1 && args[1].Type == DataType.String)
            port = args[1].String;

        if (!_deviceLink.GetSourcePortIds((runtime.Assembly, source)).Contains(port))
            return Bool(false, $"unknown signal port '{port}'");

        if (_timing.CurTime < runtime.NextSignal)
            return Bool(false, "signal rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextSignal = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        _deviceLink.SendSignal(runtime.Assembly, port, state, source);
        return Bool(true);
    }

    private DynValue Inject(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!CanManipulate(runtime, target, out var component, out var error))
            return Bool(false, error);

        if (!_hands.TryGetActiveItem((runtime.Assembly, (HandsComponent?) null), out var held) ||
            !TryComp<InjectorComponent>(held.Value, out var injector))
        {
            return Bool(false, "no injector in the active hand");
        }

        if (!TryComp<SolutionContainerManagerComponent>(held.Value, out var manager) ||
            !_solutions.TryGetSolution((held.Value, manager), injector.SolutionName, out _, out var solution))
        {
            return Bool(false, "injector has no solution");
        }

        if (!injector.InjectOnly && injector.ToggleState != InjectorToggleMode.Inject)
        {
            if (solution.Volume <= 0)
                return Bool(false, "injector is empty");

            _injector.SetMode((held.Value, injector), InjectorToggleMode.Inject);
        }

        if (_timing.CurTime < runtime.NextAction)
            return Bool(false, "action rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextAction = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        var ev = new AfterInteractEvent(
            runtime.Assembly,
            held.Value,
            target,
            Transform(target).Coordinates,
            canReach: true);
        RaiseLocalEvent(held.Value, ev);
        return Bool(ev.Handled);
    }

    private DynValue UseItem(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!CanManipulate(runtime, target, out var component, out var error))
            return Bool(false, error);

        if (!_hands.TryGetActiveItem((runtime.Assembly, (HandsComponent?) null), out var held))
            return Bool(false, "no item in hand");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        var ev = new InteractUsingEvent(runtime.Assembly, held.Value, target, Transform(target).Coordinates);
        RaiseLocalEvent(target, ev);
        return Bool(ev.Handled);
    }

    private DynValue Attack(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!CanManipulate(runtime, target, out var component, out var error))
            return Bool(false, error);

        if (!_melee.TryGetWeapon(runtime.Assembly, out var weaponUid, out var melee))
            return Bool(false, "no melee weapon");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        return Bool(_melee.AttemptLightAttack(runtime.Assembly, weaponUid, melee, target));
    }

    private DynValue Shoot(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!CanManipulate(runtime, target, out var component, out var error))
            return Bool(false, error);

        if (!_hands.TryGetActiveItem((runtime.Assembly, (HandsComponent?) null), out var held) ||
            !TryComp<GunComponent>(held.Value, out var gun))
        {
            return Bool(false, "no gun in hand");
        }

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, MathF.Max(component.SensorRange, 20f)))
            return Bool(false, "target is out of sight");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        _gun.AttemptShoot(runtime.Assembly, held.Value, gun, Transform(target).Coordinates, target);
        return Bool(true);
    }

    private DynValue Beakers(CircuitLuaRuntime runtime)
    {
        var table = NewTable(runtime);
        var index = 1;

        for (var slot = 1; slot <= 4; slot++)
        {
            if (!TryGetReagentSolution(runtime, slot, out _, out var solution, out _))
                continue;

            var entry = BuildSolutionTable(runtime, slot, solution);
            entry.Set("slot", DynValue.NewNumber(slot));
            if (_assembly.TryGetBeaker(runtime.Assembly, slot, out var beaker))
                entry.Set("handle", DynValue.NewNumber(runtime.GetHandle(beaker)));
            table.Set(index++, DynValue.NewTable(entry));
        }

        return DynValue.NewTable(table);
    }

    private DynValue Reagents(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        var slot = GetOptionalIndex(args, 0, 1);
        if (!TryGetReagentSolution(runtime, slot, out _, out var solution, out var error))
            return Bool(false, error);

        var table = BuildSolutionTable(runtime, slot, solution);
        if (_assembly.TryGetBeaker(runtime.Assembly, slot, out var beaker))
            table.Set("handle", DynValue.NewNumber(runtime.GetHandle(beaker)));

        return DynValue.NewTable(table);
    }

    private Table BuildSolutionTable(CircuitLuaRuntime runtime, int? slot, Solution solution)
    {
        var table = NewTable(runtime);
        if (slot is { } slotIndex)
            table.Set("slot", DynValue.NewNumber(slotIndex));
        table.Set("volume", DynValue.NewNumber(solution.Volume.Float()));
        table.Set("max_volume", DynValue.NewNumber(solution.MaxVolume.Float()));
        table.Set("temperature", DynValue.NewNumber(solution.Temperature));

        var contents = NewTable(runtime);
        var index = 1;
        foreach (var reagent in solution.Contents)
        {
            var entry = NewTable(runtime);
            entry.Set("id", DynValue.NewString(reagent.Reagent.Prototype));
            entry.Set("amount", DynValue.NewNumber(reagent.Quantity.Float()));
            contents.Set(index++, DynValue.NewTable(entry));
        }

        table.Set("contents", DynValue.NewTable(contents));
        return table;
    }

    private DynValue TransferBetween(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetHandle(runtime, args, 0, out var source))
            return Bool(false, "invalid source handle");

        if (!TryGetHandle(runtime, args, 1, out var target))
            return Bool(false, "invalid target handle");

        if (!TryGetNumber(args, 2, out var amount) || amount <= 0)
            return Bool(false, "transfer expects a positive amount");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component) ||
            !component.Capabilities.HasFlag(CircuitCapability.Manipulator) ||
            !component.Powered)
        {
            return Bool(false, "a powered manipulator is required");
        }

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, source, component.HandRange) ||
            !_interaction.InRangeUnobstructed(runtime.Assembly, target, component.HandRange))
        {
            return Bool(false, "source or target is out of reach or not visible");
        }

        if (!TryGetAnySolution(source, out var sourceSolution, out _, out var sourceError))
            return Bool(false, sourceError);

        if (!TryGetAnySolution(target, out var targetSolution, out _, out var targetError))
            return Bool(false, targetError);

        if (_timing.CurTime < runtime.NextTransfer)
            return Bool(false, "transfer rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextTransfer = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        var quantity = FixedPoint2.New((float) Math.Min(amount, 1000d));
        var moved = _solutions.TryTransferSolution(targetSolution, sourceSolution.Comp.Solution, quantity);
        if (moved)
            _solutions.UpdateChemicals(sourceSolution);

        return Bool(moved);
    }

    private bool TryGetAnySolution(
        EntityUid target,
        out Entity<SolutionComponent> solutionEntity,
        out string name,
        out string error)
    {
        solutionEntity = default;
        name = string.Empty;
        error = string.Empty;

        if (!TryComp<SolutionContainerManagerComponent>(target, out var manager))
        {
            error = "target has no solution containers";
            return false;
        }

        if (TryComp<InjectorComponent>(target, out var injector))
            name = injector.SolutionName;
        else if (TryComp<RefillableSolutionComponent>(target, out var refillable))
            name = refillable.Solution;
        else if (TryComp<DrainableSolutionComponent>(target, out var drainable))
            name = drainable.Solution;
        else
        {
            var names = _solutions.EnumerateSolutions((target, manager))
                .Where(entry => entry.Name != null)
                .Select(entry => entry.Name!)
                .ToList();

            if (names.Count == 1)
                name = names[0];
        }

        if (string.IsNullOrEmpty(name))
        {
            error = "multiple solutions; specify a solution name";
            return false;
        }

        if (!_solutions.TryGetSolution((target, manager), name, out var found, out _))
        {
            error = "solution not found";
            return false;
        }

        solutionEntity = found!.Value;
        return true;
    }

    private DynValue Transfer(CircuitLuaRuntime runtime, CallbackArguments args, bool toTarget)
    {
        if (!TryGetHandle(runtime, args, 0, out var target))
            return Bool(false, "invalid entity handle");

        if (!TryGetNumber(args, 1, out var amount) || amount <= 0)
            return Bool(false, "transfer expects a positive amount");

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
            return Bool(false, "assembly is missing");

        if (!component.Powered)
            return Bool(false, "no power");

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.HandRange))
            return Bool(false, "target is out of reach or not visible");

        var slot = GetOptionalIndex(args, 2, 1);
        if (!TryGetReagentSolution(runtime, slot, out var beakerSolution, out var beaker, out var error))
            return Bool(false, error);

        if (!TryGetTargetSolution(target, toTarget, out var targetSolution, out var targetError))
            return Bool(false, targetError);

        if (_timing.CurTime < runtime.NextTransfer)
            return Bool(false, "transfer rate limited");

        if (!_assembly.TryConsumePower(runtime.Assembly, component.ActionPowerCost, component))
            return Bool(false, "no power");

        runtime.NextTransfer = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        var quantity = FixedPoint2.New((float) Math.Min(amount, 1000d));
        var moved = toTarget
            ? _solutions.TryTransferSolution(targetSolution, beaker, quantity)
            : _solutions.TryTransferSolution(beakerSolution, targetSolution.Comp.Solution, quantity);

        return Bool(moved);
    }

    private bool CanManipulate(
        CircuitLuaRuntime runtime,
        EntityUid target,
        out CircuitAssemblyComponent component,
        out string error)
    {
        error = string.Empty;
        component = default!;

        if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var assemblyComponent))
        {
            error = "assembly is missing";
            return false;
        }

        component = assemblyComponent;

        if (!component.Capabilities.HasFlag(CircuitCapability.Manipulator))
        {
            error = "no manipulator module";
            return false;
        }

        if (!component.Powered)
        {
            error = "no power";
            return false;
        }

        if (!_interaction.InRangeUnobstructed(runtime.Assembly, target, component.HandRange))
        {
            error = "target is out of reach or not visible";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the nearest entity matching every supplied filter. Unlike <see cref="ScanEntities"/>
    /// this is not limited by the scan-result cap: single-result lookups such as <c>find</c> must
    /// be able to see a named target even when many other entities are closer.
    /// </summary>
    private bool TryFindNearestInRange(
        CircuitLuaRuntime runtime,
        float radius,
        string? kind,
        bool humanoidOnly,
        string? nameContains,
        out EntityUid found)
    {
        found = EntityUid.Invalid;
        var origin = _transform.GetMapCoordinates(runtime.Assembly);
        _lookupSet.Clear();
        _lookup.GetEntitiesInRange(runtime.Assembly, radius, _lookupSet, LookupFlags.Uncontained);

        var bestDistance = float.MaxValue;

        foreach (var uid in _lookupSet)
        {
            if (uid == runtime.Assembly || Deleted(uid))
                continue;

            if (kind != null && GetKind(uid) != kind)
                continue;

            if (humanoidOnly && !HasComp<HumanoidAppearanceComponent>(uid))
                continue;

            // Name matching is much cheaper than a line-of-sight raycast, so do it first.
            if (nameContains != null &&
                !Name(uid).Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var other = _transform.GetMapCoordinates(uid);
            if (origin.MapId != other.MapId)
                continue;

            var distance = (origin.Position - other.Position).Length();
            if (distance >= bestDistance)
                continue;

            if (!_interaction.InRangeUnobstructed(runtime.Assembly, uid, radius))
                continue;

            bestDistance = distance;
            found = uid;
        }

        return found.IsValid();
    }

    private bool TryConsumeSensorQuery(CircuitLuaRuntime runtime)
    {
        var now = _timing.CurTime;
        var elapsed = now - runtime.LastSensorQuery;
        if (elapsed > TimeSpan.Zero)
        {
            runtime.SensorQueryTokens = Math.Min(
                CircuitLuaRuntime.MaxSensorQueryTokens,
                runtime.SensorQueryTokens + elapsed.TotalSeconds * CircuitLuaRuntime.SensorQueriesPerSecond);
            runtime.LastSensorQuery = now;
        }

        if (runtime.SensorQueryTokens < 1)
            return false;

        runtime.SensorQueryTokens -= 1;
        return true;
    }

    /// <summary>
    /// True while the assembly has a do-after that has not completed or been cancelled. Used by
    /// the Lua synchronous action wrappers.
    /// </summary>
    private DynValue ActionBusy(CircuitLuaRuntime runtime)
    {
        if (!TryComp<DoAfterComponent>(runtime.Assembly, out var component))
            return DynValue.NewBoolean(false);

        foreach (var doAfter in component.DoAfters.Values)
        {
            if (!doAfter.Completed && !doAfter.Cancelled)
                return DynValue.NewBoolean(true);
        }

        return DynValue.NewBoolean(false);
    }

    private DynValue ActionMarker(CircuitLuaRuntime runtime)
    {
        return DynValue.NewNumber(TryComp<DoAfterComponent>(runtime.Assembly, out var component)
            ? component.NextId
            : 0);
    }

    private DynValue ActionStatus(CircuitLuaRuntime runtime, CallbackArguments args)
    {
        if (!TryGetNumber(args, 0, out var markerValue))
            return DynValue.NewString("none");

        var marker = (ushort) (int) markerValue;

        if (!TryComp<DoAfterComponent>(runtime.Assembly, out var component))
            return DynValue.NewString("none");

        var running = false;
        foreach (var doAfter in component.DoAfters.Values)
        {
            if (!IsDoAfterAtOrAfter(doAfter.Index, marker))
                continue;

            if (doAfter.Cancelled)
                return DynValue.NewString("cancelled");

            if (!doAfter.Completed)
                running = true;
        }

        if (running)
            return DynValue.NewString("running");

        // The do-after was already completed and removed from the component.
        if (IsDoAfterAfter(component.NextId, marker))
            return DynValue.NewString("done");

        return DynValue.NewString("none");
    }

    /// <summary>
    /// True for the do-after index that was next in line when the marker was taken.
    /// </summary>
    private static bool IsDoAfterAtOrAfter(ushort value, ushort marker)
    {
        return value == marker || IsDoAfterAfter(value, marker);
    }

    /// <summary>
    /// Compares ushort do-after indices across the normal wrap point.
    /// </summary>
    private static bool IsDoAfterAfter(ushort value, ushort marker)
    {
        return value != marker && (ushort) (value - marker) < 0x8000;
    }

    private List<(EntityUid Uid, float Distance)> ScanEntities(
        CircuitLuaRuntime runtime,
        float radius,
        string? kind,
        int maxResults)
    {
        var result = new List<(EntityUid, float)>();
        _lookupSet.Clear();
        _lookup.GetEntitiesInRange(runtime.Assembly, radius, _lookupSet, LookupFlags.Uncontained);

        var origin = _transform.GetMapCoordinates(runtime.Assembly);

        foreach (var uid in _lookupSet)
        {
            if (uid == runtime.Assembly || Deleted(uid))
                continue;

            if (kind != null && GetKind(uid) != kind)
                continue;

            if (!_interaction.InRangeUnobstructed(runtime.Assembly, uid, radius))
                continue;

            var other = _transform.GetMapCoordinates(uid);
            if (origin.MapId != other.MapId)
                continue;

            result.Add((uid, (origin.Position - other.Position).Length()));
        }

        result.Sort((a, b) => a.Item2.CompareTo(b.Item2));

        if (maxResults > 0 && result.Count > maxResults)
            result.RemoveRange(maxResults, result.Count - maxResults);

        return result;
    }

    private bool TryGetReagentSolution(
        CircuitLuaRuntime runtime,
        int index,
        out Entity<SolutionComponent> solutionEntity,
        out Solution solution,
        out string error)
    {
        solutionEntity = default;
        solution = null!;
        error = string.Empty;

        if (!_assembly.TryGetBeaker(runtime.Assembly, index, out var beaker))
        {
            error = $"beaker slot {index} is empty";
            return false;
        }

        if (!TryComp<SolutionContainerManagerComponent>(beaker, out var manager))
        {
            error = "beaker has no solution container";
            return false;
        }

        var name = "beaker";
        if (TryComp<RefillableSolutionComponent>(beaker, out var refillable))
            name = refillable.Solution;
        else if (TryComp<DrainableSolutionComponent>(beaker, out var drainable))
            name = drainable.Solution;

        if (!_solutions.TryGetSolution((beaker, manager), name, out var found, out var foundSolution))
        {
            error = "beaker solution not found";
            return false;
        }

        solutionEntity = found!.Value;
        solution = foundSolution!;
        return true;
    }

    private bool TryGetTargetSolution(
        EntityUid target,
        bool refillable,
        out Entity<SolutionComponent> solutionEntity,
        out string error)
    {
        solutionEntity = default;
        error = string.Empty;

        if (!TryComp<SolutionContainerManagerComponent>(target, out var manager))
        {
            error = "target has no solution container";
            return false;
        }

        // Syringes and droppers keep their payload in the injector solution rather than exposing
        // RefillableSolution/DrainableSolution. Let transfer_to/from fill or empty them directly.
        if (TryComp<InjectorComponent>(target, out var injector) &&
            _solutions.TryGetSolution((target, manager), injector.SolutionName, out var injectorSolution, out _))
        {
            solutionEntity = injectorSolution!.Value;
            return true;
        }

        var name = "default";
        if (refillable && TryComp<RefillableSolutionComponent>(target, out var refill))
            name = refill.Solution;
        else if (!refillable && TryComp<DrainableSolutionComponent>(target, out var drain))
            name = drain.Solution;
        else
        {
            error = refillable ? "target is not refillable" : "target is not drainable";
            return false;
        }

        if (!_solutions.TryGetSolution((target, manager), name, out var found, out _))
        {
            error = "target solution not found";
            return false;
        }

        solutionEntity = found!.Value;
        return true;
    }

    private (float Damage, float MaxHealth) GetHealth(EntityUid uid)
    {
        var damage = TryComp<DamageableComponent>(uid, out var damageable)
            ? damageable.TotalDamage.Float()
            : 0f;

        var maxHealth = 0f;
        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
        {
            foreach (var threshold in thresholds.Thresholds.Keys)
                maxHealth = MathF.Max(maxHealth, threshold.Float());
        }

        return (damage, maxHealth);
    }

    private (float Current, float Max) GetPower(EntityUid assembly)
    {
        return _assembly.TryGetPower(assembly, out var current, out var max)
            ? (current, max)
            : (0f, 0f);
    }

    private bool TryGetIdInfo(EntityUid uid, out string? name, out string? job)
    {
        name = null;
        job = null;

        if (!_idCardSystem.TryFindIdCard(uid, out var idCard))
            return false;

        name = idCard.Comp.FullName;
        job = idCard.Comp.LocalizedJobTitle;
        return true;
    }

    private string GetKind(EntityUid uid)
    {
        if (HasComp<MobStateComponent>(uid))
            return "mob";

        if (HasComp<DoorComponent>(uid))
            return "door";

        // Many structures (gas pipes, vents, scrubbers, etc.) inherit from BaseItem and therefore
        // have ItemComponent, but they are installable machines rather than loose items. Anchored
        // or anchorable entities are structures even if they are not currently anchored (for
        // example, a spawned pipe item or a machine moved by an admin). Constructible machines
        // such as lathes and dispensers use MachineComponent instead. Plain anchored geometry
        // (walls, windows) stays "object" so structure scans do not return every nearby wall.
        var installed = Transform(uid).Anchored || HasComp<AnchorableComponent>(uid);

        if (HasComp<ItemComponent>(uid))
            return installed ? "structure" : "item";

        if (HasComp<MachineComponent>(uid))
            return "structure";

        if (HasComp<SolutionContainerManagerComponent>(uid))
            return "reagent";

        return "object";
    }

    private string Name(EntityUid uid)
    {
        return MetaData(uid).EntityName;
    }

    private Table NewTable(CircuitLuaRuntime runtime)
    {
        return new Table(runtime.Script);
    }

    private static DynValue Bool(bool success, string? reason = null)
    {
        return reason == null
            ? DynValue.NewBoolean(success)
            : DynValue.NewTuple(DynValue.NewBoolean(success), DynValue.NewString(reason));
    }

    private static int GetOptionalIndex(CallbackArguments args, int index, int fallback)
    {
        if (index >= args.Count || args[index].Type != DataType.Number)
            return fallback;

        return (int) args[index].Number;
    }

    private static bool TryGetNumber(CallbackArguments args, int index, out double value)
    {
        value = 0;
        if (index >= args.Count || args[index].Type != DataType.Number)
            return false;

        value = args[index].Number;
        return true;
    }

    private static bool TryGetString(CallbackArguments args, int index, out string value)
    {
        value = string.Empty;
        if (index >= args.Count || args[index].Type != DataType.String)
            return false;

        value = args[index].String;
        return true;
    }

    private static bool TryGetHandle(
        CircuitLuaRuntime runtime,
        CallbackArguments args,
        int index,
        out EntityUid uid)
    {
        uid = default;
        if (index >= args.Count || args[index].Type != DataType.Number)
            return false;

        return runtime.TryResolve((int) args[index].Number, out uid);
    }
}
